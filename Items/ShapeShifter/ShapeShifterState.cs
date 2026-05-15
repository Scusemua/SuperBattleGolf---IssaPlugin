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
        Acorn,
        Isosphere,
    }

    /// Applied to a GolfBall's GameObject when the shape-shifter effect is active.
    ///
    /// Picks a random shape each time it is applied.  Each shape is loaded from
    /// the asset bundle as a prefab containing MeshFilter + MeshRenderer +
    /// MeshCollider (convex).  The child is parented to the ball and scaled to
    /// match the ball's visual size.  Unity's Rigidbody automatically picks up
    /// colliders on child objects, so the MeshCollider stays on the child where
    /// its scale is correct — moving it to the root would break the sizing.
    ///
    /// On Apply():
    ///   • Disables the GolfBall's SphereCollider.
    ///   • Instantiates the shape prefab as a scaled child; its MeshCollider
    ///     (convex) is used directly by the parent Rigidbody.
    ///   • Applies high-friction physics to that collider.
    ///   • Hides the original MeshRenderers and inherits the ball's material.
    ///
    /// On Revert():
    ///   • Re-enables the SphereCollider, destroys the shape child (and its
    ///     collider with it).
    ///   • Restores original MeshRenderer visibility.
    ///   • Destroys this component.
    public class ShapeShifterState : MonoBehaviour
    {
        private SphereCollider _sphere;
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

            // ── Disable sphere collider ───────────────────────────────────────
            _sphere = GetComponent<SphereCollider>();
            if (_sphere != null)
                _sphere.enabled = false;
            else
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] ShapeShifterState.Apply: no SphereCollider found on ball."
                );

            // ── Hide original renderers ───────────────────────────────────────
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

            // ── Spawn shape child ─────────────────────────────────────────────
            var prefab = GetShapePrefab(Shape);
            _shapeChild = prefab != null ? Instantiate(prefab) : FallbackShapeObject(Shape);

            // Parent and scale the child to match the ball's visual size.
            // The MeshCollider stays on the child — Unity's Rigidbody picks up
            // colliders on children automatically, and keeping the collider here
            // means its scale is driven by localScale, which is exactly localSide.
            _shapeChild.transform.SetParent(transform, worldPositionStays: false);
            _shapeChild.transform.localPosition = localCenter;
            _shapeChild.transform.localScale = Vector3.one * localSide;
            _shapeChild.transform.localRotation = Quaternion.identity;

            // Apply high-friction physics to whatever collider the child has.
            _shapeMaterial = new PhysicsMaterial("ShapeShifterMat")
            {
                staticFriction = ModConfig.ShapeShifter.PhysicsStaticFriction.Value,
                dynamicFriction = ModConfig.ShapeShifter.PhysicsDynamicFriction.Value,
                bounciness = ModConfig.ShapeShifter.PhysicsBounciness.Value,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            if (_shapeChild.GetComponentInChildren<Collider>() is { } col)
                col.sharedMaterial = _shapeMaterial;

            // Inherit the ball's cosmetic material.
            if (primaryRenderer?.sharedMaterial is { } mat)
                if (_shapeChild.GetComponentInChildren<MeshRenderer>() is { } mr)
                    mr.sharedMaterial = mat;
        }

        public void Revert()
        {
            if (_sphere != null)
                _sphere.enabled = true;

            if (_shapeMaterial != null)
                Destroy(_shapeMaterial);

            // Destroying the child also destroys its MeshCollider and MeshRenderer.
            if (_shapeChild != null)
                Destroy(_shapeChild);

            if (_originalRenderers != null)
            {
                for (int i = 0; i < _originalRenderers.Length; i++)
                    if (_originalRenderers[i] != null)
                        _originalRenderers[i].enabled = true;
            }

            Destroy(this);
        }

        private static GameObject GetShapePrefab(BallShape shape) =>
            shape switch
            {
                BallShape.Cube => AssetLoader.ShapeShifterShapeCube,
                BallShape.Disk => AssetLoader.ShapeShifterShapeDisk,
                BallShape.Cylinder => AssetLoader.ShapeShifterShapeCylinder,
                BallShape.Cone => AssetLoader.ShapeShifterShapeCone,
                BallShape.Pyramid => AssetLoader.ShapeShifterShapePyramid,
                BallShape.Acorn => AssetLoader.ShapeShifterShapeAcorn,
                BallShape.Isosphere => AssetLoader.ShapeShifterShapeIsosphere,
                _ => null,
            };

        // Returns a visual+collider primitive when the bundle prefab is absent.
        // The collider is kept on the child (not stripped) so the ball still lands —
        // there is no bundle MeshCollider to promote in this path, so the child's
        // primitive collider is the only one available.
        private static GameObject FallbackShapeObject(BallShape shape)
        {
            IssaPluginPlugin.Log.LogWarning(
                $"[ShapeShifter] Bundle prefab for shape '{shape}' not found — using primitive fallback."
            );
            return shape switch
            {
                BallShape.Cylinder => GameObject.CreatePrimitive(PrimitiveType.Cylinder),
                BallShape.Disk => BuildFlatCylinder(),
                BallShape.Cone => GameObject.CreatePrimitive(PrimitiveType.Capsule),
                BallShape.Pyramid => GameObject.CreatePrimitive(PrimitiveType.Cube),
                BallShape.Acorn => GameObject.CreatePrimitive(PrimitiveType.Capsule),
                _ => GameObject.CreatePrimitive(PrimitiveType.Cube),
            };
        }

        private static GameObject BuildFlatCylinder()
        {
            // Wrap in a parent so Apply()'s uniform localScale applies to the root
            // while the inner cylinder keeps its flat proportions in its own local space.
            var root = new GameObject("Disk_Fallback");
            var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inner.transform.SetParent(root.transform, worldPositionStays: false);
            inner.transform.localScale = new Vector3(1f, 0.2f, 1f);
            return root;
        }
    }
}
