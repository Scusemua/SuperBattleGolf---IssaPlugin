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
            IssaPluginPlugin.Log.LogInfo($"[AC130] OnCollisionEnter called.");
        }

        // The game's Rocket uses a trigger collider, so we receive OnTriggerEnter
        // (not OnCollisionEnter) when it overlaps the gunship mesh collider.
        private void OnTriggerEnter(Collider other)
        {
            IssaPluginPlugin.Log.LogInfo($"[AC130] OnTriggerEnter called.");

            if (!NetworkServer.active)
                return;

            int hitsRequired = Configuration.AC130HitsToMayday.Value;
            if (hitsRequired <= 0)
                return;

            if (_hitCount >= hitsRequired)
                return;

            if (other.GetComponentInParent<Rocket>() == null)
                return;

            _hitCount++;
            IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket impact {_hitCount}/{hitsRequired}.");

            if (_hitCount >= hitsRequired)
            {
                IssaPluginPlugin.Log.LogInfo("[AC130] Hit threshold reached — triggering mayday.");
                OnHitsExceeded?.Invoke();
            }
        }

        public void OnAC130Hit(
            PlayerInventory attackerInventory,
            ItemType itemType,
            ItemUseId itemUseId,
            Vector3 hitPoint,
            float damage,
            bool isReflected
        )
        {
            if (itemType == ItemType.RocketLauncher)
            {
                IssaPluginPlugin.Log.LogInfo("AC130 was hit by a rocket!");

                int hitsRequired = Configuration.AC130HitsToMayday.Value;
                if (hitsRequired <= 0)
                {
                    IssaPluginPlugin.Log.LogInfo($"hitsRequired={hitsRequired}; ignoring hit.");
                    return;
                }

                if (_hitCount >= hitsRequired)
                {
                    IssaPluginPlugin.Log.LogInfo(
                        $"hitsRequired={hitsRequired}, _hitCount={_hitCount}, _hitCount >= hitsRequired; ignoring hit."
                    );
                    return;
                }

                _hitCount++;
                IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket impact {_hitCount}/{hitsRequired}.");

                if (_hitCount >= hitsRequired)
                {
                    IssaPluginPlugin.Log.LogInfo(
                        "[AC130] Hit threshold reached — triggering mayday."
                    );
                    OnHitsExceeded?.Invoke();
                }
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo($"AC130 was hit by: {itemType}");
            }
        }
    }
}
