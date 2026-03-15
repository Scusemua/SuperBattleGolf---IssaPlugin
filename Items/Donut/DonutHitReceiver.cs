using IssaPlugin;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class DonutHitReceiver : CustomHittable
    {
        public void Awake()
        {
            HitCount = 0;
            HitsRequired = 1;
            OnHit += OnDonutHit;
        }

        private void OnDonutHit()
        {
            IssaPluginPlugin.Log.LogInfo($"[Donut] OnDonutHit called.");

            if (!NetworkServer.active)
                return;

            if (HitsRequired <= 0)
                return;

            if (HitCount >= HitsRequired)
                return;

            HitCount++;
            IssaPluginPlugin.Log.LogInfo($"[Donut] Rocket impact {HitCount}/{HitsRequired}.");

            if (HitCount >= HitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo("[Donut] Hit threshold reached — triggering mayday.");
                OnHitsExceeded?.Invoke();
            }
        }
    }
}
