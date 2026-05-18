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
    ///   • Shrinks the GolfBall's SphereCollider to near-zero rather than
    ///     disabling it.  The game holds a direct reference to it internally
    ///     (IgnoreCollision, OverlapSphere, penetration resolution) and silently
    ///     breaks if it is disabled — leaving collisions permanently ignored and
    ///     causing the rare fall-through bug.
    ///   • Instantiates the shape prefab as a scaled child; its MeshCollider
    ///     (convex) is used directly by the parent Rigidbody.
    ///   • Applies high-friction physics to that collider.
    ///   • Hides the original MeshRenderers and inherits the ball's material.
    ///
    /// On Revert():
    ///   • Restores the SphereCollider radius, destroys the shape child.
    ///   • Restores original MeshRenderer visibility.
    ///   • Destroys this component.
    public class ShapeShifterState : MonoBehaviour
    {
        private SphereCollider _sphere;
        private float _originalRadius;
        private MeshRenderer[] _originalRenderers;
        private GameObject _shapeChild;
        private int _shapeColliderId;

        public BallShape Shape { get; private set; }

        public void Apply()
        {
            Shape = PickRandomShape();

            // ── Size from SphereCollider radius ───────────────────────────────
            // Using MeshRenderer.bounds is unstable (varies with rotation/animation).
            // The SphereCollider radius is the authoritative visual size of the ball.
            _sphere = GetComponent<SphereCollider>();
            if (_sphere == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] ShapeShifterState.Apply: no SphereCollider found on ball."
                );
            }

            // localSide = the diameter of the ball in local space.
            float localSide = _sphere != null ? _sphere.radius * 2f : 1f;
            Vector3 localCenter = _sphere != null ? _sphere.center : Vector3.zero;

            // ── Shrink sphere collider to near-zero ───────────────────────────
            // Do NOT disable it — GolfBall keeps a direct reference and calls
            // Physics.IgnoreCollision / OverlapSphere on it every frame.
            // Disabling it prevents those re-enable calls from working, leaving
            // our shape collider permanently ignored by terrain (fall-through bug).
            if (_sphere != null)
            {
                _originalRadius = _sphere.radius;
                _sphere.radius = 0.001f;
            }

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

            // Use the game's own BallMaterial on the shape collider.
            // A custom high-friction material would apply to player contacts too,
            // causing even a slow-rolling shape ball to generate enough impulse to
            // knock the player down.  Terrain friction is overridden per-contact by
            // PhysicsManager.ApplyCollisionSettings regardless of sharedMaterial,
            // so the game material is correct for all contact types.
            // Also register the collider ID so PhysicsManager classifies it as Ball,
            // matching the treatment the SphereCollider gets via GolfBall.Start().
            if (_shapeChild.GetComponentInChildren<Collider>() is { } col)
            {
                col.sharedMaterial = PhysicsManager.Settings?.BallMaterial;
                _shapeColliderId = col.GetInstanceID();
                PhysicsManager.RegisterBallColliderId(_shapeColliderId);
            }

            // Inherit the ball's cosmetic material from any still-enabled renderer on the ball root.
            var ballRenderer = GetComponentInChildren<MeshRenderer>(includeInactive: false);
            if (ballRenderer?.sharedMaterial is { } mat)
                if (_shapeChild.GetComponentInChildren<MeshRenderer>() is { } mr)
                    mr.sharedMaterial = mat;
        }

        public void Revert()
        {
            if (_shapeColliderId != 0)
            {
                PhysicsManager.DeregisterBallColliderId(_shapeColliderId);
                _shapeColliderId = 0;
            }

            if (_sphere != null)
                _sphere.radius = _originalRadius;

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

        private static BallShape PickRandomShape()
        {
            var cfg = ModConfig.ShapeShifter;
            // Build the pool of enabled shapes inline — avoids allocation on the hot path
            // only when shapes are actually disabled.
            BallShape[] all =
            {
                BallShape.Cube,
                BallShape.Disk,
                BallShape.Cylinder,
                BallShape.Cone,
                BallShape.Pyramid,
                BallShape.Acorn,
                BallShape.Isosphere,
            };
            bool[] enabled =
            {
                cfg.ShapeCubeEnabled.Value,
                cfg.ShapeDiskEnabled.Value,
                cfg.ShapeCylinderEnabled.Value,
                cfg.ShapeConeEnabled.Value,
                cfg.ShapePyramidEnabled.Value,
                cfg.ShapeAcornEnabled.Value,
                cfg.ShapeIsosphereEnabled.Value,
            };

            int poolSize = 0;
            for (int i = 0; i < enabled.Length; i++)
                if (enabled[i])
                    poolSize++;

            if (poolSize == 0)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] All shapes are disabled in config — picking from full pool."
                );
                return all[Random.Range(0, all.Length)];
            }

            int pick = Random.Range(0, poolSize);
            int seen = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (!enabled[i])
                    continue;
                if (seen == pick)
                    return all[i];
                seen++;
            }
            return BallShape.Cube; // unreachable
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
