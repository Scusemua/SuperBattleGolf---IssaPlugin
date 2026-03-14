using IssaPlugin;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class UFOHitReceiver : CustomHittable
    {
        public void Awake()
        {
            HitCount = 0;
            HitsRequired = 1;
            OnHit += OnUfoHit;
        }

        private void OnUfoHit()
        {
            IssaPluginPlugin.Log.LogInfo($"[UFO] OnUfoHit called.");

            if (!NetworkServer.active)
                return;

            if (HitsRequired <= 0)
                return;

            if (HitCount >= HitsRequired)
                return;

            HitCount++;
            IssaPluginPlugin.Log.LogInfo($"[UFO] Rocket impact {HitCount}/{HitsRequired}.");

            if (HitCount >= HitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo("[UFO] Hit threshold reached — triggering mayday.");
                OnHitsExceeded?.Invoke();
            }
        }
    }
}
