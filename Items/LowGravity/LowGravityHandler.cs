using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Manages the global physics gravity change during a Low Gravity session.
    ///
    /// Physics.gravity is the single source of truth for:
    ///   - Rigidbody objects (golf balls, rockets, ragdolls)
    ///   - PlayerMovement, which accumulates vertical velocity via
    ///     "Physics.gravity.y * settings.BaseGravityFactor * gravityFactor * dt"
    ///     (see PlayerMovement.ApplyGravity, decompiled source line 1761)
    ///
    /// Only the Y component is scaled; X and Z are preserved (typically zero).
    /// Added to the Plugin's persistent gameObject in Plugin.cs.
    public class LowGravityHandler : MonoBehaviour
    {
        private bool _applied;
        private Vector3 _savedGravity;

        private void OnDestroy()
        {
            if (_applied)
                Restore();
        }

        private void Update()
        {
            if (LowGravityItem.IsActive && !_applied)
                Apply();
            else if (!LowGravityItem.IsActive && _applied)
                Restore();
        }

        private void Apply()
        {
            _applied = true;
            _savedGravity = Physics.gravity;

            float scale = Configuration.LowGravityScale.Value; 
            Physics.gravity = new Vector3(
                _savedGravity.x,
                _savedGravity.y * scale,
                _savedGravity.z
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[LowGravity] Gravity applied. Scale={scale:F2}, "
                + $"Physics.gravity.y={Physics.gravity.y:F3}"
            );
        }

        private void Restore()
        {
            _applied = false;
            Physics.gravity = _savedGravity;

            IssaPluginPlugin.Log.LogInfo(
                $"[LowGravity] Gravity restored. Physics.gravity.y={Physics.gravity.y:F3}"
            );
        }
    }
}
