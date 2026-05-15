using UnityEngine;

namespace IssaPlugin.Items
{
    /// Applied to a GolfBall's GameObject when the cube-ball effect is active.
    ///
    /// On Apply():
    ///   • Disables the GolfBall's SphereCollider and adds a BoxCollider sized to
    ///     match the ball's actual rendered visual bounds (not the physics collider
    ///     radius, which is intentionally larger than the visual for gameplay).
    ///   • Hides the original MeshRenderers and spawns a child cube GameObject
    ///     scaled to the same visual size, inheriting the ball's cosmetic material.
    ///   • Sets high-friction physics on the BoxCollider so the cube grips and
    ///     tumbles rather than sliding smoothly like a ball.
    ///
    /// On Revert():
    ///   • Re-enables the SphereCollider, destroys the BoxCollider and cube child.
    ///   • Restores original MeshRenderer visibility.
    ///   • Destroys this component.
    public class ShapeShifterState : MonoBehaviour
    {
        private SphereCollider _sphere;
        private BoxCollider _box;
        private PhysicsMaterial _cubeMaterial;

        private MeshRenderer[] _originalRenderers;
        private GameObject _cubeChild;

        public void Apply()
        {
            // ── Measure visual size from rendered bounds ───────────────────────
            // The SphereCollider radius is intentionally larger than the visual
            // mesh for gameplay. Use the MeshRenderer world bounds instead.
            var primaryRenderer = GetComponentInChildren<MeshRenderer>();
            float worldSide = 0f;
            Vector3 worldCenter = transform.position;

            if (primaryRenderer != null)
            {
                var b = primaryRenderer.bounds;
                worldSide = Mathf.Max(b.size.x, b.size.y, b.size.z);
                worldCenter = b.center;
            }

            // Convert visual world size → local space once; reused for both collider and mesh.
            float uniformScale = transform.lossyScale.x;
            float localSide =
                worldSide > 0f ? (uniformScale > 0f ? worldSide / uniformScale : worldSide) : 0f;

            // ── Collider swap ─────────────────────────────────────────────────
            _sphere = GetComponent<SphereCollider>();
            if (_sphere != null)
            {
                _sphere.enabled = false;

                _box = gameObject.AddComponent<BoxCollider>();

                if (localSide > 0f)
                {
                    _box.size = Vector3.one * localSide;
                    _box.center = transform.InverseTransformPoint(worldCenter);
                }
                else
                {
                    // Fallback: use sphere dimensions
                    _box.size = Vector3.one * _sphere.radius * 2f;
                    _box.center = _sphere.center;
                }

                // High friction so the cube tumbles (grips ground) rather than sliding.
                _cubeMaterial = new PhysicsMaterial("ShapeShifterMat")
                {
                    staticFriction = ModConfig.ShapeShifter.PhysicsStaticFriction.Value,
                    dynamicFriction = ModConfig.ShapeShifter.PhysicsDynamicFriction.Value,
                    bounciness = ModConfig.ShapeShifter.PhysicsBounciness.Value,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
                _box.sharedMaterial = _cubeMaterial;
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] ShapeShifterState.Apply: no SphereCollider found on ball."
                );
            }

            // ── Visual: hide original renderers, spawn sized cube child ───────
            // Capture only renderers that are currently ON so Revert() can safely
            // force them back on regardless of what the game does to them during flight.
            var all = GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled)
                    count++;
            _originalRenderers = new MeshRenderer[count];
            int idx = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled)
                    _originalRenderers[idx++] = all[i];
            for (int i = 0; i < _originalRenderers.Length; i++)
                _originalRenderers[i].enabled = false;

            _cubeChild = GameObject.CreatePrimitive(PrimitiveType.Cube);

            // Remove the extra BoxCollider added by CreatePrimitive — physics is
            // handled by the one we added above on the GolfBall root.
            var extraCollider = _cubeChild.GetComponent<Collider>();
            if (extraCollider != null)
                Destroy(extraCollider);

            _cubeChild.transform.SetParent(transform, worldPositionStays: false);

            if (localSide > 0f)
            {
                // Position the cube child at the visual center of the ball in local space.
                _cubeChild.transform.localPosition = transform.InverseTransformPoint(worldCenter);
                _cubeChild.transform.localScale = Vector3.one * localSide;
            }
            else
            {
                _cubeChild.transform.localPosition = Vector3.zero;
                _cubeChild.transform.localScale = Vector3.one;
            }

            _cubeChild.transform.localRotation = Quaternion.identity;

            // Inherit the ball's cosmetic material so the skin still shows.
            var mat = primaryRenderer?.sharedMaterial;
            if (mat != null)
                _cubeChild.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        public void Revert()
        {
            if (_sphere != null)
                _sphere.enabled = true;

            if (_box != null)
                Destroy(_box);

            if (_cubeMaterial != null)
                Destroy(_cubeMaterial);

            if (_cubeChild != null)
                Destroy(_cubeChild);

            if (_originalRenderers != null)
            {
                for (int i = 0; i < _originalRenderers.Length; i++)
                {
                    if (_originalRenderers[i] != null)
                        _originalRenderers[i].enabled = true;
                }
            }

            Destroy(this);
        }
    }
}
