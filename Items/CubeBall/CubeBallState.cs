using UnityEngine;

namespace IssaPlugin.Items
{
    public enum BallShape
    {
        Cube,
        Disk,
        Cylinder,
        Cone,
        Pyramid,
    }

    /// Applied to a GolfBall's GameObject when the cube-ball effect is active.
    ///
    /// Picks a random shape each time it is applied.  Each shape is loaded from
    /// the asset bundle as a prefab containing MeshFilter + MeshRenderer +
    /// MeshCollider (convex).  The MeshCollider is moved to the ball root so it
    /// sits alongside the SphereCollider (which is disabled for the duration).
    ///
    /// On Apply():
    ///   • Disables the GolfBall's SphereCollider.
    ///   • Instantiates the shape prefab as a child, transfers its MeshCollider
    ///     to the ball root, and applies high-friction physics.
    ///   • Hides the original MeshRenderers; the shape child provides the visual.
    ///   • Inherits the ball's cosmetic material.
    ///
    /// On Revert():
    ///   • Re-enables the SphereCollider, destroys the shape collider and child.
    ///   • Restores original MeshRenderer visibility.
    ///   • Destroys this component.
    public class CubeBallState : MonoBehaviour
    {
        private SphereCollider _sphere;
        private MeshCollider _shapeCollider;
        private PhysicsMaterial _shapeMaterial;

        private MeshRenderer[] _originalRenderers;
        private GameObject _shapeChild;

        public BallShape Shape { get; private set; }

        public void Apply()
        {
            Shape = (BallShape)Random.Range(0, System.Enum.GetValues(typeof(BallShape)).Length);

            // ── Measure visual size from rendered bounds ───────────────────────
            var primaryRenderer = GetComponentInChildren<MeshRenderer>();
            float worldSide = 0f;
            Vector3 worldCenter = transform.position;

            if (primaryRenderer != null)
            {
                var b = primaryRenderer.bounds;
                worldSide = Mathf.Max(b.size.x, b.size.y, b.size.z);
                worldCenter = b.center;
            }

            float uniformScale = transform.lossyScale.x;
            float localSide =
                worldSide > 0f ? (uniformScale > 0f ? worldSide / uniformScale : worldSide) : 0f;
            if (localSide <= 0f)
                localSide = 1f;

            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

            // ── Collider swap ─────────────────────────────────────────────────
            _sphere = GetComponent<SphereCollider>();
            if (_sphere != null)
                _sphere.enabled = false;
            else
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] CubeBallState.Apply: no SphereCollider found on ball."
                );

            // ── Visual: hide original renderers, spawn shape child ────────────
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

            var prefab = GetShapePrefab(Shape);
            _shapeChild = prefab != null ? Instantiate(prefab) : FallbackShapeObject(Shape);

            // Transfer the MeshCollider from the child to the ball root so it
            // lives alongside the (disabled) SphereCollider and is driven by the
            // ball's own Rigidbody.  The child provides only the visual.
            var childCollider = _shapeChild.GetComponentInChildren<MeshCollider>();
            if (childCollider != null)
            {
                _shapeCollider = gameObject.AddComponent<MeshCollider>();
                _shapeCollider.sharedMesh = childCollider.sharedMesh;
                _shapeCollider.convex = true;
                Destroy(childCollider);
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[CubeBall] Shape prefab for {Shape} has no MeshCollider — falling back to BoxCollider."
                );
                // Fallback: box collider approximation
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one * localSide;
                box.center = localCenter;
                _shapeCollider = null; // handled separately; see Revert()
                // Store as a plain Collider so Revert can clean it up.
                // (We reuse _shapeCollider only for MeshCollider; store the box differently.)
                _fallbackBoxCollider = box;
            }

            if (_shapeCollider != null)
            {
                _shapeMaterial = new PhysicsMaterial("CubeBallMat")
                {
                    staticFriction = ModConfig.CubeBall.PhysicsStaticFriction.Value,
                    dynamicFriction = ModConfig.CubeBall.PhysicsDynamicFriction.Value,
                    bounciness = ModConfig.CubeBall.PhysicsBounciness.Value,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
                _shapeCollider.sharedMaterial = _shapeMaterial;
            }
            else if (_fallbackBoxCollider != null)
            {
                _shapeMaterial = new PhysicsMaterial("CubeBallMat")
                {
                    staticFriction = ModConfig.CubeBall.PhysicsStaticFriction.Value,
                    dynamicFriction = ModConfig.CubeBall.PhysicsDynamicFriction.Value,
                    bounciness = ModConfig.CubeBall.PhysicsBounciness.Value,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
                _fallbackBoxCollider.sharedMaterial = _shapeMaterial;
            }

            _shapeChild.transform.SetParent(transform, worldPositionStays: false);
            _shapeChild.transform.localPosition = localCenter;
            _shapeChild.transform.localScale = Vector3.one * localSide;
            _shapeChild.transform.localRotation = Quaternion.identity;

            var mat = primaryRenderer?.sharedMaterial;
            if (mat != null)
                if (_shapeChild.GetComponent<MeshRenderer>() is { } mr)
                    mr.sharedMaterial = mat;

            IssaPluginPlugin.Log.LogWarning($"[CubeBall] Applied shape {Shape}.");
        }

        // Fallback box collider used only when the prefab has no MeshCollider.
        private BoxCollider _fallbackBoxCollider;

        public void Revert()
        {
            if (_sphere != null)
                _sphere.enabled = true;

            if (_shapeCollider != null)
                Destroy(_shapeCollider);

            if (_fallbackBoxCollider != null)
                Destroy(_fallbackBoxCollider);

            if (_shapeMaterial != null)
                Destroy(_shapeMaterial);

            if (_shapeChild != null)
                Destroy(_shapeChild);

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

        private static GameObject GetShapePrefab(BallShape shape) =>
            shape switch
            {
                BallShape.Cube => AssetLoader.CubeBallShapeCube,
                BallShape.Disk => AssetLoader.CubeBallShapeDisk,
                BallShape.Cylinder => AssetLoader.CubeBallShapeCylinder,
                BallShape.Cone => AssetLoader.CubeBallShapeCone,
                BallShape.Pyramid => AssetLoader.CubeBallShapePyramid,
                _ => null,
            };

        // Returns a simple GameObject when the bundle prefab is absent, so the
        // effect still works visually during development.
        private static GameObject FallbackShapeObject(BallShape shape)
        {
            IssaPluginPlugin.Log.LogWarning(
                $"[CubeBall] Bundle prefab for shape '{shape}' not found — using primitive fallback."
            );
            var go = shape switch
            {
                BallShape.Cylinder or BallShape.Disk => GameObject.CreatePrimitive(
                    PrimitiveType.Cylinder
                ),
                _ => GameObject.CreatePrimitive(PrimitiveType.Cube),
            };
            // Remove collider; it will be handled by the fallback BoxCollider path above.
            var col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            return go;
        }
    }
}
