using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached client-side to the Javelin rocket GameObject alongside its trail VFX.
    /// When the rocket is destroyed (on detonation), this component detaches the trail
    /// so already-emitted particles can finish their natural lifetime rather than
    /// vanishing instantly.  The detached trail root is then destroyed after a short
    /// delay to ensure no orphaned GameObjects accumulate.
    public class JavelinRocketTrailDetacher : MonoBehaviour
    {
        private const float LingerSeconds = 4f;

        public Transform TrailRoot;

        private void OnDestroy()
        {
            if (TrailRoot == null)
                return;

            // Unparent so the trail survives the rocket's destruction.
            TrailRoot.SetParent(null, worldPositionStays: true);

            // Stop new particles from emitting; existing ones play out naturally.
            foreach (var ps in TrailRoot.GetComponentsInChildren<ParticleSystem>())
            {
                var emission = ps.emission;
                emission.enabled = false;
            }

            Destroy(TrailRoot.gameObject, LingerSeconds);
        }
    }
}
