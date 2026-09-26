using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Vehicle-specific steps for <see cref="AutonomousVehicleSession"/>.
    /// The session owns the lifecycle, the coroutine, and destruction of <see cref="Body"/>.
    /// Implementations send their own messages and release their own side state.
    /// They do not consume the item, destroy <see cref="Body"/>, or send the end message
    /// from anywhere except <see cref="NotifyClientsSessionEnded"/>.
    /// </summary>
    internal interface IAutonomousVehicleLogic
    {
        string LogName { get; }

        /// <summary>
        /// Spawns the craft and stores it as <see cref="Body"/>.
        /// Returns false when nothing was spawned. The session has already consumed the item.
        /// </summary>
        bool TrySpawn(PlayerInventory inventory);

        void Arm(PlayerInventory inventory);

        /// <summary>
        /// Tells clients the session started. The session records that itself,
        /// before this is called. The summoner was passed to <see cref="TrySpawn"/>.
        /// </summary>
        void Announce();

        GameObject Body { get; }

        /// <summary>
        /// True when the craft or its flight driver is gone.
        /// A neutralize callback must set <see cref="IsNeutralized"/> before it removes the driver,
        /// or the session treats the craft as lost and destroys it instead of waiting out the crash.
        /// </summary>
        bool IsLost { get; }

        bool HasArrived { get; }
        bool IsNeutralized { get; }

        /// <summary>
        /// True when a normal departure has finished and the body is still the session's to destroy.
        /// A craft that destroys itself can leave this false; <see cref="IsLost"/> ends the wait.
        /// </summary>
        bool HasDeparted { get; }

        /// <summary>Runs when the departure phase is pumped, not when the session builds the phase.</summary>
        void BeginDeparture();

        float ArrivalTimeout { get; }
        float EngagementDuration { get; }
        float FireInterval { get; }
        float OpeningFireDelay { get; }
        float DepartureTimeout { get; }
        float NeutralizedTimeout { get; }

        /// <summary>
        /// Called once when the attack starts, before the first shot.
        /// Snapshot rules that must stay fixed for the burst, such as friendly fire.
        /// </summary>
        void BeginEngagement();

        void Fire(PlayerInventory inventory);

        /// <summary>True when the crash or mayday started by arming has finished, or cannot run.</summary>
        bool IsNeutralizedComplete { get; }

        void NotifyClientsSessionEnded();

        /// <summary>
        /// Item-specific teardown on every exit, including a failed spawn and a hole change.
        /// Release locks here. Do not destroy <see cref="Body"/> or notify clients.
        /// Must be safe to call only for a session this logic actually started.
        /// </summary>
        void OnSessionFinished();
    }

    /// <summary>
    /// Server run for a vehicle that flies in, fires on its own, then leaves or crashes.
    /// Composed by a bridge rather than inherited: the piloted AC130 bridge stays on
    /// <see cref="NetworkBridgeBase"/> and can own one of these for an autonomous mode.
    /// One coroutine on the host bridge, so stopping that coroutine stops every phase.
    /// </summary>
    internal sealed class AutonomousVehicleSession
    {
        private readonly MonoBehaviour _host;
        private readonly IAutonomousVehicleLogic _logic;
        private Coroutine _routine;
        private bool _clientsNotified;

        public bool IsActive { get; private set; }

        public AutonomousVehicleSession(MonoBehaviour host, IAutonomousVehicleLogic logic)
        {
            _host = host;
            _logic = logic;
        }

        /// <summary>
        /// Starts the run and consumes the equipped item.
        /// Callers validate equipment and any global lock before this, and release that
        /// lock only from <see cref="IAutonomousVehicleLogic.OnSessionFinished"/>.
        /// </summary>
        public void Begin(PlayerInventory inventory)
        {
            if (IsActive || inventory == null)
                return;

            // Set before StartCoroutine. The first yield is not a safe guard: a yield
            // added above it would let a second use start on the same frame.
            IsActive = true;
            _routine = _host.StartCoroutine(Run(inventory));
        }

        /// <summary>
        /// Disconnect or a destroyed approach. Sends the end message when clients were told
        /// the session had started.
        /// </summary>
        public void Abort()
        {
            FinishAborted();
        }

        /// <summary>
        /// Hole change. Destroys the craft and does not broadcast an end message;
        /// each client clears its own local session in its hole cleanup.
        /// </summary>
        public void EndForHoleChange()
        {
            FinishSilent();
        }

        private IEnumerator Run(PlayerInventory inventory)
        {
            ItemHelper.ConsumeEquippedItem(inventory);

            if (!_logic.TrySpawn(inventory))
            {
                IssaPluginPlugin.Log.LogError($"[{_logic.LogName}] Failed to spawn.");
                FinishSilent();
                yield break;
            }

            _logic.Arm(inventory);
            _clientsNotified = true;
            _logic.Announce();

            foreach (
                object step in EachStep(
                    WaitWhile(
                        _logic.ArrivalTimeout,
                        () => !_logic.IsLost && !_logic.HasArrived && !_logic.IsNeutralized
                    )
                )
            )
                yield return step;

            if (_logic.IsLost)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[{_logic.LogName}] Destroyed before arriving — aborting session."
                );
                FinishAborted();
                yield break;
            }

            if (_logic.IsNeutralized)
            {
                IssaPluginPlugin.Log.LogInfo($"[{_logic.LogName}] Shot down during approach.");
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[{_logic.LogName}] Arrived — beginning attack phase."
                );
                foreach (object step in EachStep(Engage(inventory)))
                    yield return step;
            }

            // Latched here, after the attack. A hit during departure does not change branches.
            foreach (object step in EachStep(Exit(_logic.IsNeutralized)))
                yield return step;

            FinishCompleted();
        }

        private IEnumerator Engage(PlayerInventory inventory)
        {
            float duration = _logic.EngagementDuration;
            float fireInterval = _logic.FireInterval;
            float openingDelay = _logic.OpeningFireDelay;
            _logic.BeginEngagement();

            float elapsed = 0f;
            float cooldown = openingDelay;
            while (elapsed < duration && _logic.Body != null && !_logic.IsNeutralized)
            {
                elapsed += Time.deltaTime;
                cooldown -= Time.deltaTime;

                if (cooldown <= 0f)
                {
                    cooldown = fireInterval;
                    _logic.Fire(inventory);
                }

                yield return null;
            }
        }

        private IEnumerator Exit(bool neutralized)
        {
            if (neutralized)
            {
                foreach (
                    object step in EachStep(
                        WaitWhile(_logic.NeutralizedTimeout, () => !_logic.IsNeutralizedComplete)
                    )
                )
                    yield return step;
                yield break;
            }

            if (_logic.IsLost)
                yield break;

            // Runs on the first step of this phase, not when Exit() is constructed.
            _logic.BeginDeparture();
            foreach (
                object step in EachStep(
                    WaitWhile(
                        _logic.DepartureTimeout,
                        () => !_logic.IsLost && !_logic.HasDeparted
                    )
                )
            )
                yield return step;
        }

        private void FinishSilent()
        {
            Finish(notifyClients: false, logComplete: false);
        }

        private void FinishAborted()
        {
            Finish(notifyClients: true, logComplete: false);
        }

        private void FinishCompleted()
        {
            Finish(notifyClients: true, logComplete: true);
        }

        private void Finish(bool notifyClients, bool logComplete)
        {
            if (!IsActive)
                return;

            // Cleared before notify so a message that re-enters Abort cannot finish twice.
            IsActive = false;

            if (_routine != null)
            {
                _host.StopCoroutine(_routine);
                _routine = null;
            }

            GameObject body = _logic.Body;
            if (body != null)
                NetworkServer.Destroy(body);

            if (notifyClients && _clientsNotified)
                _logic.NotifyClientsSessionEnded();
            _clientsNotified = false;

            _logic.OnSessionFinished();

            if (logComplete)
                IssaPluginPlugin.Log.LogInfo($"[{_logic.LogName}] Session complete.");
        }

        /// <summary>
        /// Frames of <paramref name="phase"/>, yielded by <see cref="Run"/> so the whole
        /// session stays one coroutine. Stopping that coroutine stops every phase.
        /// </summary>
        private static IEnumerable EachStep(IEnumerator phase)
        {
            while (phase.MoveNext())
                yield return phase.Current;
        }

        private static IEnumerator WaitWhile(float timeoutSeconds, System.Func<bool> shouldContinue)
        {
            float elapsed = 0f;
            while (shouldContinue() && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
