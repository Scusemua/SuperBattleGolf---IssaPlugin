using IssaPlugin;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to the AC130 gunship server-side.
    /// Counts rocket impacts and invokes OnHitsExceeded when the configured
    /// threshold is reached, giving the session bridge a chance to trigger mayday.
    /// Only runs logic on the server; clients ignore collision events here.
    public class AC130HitReceiver : CustomHittable
    {
        public void Awake()
        {
            HitCount = 0;
            HitsRequired = (int)Configuration.AC130HitsToMayday.Value;
            OnHit += OnAC130Hit;
        }

        private void OnAC130Hit()
        {
            IssaPluginPlugin.Log.LogInfo($"[AC130] OnAC130Hit called.");

            if (!NetworkServer.active)
                return;

            if (HitsRequired <= 0)
                return;

            if (HitCount >= HitsRequired)
                return;

            HitCount++;
            IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket impact {HitCount}/{HitsRequired}.");

            if (HitCount >= HitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo("[AC130] Hit threshold reached — triggering mayday.");
                OnHitsExceeded?.Invoke();
            }
        }
    }
}
