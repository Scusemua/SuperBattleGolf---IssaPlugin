using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Handles the Moon item's server-authoritative session:
    ///   1. Wielder uses item → ClientUse() sends MoonUseMessage to server.
    ///   2. Server validates, computes positions, broadcasts MoonBeginMessage to all.
    ///   3. After (ApproachDuration + SuckDuration), server broadcasts MoonEndMessage.
    ///
    /// Message flow:
    ///   Client → Server : MoonUseMessage
    ///   Server → All    : MoonBeginMessage
    ///   Server → All    : MoonEndMessage
    ///
    /// Global one-at-a-time lock via GlobalSessionLock&lt;MoonNetworkBridge&gt;.
    /// </summary>
    public class MoonNetworkBridge : NetworkBridgeBase
    {
        // ── Server-side session state (wielder's bridge only) ─────────────────

        private bool _serverSessionActive;
        private NetworkConnectionToClient _wielderConn;
        private Coroutine _serverCoroutine;
        private int _wielderSlot = -1;
        private Vector3 _serverImpactPos;

        private readonly HashSet<Rigidbody> _seenRigidbodies = new HashSet<Rigidbody>();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Update()
        {
            if (!isOwned)
                return;

            MoonClientLogic.UpdateAll();
        }

        private void OnGUI()
        {
            if (!isOwned)
                return;

            MoonClientLogic.DrawWarningGui();
        }

        // ── Client — activating the item ──────────────────────────────────────

        public void ClientUse()
        {
            if (!isOwned)
                return;

            NetworkClient.Send(new MoonUseMessage());
        }

        // ── Server — handling use request ─────────────────────────────────────

        public void ServerHandleUse(NetworkConnectionToClient conn, MoonUseMessage msg)
        {
            if (!isServer)
                return;

            if (!GlobalSessionLock<MoonNetworkBridge>.TryAcquire(this))
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[Moon] Server: session already active — ignoring use request."
                );
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            int slot =
                inventory != null
                    ? ItemRegistry.FindSlotIndex(inventory, ItemRegistry.MoonItemType)
                    : -1;
            if (slot < 0)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Moon] Server: wielder does not have the Moon item."
                );
                GlobalSessionLock<MoonNetworkBridge>.Release();
                return;
            }
            _wielderSlot = slot;

            if (GolfHoleManager.MainHole == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Moon] Server: GolfHoleManager.MainHole is null — aborting."
                );
                GlobalSessionLock<MoonNetworkBridge>.Release();
                _wielderSlot = -1;
                return;
            }

            Vector3 holePos = GolfHoleManager.MainHole.transform.position;

            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 xzDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            float spawnDist = ModConfig.Moon.SpawnDistance.Value;
            float spawnHeight = ModConfig.Moon.SpawnHeight.Value;
            float initialScale = ModConfig.Moon.InitialScale.Value;
            float finalScale = ModConfig.Moon.FinalScale.Value;
            float impactHeight = ModConfig.Moon.ImpactHeight.Value;
            float approachDuration = ModConfig.Moon.ApproachDuration.Value;
            float suckDuration = ModConfig.Moon.SuckDuration.Value;

            Vector3 moonSpawnPos =
                holePos
                + /* xzDir * spawnDist + */
                Vector3.up * spawnHeight;
            Vector3 moonImpactPos = holePos + Vector3.up * impactHeight;

            _serverImpactPos = moonImpactPos;
            _wielderConn = conn;
            _serverSessionActive = true;

            uint wielderNetId = GetComponent<NetworkIdentity>().netId;

            IssaPluginPlugin.Log.LogInfo(
                $"[Moon] Server: session started. wielder={wielderNetId} spawnPos={moonSpawnPos} impactPos={moonImpactPos}"
            );

            NetworkServer.SendToAll(
                new MoonBeginMessage
                {
                    WielderNetId = wielderNetId,
                    MoonSpawnPos = moonSpawnPos,
                    MoonImpactPos = moonImpactPos,
                    ApproachDuration = approachDuration,
                    SuckDuration = suckDuration,
                    InitialScale = initialScale,
                    FinalScale = finalScale,
                }
            );

            _serverCoroutine = StartCoroutine(
                ServerSessionCoroutine(approachDuration, suckDuration)
            );
        }

        // ── Server — session coroutine ────────────────────────────────────────

        private System.Collections.IEnumerator ServerSessionCoroutine(
            float approachDuration,
            float suckDuration
        )
        {
            yield return new WaitForSeconds(approachDuration);

            if (!_serverSessionActive)
                yield break;

            // Suck phase: pull golf balls (and optionally the wielder is handled client-side).
            float suckEnd = Time.time + suckDuration;
            while (Time.time < suckEnd)
            {
                if (ModConfig.Moon.PullAffectsGolfBalls.Value)
                    ServerApplyGolfBallPull();
                yield return new WaitForFixedUpdate();
            }

            if (!_serverSessionActive)
                yield break;

            IssaPluginPlugin.Log.LogInfo("[Moon] Server: session complete — triggering explosion.");
            ServerEndSession(broadcast: true);
        }

        private void ServerApplyGolfBallPull()
        {
            float pullRadius = ModConfig.Moon.PullRadius.Value;
            float pullForce = ModConfig.Moon.PullForce.Value;
            float maxSpeed = ModConfig.Moon.MaxPullSpeed.Value;

            var hits = Physics.OverlapSphere(
                _serverImpactPos,
                pullRadius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            _seenRigidbodies.Clear();

            foreach (var col in hits)
            {
                if (col.GetComponentInParent<PlayerInfo>() != null)
                    continue;
                if (col.GetComponentInParent<GolfBall>() == null)
                    continue;

                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic || _seenRigidbodies.Contains(rb))
                    continue;
                _seenRigidbodies.Add(rb);

                Vector3 dir = (_serverImpactPos - rb.position).normalized;
                float dist = Vector3.Distance(rb.position, _serverImpactPos);
                float t = Mathf.Clamp01(1f - dist / pullRadius);
                t *= t;
                float force = Mathf.Lerp(pullForce * 0.3f, pullForce, t);
                rb.AddForce(dir * force, ForceMode.Acceleration);

                float towardSpeed = Vector3.Dot(rb.linearVelocity, dir);
                if (towardSpeed > maxSpeed)
                    rb.linearVelocity -= dir * (towardSpeed - maxSpeed);
            }
        }

        // ── Server — session end ──────────────────────────────────────────────

        private void ServerEndSession(bool broadcast)
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            if (broadcast)
            {
                float explosionForce = ModConfig.Moon.ExplosionForce.Value;
                float explosionRadius = ModConfig.Moon.ExplosionRadius.Value;

                var colliders = Physics.OverlapSphere(_serverImpactPos, explosionRadius);
                foreach (var col in colliders)
                {
                    var rb = col.attachedRigidbody;
                    if (rb == null || rb.isKinematic)
                        continue;
                    if (col.GetComponentInParent<PlayerInfo>() != null)
                        continue;
                    rb.AddExplosionForce(
                        explosionForce,
                        _serverImpactPos,
                        explosionRadius,
                        0.5f,
                        ForceMode.VelocityChange
                    );
                }

                if (_wielderSlot >= 0)
                {
                    var inventory = GetComponent<PlayerInventory>();
                    if (inventory != null)
                    {
                        ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
                        ItemHelper.DecrementAndRemove(inventory, _wielderSlot);
                        ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                    }
                }

                NetworkServer.SendToAll(
                    new MoonEndMessage
                    {
                        WielderNetId = GetComponent<NetworkIdentity>().netId,
                        ExplosionPos = _serverImpactPos,
                    }
                );
            }

            GlobalSessionLock<MoonNetworkBridge>.Release();

            if (_serverCoroutine != null)
            {
                StopCoroutine(_serverCoroutine);
                _serverCoroutine = null;
            }

            _wielderConn = null;
            _wielderSlot = -1;
        }

        // ── Hole cleanup ──────────────────────────────────────────────────────

        public override void ServerHoleCleanup()
        {
            // Do not broadcast MoonEndMessage during hole transitions — ClientHoleCleanup
            // handles VFX teardown on each client directly via ClearAll().
            if (_serverSessionActive)
                ServerEndSession(broadcast: false);

            if (_serverCoroutine != null)
            {
                StopCoroutine(_serverCoroutine);
                _serverCoroutine = null;
            }
        }

        public override void ClientHoleCleanup()
        {
            MoonClientLogic.ClearAll();
        }
    }
}
