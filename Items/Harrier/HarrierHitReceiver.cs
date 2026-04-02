using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached server-side to the Harrier Jet GameObject after it spawns.
    /// Counts rocket impacts and invokes OnHitsExceeded when the configured
    /// threshold is reached, triggering the crash sequence.
    ///
    /// Set HitsRequired to 0 to make the Harrier invincible.
    /// </summary>
    public class HarrierHitReceiver : CustomHittable
    {
        /// <summary>World-space explosion position of the last rocket hit.</summary>
        public Vector3 LastHitWorldPos;

        private bool _shotDown;

        private void Awake()
        {
            HitCount    = 0;
            HitsRequired = (int)ModConfig.Harrier.HitsToDestroy.Value;
            OnHit += HandleHit;
        }

        private void HandleHit()
        {
            if (!NetworkServer.active || _shotDown)
                return;

            // 0 = invincible
            if (HitsRequired <= 0)
                return;

            if (HitCount >= HitsRequired)
                return;

            HitCount++;
            IssaPluginPlugin.Log.LogInfo(
                $"[Harrier] Rocket impact {HitCount}/{HitsRequired}."
            );

            if (HitCount >= HitsRequired)
            {
                _shotDown = true;
                IssaPluginPlugin.Log.LogInfo("[Harrier] Hit threshold reached — crashing.");
                OnHitsExceeded?.Invoke();
            }
        }
    }
}
