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
    /// OnHitsExceeded is raised (from CustomHittable) to trigger death.
    /// NOTE: CustomHittable.OnHitsExceeded is used as a plain Action delegate here —
    /// the base class does not auto-invoke it; the subclass calls it directly in HandleHit.
    /// </summary>
    public class BearHitReceiver : CustomHittable
    {
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
            // The explosion patch stores the attacker via BearExplosionAttackerContext.
            PlayerInfo attacker = TryGetCurrentAttacker();

            if (HitCount >= HitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo("[Bear] Bear killed by explosion.");
                OnHitsExceeded?.Invoke();
                return;
            }

            // OnHitByExplosion handles both aggro (NotifyHitBy) and stun transition.
            Behaviour?.OnHitByExplosion(attacker);
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
