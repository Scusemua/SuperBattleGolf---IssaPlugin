using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Applies the session's rocket speed multiplier to rockets fired from
    /// the AC130 gunship, identically on every peer. Piloted and autonomous
    /// sessions each send their own multiplier.
    ///
    /// Rockets carry no NetworkTransform — each peer simulates them locally from the
    /// spawn position and rotation Mirror replicates, and <c>Rocket.Awake</c> seeds the
    /// velocity as <c>transform.forward * GameManager.ItemSettings.RocketVelocity</c> on
    /// every peer. A speed override must therefore be reproduced everywhere, or remote
    /// clients' rockets drift away from the server's authoritative simulation — which is
    /// the one that decides what a rocket actually hits.
    ///
    /// Identifying the rocket is the hard part. <see cref="CustomSpawnedRocket"/> is
    /// added after <c>Instantiate</c> on the host only and never exists on client copies,
    /// so it cannot serve. The rocket's <c>launcher</c> SyncVar, however, is written into
    /// the initial spawn payload and so is readable on every peer. The server broadcasts
    /// <see cref="AC130ShooterStateMessage"/> when a session starts and ends, and each
    /// peer keeps the resulting set of active AC130 shooters here; a rocket whose
    /// launcher is in that set is an AC130 rocket.
    ///
    /// The hook is <c>Rocket.OnStartClient</c>, not <c>Rocket.Awake</c>: Mirror applies
    /// the spawn payload after the object is instantiated, so the launcher SyncVar is
    /// still null during Awake on a remote client. OnStartClient runs after the payload
    /// is applied and before the first physics step, so the rescale is both well-informed
    /// and early enough to matter. It fires on the host too, which is why the host needs
    /// no separate path.
    ///
    /// An earlier version matched the rocket's spawn position against the gunship
    /// position instead. That was abandoned: the Harrier hovers at a similar altitude
    /// near the same map centre and can be airborne at the same time, so its rockets
    /// could fall inside the match radius and be silently sped up too.
    /// </summary>
    public static class AC130RocketSpeed
    {
        /// netIds of players with an AC130 session currently in progress, and the rocket
        /// speed multiplier for that session. Maintained on every peer from
        /// AC130ShooterStateMessage.
        private static readonly Dictionary<uint, float> ActiveShooters = new Dictionary<uint, float>();

        /// <summary>
        /// Records that <paramref name="shooterNetId"/> has started or finished an AC130
        /// session. Called on every peer from the AC130ShooterStateMessage handler, and
        /// directly on the host, which does not receive its own broadcast.
        /// </summary>
        public static void SetShooterActive(
            uint shooterNetId,
            bool active,
            float rocketSpeedMultiplier = 1f
        )
        {
            if (active)
                ActiveShooters[shooterNetId] = rocketSpeedMultiplier;
            else
                ActiveShooters.Remove(shooterNetId);
        }

        /// <summary>
        /// Clears all tracked shooters. Called from the AC130 bridge's hole cleanup so a
        /// session cut short by a hole change cannot leave a stale entry behind.
        /// </summary>
        public static void Clear() => ActiveShooters.Clear();

        /// <summary>
        /// Called from the <c>Rocket.OnStartClient</c> postfix on every peer. Rescales
        /// the rocket's initial velocity when it was fired from the AC130 gunship.
        /// </summary>
        public static void TryApply(Rocket rocket)
        {
            if (rocket == null)
                return;

            // Most rockets in the match are not from an AC130. Bail before touching the launcher.
            if (ActiveShooters.Count == 0)
                return;

            if (!TryGetMultiplier(rocket, out float multiplier))
                return;

            if (Mathf.Approximately(multiplier, 1f))
                return;

            var entity = rocket.GetComponent<Entity>();
            if (entity == null || !entity.HasRigidbody)
                return;

            entity.Rigidbody.linearVelocity *= multiplier;
        }

        private static bool TryGetMultiplier(Rocket rocket, out float multiplier)
        {
            multiplier = 1f;
            var launcher = rocket.Launcher;
            if (launcher == null)
                return false;

            var identity = launcher.netIdentity;
            return identity != null && ActiveShooters.TryGetValue(identity.netId, out multiplier);
        }

        /// <summary>
        /// Client handler for <see cref="AC130ShooterStateMessage"/>.
        /// Registered in NetworkManagerPatches.
        /// </summary>
        internal static void HandleShooterState(AC130ShooterStateMessage msg)
        {
            // The host already applied this locally when it sent the broadcast, and
            // SetShooterActive is idempotent, so re-applying here is harmless.
            SetShooterActive(msg.ShooterNetId, msg.Active, msg.RocketSpeedMultiplier);
        }
    }
}
