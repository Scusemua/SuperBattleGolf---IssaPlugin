using System;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached server-side to each bear alongside BearBehaviour.
    /// Tracks incoming hits from the explosion patch and manages the bear's HP.
    ///
    /// Inherits CustomHittable so the existing Patch_Rocket_ServerExplode
    /// overlap-sphere check in RocketPatches.cs naturally discovers this component
    /// and calls OnHit (once the A-5 audit fix has been applied to filter to
    /// mod-only rockets — until then, this will also trigger on base-game rockets,
    /// which may actually be desirable for bear hunting).
    ///
    /// OnBearHitByPlayer is raised so BearBehaviour can update the aggro table.
    /// OnHitsExceeded is raised (from CustomHittable) to trigger death.
    /// </summary>
    public class BearHitReceiver : CustomHittable
    {
        /// Raised on the server when the bear takes a hit from an explosion.
        /// The PlayerInfo is the owner of the rocket that hit the bear, if known.
        public event Action<PlayerInfo> OnBearHitByPlayer;

        /// Back-reference to the driving behaviour component so we can call
        /// OnHitByExplosion / OnKilled without requiring another GetComponent call.
        public BearBehaviour Behaviour { get; set; }

        private void Awake()
        {
            HitCount = 0;
            HitsRequired = (int)Configuration.BearHitsToKill.Value;

            OnHit += HandleHit;

            OnHitsExceeded = () =>
            {
                Behaviour?.OnKilled();
            };
        }

        private void HandleHit()
        {
            if (!NetworkServer.active)
                return;
            if (HitsRequired <= 0)
                return;
            if (HitCount >= HitsRequired)
                return;

            HitCount++;

            IssaPluginPlugin.Log.LogInfo($"[Bear] Hit {HitCount}/{HitsRequired}.");

            // Try to resolve the attacking player from the most recent explosion.
            // The explosion patch stores the attacker via PlayerInfo on the Rocket;
            // we surface it here for the aggro system.
            // NOTE: GetCurrentAttacker() is a best-effort lookup — see implementation below.
            PlayerInfo attacker = TryGetCurrentAttacker();
            OnBearHitByPlayer?.Invoke(attacker);

            if (HitCount >= HitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo("[Bear] Bear killed by explosion.");
                OnHitsExceeded?.Invoke();
                return;
            }
            else
            {
                Behaviour?.OnHitByExplosion(attacker);
            }
        }

        /// <summary>
        /// Attempts to find the PlayerInfo of the player whose rocket most recently
        /// overlapped this bear. This is a best-effort approach: we walk up the
        /// recent-explosion context set by the explosion postfix patch.
        ///
        /// If we cannot determine the attacker (e.g. the rocket has already been
        /// destroyed), we return null — which causes the bear to keep its existing
        /// aggro target.
        /// </summary>
        private static PlayerInfo TryGetCurrentAttacker()
        {
            // BearExplosionAttackerContext is a lightweight static set by the
            // Patch_Rocket_ServerExplode postfix immediately before calling OnHit.
            // See BearRocketPatch.cs.
            return BearExplosionAttackerContext.CurrentAttacker;
        }
    }

    /// <summary>
    /// Thread-local-ish context set by the rocket explosion patch immediately
    /// before invoking BearHitReceiver.OnHit so the hit receiver can identify
    /// the attacker without storing state on the Rocket itself.
    ///
    /// Unity is single-threaded so a simple static field is safe here.
    /// </summary>
    public static class BearExplosionAttackerContext
    {
        public static PlayerInfo CurrentAttacker { get; set; }
    }
}
