using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to the AC130 gunship server-side.
    /// Counts rocket impacts and invokes OnHitsExceeded when the configured
    /// threshold is reached, giving the session bridge a chance to trigger mayday.
    /// Only runs logic on the server; clients ignore collision events here.
    /// </summary>
    public class AC130HitReceiver : MonoBehaviour
    {
        /// <summary>Invoked on the server when hit count reaches the threshold.</summary>
        public System.Action OnHitsExceeded;

        private int _hitCount;

        private void OnCollisionEnter(Collision collision)
        {
            if (!NetworkServer.active)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[AC130] NetworkServer inactive. Ignoring collision."
                );
                return;
            }

            int hitsRequired = Configuration.AC130HitsToMayday.Value;
            if (hitsRequired <= 0)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[AC130] hitsRequired={hitsRequired} <= 0. Ignoring collision."
                );
                return; // 0 = disabled
            }

            if (_hitCount >= hitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[AC130] _hitCount={_hitCount} >= hitsRequired={hitsRequired}. Ignoring collision."
                );
                return; // already triggered
            }

            if (collision.gameObject.GetComponent<Rocket>() == null)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[AC130] Whataever hit AC130 isn't a Rocket. Ignoring collision."
                );
                return;
            }

            _hitCount++;
            IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket impact {_hitCount}/{hitsRequired}.");

            if (_hitCount >= hitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo("[AC130] Hit threshold reached — triggering mayday.");
                OnHitsExceeded?.Invoke();
            }
        }
    }
}
