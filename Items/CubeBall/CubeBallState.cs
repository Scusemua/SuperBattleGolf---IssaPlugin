using UnityEngine;

namespace IssaPlugin.Items
{
    public enum BallShape
    {
        Cube,
        Disk,
        Cylinder,
        Cone,
        TriangularPrism,
        Pyramid,
    }

    /// Applied to a GolfBall's GameObject when the cube-ball effect is active.
    ///
    /// Picks a random shape each time it is applied.  Shapes: Cube, Disk,
    /// Cylinder, Cone, TriangularPrism, Pyramid.
    ///
    /// On Apply():
    ///   • Disables the GolfBall's SphereCollider and adds a shape-matched
    ///     collider (BoxCollider or CapsuleCollider).
    ///   • Hides the original MeshRenderers and spawns a child GameObject
    ///     with the chosen shape's mesh, inheriting the ball's cosmetic material.
    ///   • Sets high-friction physics so the shape grips and tumbles.
    ///
    /// On Revert():
    ///   • Re-enables the SphereCollider, destroys the shape collider and child.
    ///   • Restores original MeshRenderer visibility.
    ///   • Destroys this component.
    public class CubeBallState : MonoBehaviour
    {
        private SphereCollider _sphere;
        private Collider _shapeCollider;
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
            if (localSide <= 0f) localSide = 1f;

            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

            // ── Collider swap ─────────────────────────────────────────────────
            _sphere = GetComponent<SphereCollider>();
            if (_sphere != null)
            {
                _sphere.enabled = false;
                _shapeCollider = AddShapeCollider(localSide, localCenter);

                _shapeMaterial = new PhysicsMaterial("CubeBallMat")
                {
                    staticFriction  = ModConfig.CubeBall.PhysicsStaticFriction.Value,
                    dynamicFriction = ModConfig.CubeBall.PhysicsDynamicFriction.Value,
                    bounciness      = ModConfig.CubeBall.PhysicsBounciness.Value,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine   = PhysicsMaterialCombine.Minimum,
                };
                _shapeCollider.sharedMaterial = _shapeMaterial;
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] CubeBallState.Apply: no SphereCollider found on ball."
                );
            }

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

            _shapeChild = ShapeMeshBuilder.CreateShapeObject(Shape);

            // Remove any collider CreatePrimitive may have added.
            var extraCollider = _shapeChild.GetComponent<Collider>();
            if (extraCollider != null)
                Destroy(extraCollider);

            _shapeChild.transform.SetParent(transform, worldPositionStays: false);
            _shapeChild.transform.localPosition = localCenter;
            _shapeChild.transform.localScale    = Vector3.one * localSide;
            _shapeChild.transform.localRotation = Quaternion.identity;

            var mat = primaryRenderer?.sharedMaterial;
            if (mat != null)
                _shapeChild.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        public void Revert()
        {
            if (_sphere != null)
                _sphere.enabled = true;

            if (_shapeCollider != null)
                Destroy(_shapeCollider);

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

        // Returns a collider whose shape approximates the chosen BallShape.
        // A CapsuleCollider covers cylinder/disk; BoxCollider covers everything else.
        private Collider AddShapeCollider(float localSide, Vector3 localCenter)
        {
            switch (Shape)
            {
                case BallShape.Cylinder:
                {
                    var cap = gameObject.AddComponent<CapsuleCollider>();
                    cap.direction = 1; // Y-axis
                    cap.radius    = localSide * 0.5f;
                    cap.height    = localSide;
                    cap.center    = localCenter;
                    return cap;
                }
                case BallShape.Disk:
                {
                    var cap = gameObject.AddComponent<CapsuleCollider>();
                    cap.direction = 1; // Y-axis
                    cap.radius    = localSide * 0.5f;
                    cap.height    = localSide * 0.35f; // flat puck proportions
                    cap.center    = localCenter;
                    return cap;
                }
                default:
                {
                    var box = gameObject.AddComponent<BoxCollider>();
                    box.size   = Vector3.one * localSide;
                    box.center = localCenter;
                    return box;
                }
            }
        }
    }

    /// Builds mesh-only GameObjects for each BallShape.
    /// For shapes Unity exposes as PrimitiveType we use CreatePrimitive;
    /// for the rest (Cone, TriangularPrism, Pyramid) we generate meshes procedurally.
    internal static class ShapeMeshBuilder
    {
        public static GameObject CreateShapeObject(BallShape shape)
        {
            switch (shape)
            {
                case BallShape.Cube:
                    return GameObject.CreatePrimitive(PrimitiveType.Cube);

                case BallShape.Cylinder:
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    // Unity's cylinder is 2 units tall, 1 unit radius — keep as-is for scaling later.
                    return go;
                }

                case BallShape.Disk:
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    // Flatten to 35% height to look like a hockey puck.
                    go.transform.localScale = new Vector3(1f, 0.175f, 1f);
                    return go;
                }

                case BallShape.Cone:
                    return CreateMeshObject("Cone", BuildConeMesh(segments: 16));

                case BallShape.TriangularPrism:
                    return CreateMeshObject("TriangularPrism", BuildTriangularPrismMesh());

                case BallShape.Pyramid:
                    return CreateMeshObject("Pyramid", BuildPyramidMesh());

                default:
                    return GameObject.CreatePrimitive(PrimitiveType.Cube);
            }
        }

        // ── Procedural mesh helpers ────────────────────────────────────────────

        private static GameObject CreateMeshObject(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.mesh = mesh;
            // Caller will set material; assign default so it isn't invisible.
            mr.sharedMaterial = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            return go;
        }

        /// Cone: flat circular base, apex directly above the center.
        /// Fits inside a unit cube (radius 0.5, height 1, centered at origin).
        private static Mesh BuildConeMesh(int segments = 16)
        {
            var mesh = new Mesh { name = "Cone" };

            int vertCount = segments + 2; // base centre + rim verts + apex
            var verts = new Vector3[vertCount];
            var tris  = new int[segments * 6]; // base fan + side triangles

            float r = 0.5f;
            float yBase = -0.5f;
            float yApex = 0.5f;

            // Base centre
            verts[0] = new Vector3(0f, yBase, 0f);
            // Apex
            verts[1] = new Vector3(0f, yApex, 0f);
            // Rim
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                verts[2 + i] = new Vector3(Mathf.Cos(a) * r, yBase, Mathf.Sin(a) * r);
            }

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int cur  = 2 + i;
                int next = 2 + (i + 1) % segments;

                // Base cap (winding: outward normal points down)
                tris[t++] = 0;
                tris[t++] = next;
                tris[t++] = cur;

                // Side (winding: outward normal points away from axis)
                tris[t++] = 1;
                tris[t++] = cur;
                tris[t++] = next;
            }

            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// Triangular prism: equilateral-triangle cross-section, standing upright.
        /// Fits inside a unit cube.
        private static Mesh BuildTriangularPrismMesh()
        {
            var mesh = new Mesh { name = "TriangularPrism" };

            float r    = 0.5f;   // circumradius of triangle
            float yBot = -0.5f;
            float yTop =  0.5f;

            // 3 bottom + 3 top = 6 base verts
            var bottom = new Vector3[3];
            var top    = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                float a = 2f * Mathf.PI * i / 3f - Mathf.PI / 2f;
                bottom[i] = new Vector3(Mathf.Cos(a) * r, yBot, Mathf.Sin(a) * r);
                top[i]    = new Vector3(Mathf.Cos(a) * r, yTop, Mathf.Sin(a) * r);
            }

            // We duplicate verts per face for correct normals:
            // 2 caps × 3 verts + 3 side quads × 4 verts = 6 + 12 = 18 verts
            var verts = new Vector3[18];
            var tris  = new int[3 * 3 + 3 * 6]; // 9 cap tris + 18 side tris

            // Bottom cap
            verts[0] = bottom[0]; verts[1] = bottom[1]; verts[2] = bottom[2];
            // Top cap
            verts[3] = top[0]; verts[4] = top[1]; verts[5] = top[2];
            // Side quads: 3 faces × 4 verts
            for (int i = 0; i < 3; i++)
            {
                int b = 6 + i * 4;
                int n = (i + 1) % 3;
                verts[b + 0] = bottom[i];
                verts[b + 1] = bottom[n];
                verts[b + 2] = top[n];
                verts[b + 3] = top[i];
            }

            int t = 0;
            // Bottom cap (normal down)
            tris[t++] = 0; tris[t++] = 2; tris[t++] = 1;
            // Top cap (normal up)
            tris[t++] = 3; tris[t++] = 4; tris[t++] = 5;
            // Side faces
            for (int i = 0; i < 3; i++)
            {
                int b = 6 + i * 4;
                tris[t++] = b;     tris[t++] = b + 1; tris[t++] = b + 2;
                tris[t++] = b;     tris[t++] = b + 2; tris[t++] = b + 3;
            }

            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// Square-base pyramid.  Fits inside a unit cube.
        private static Mesh BuildPyramidMesh()
        {
            var mesh = new Mesh { name = "Pyramid" };

            float h    = 0.5f;
            float yBot = -0.5f;
            float yTop =  0.5f;

            var baseVerts = new Vector3[]
            {
                new(-h, yBot, -h),
                new( h, yBot, -h),
                new( h, yBot,  h),
                new(-h, yBot,  h),
            };
            var apex = new Vector3(0f, yTop, 0f);

            // Base (4 verts) + 4 side triangles × 3 verts = 4 + 12 = 16 verts
            var verts = new Vector3[16];
            var tris  = new int[6 + 12]; // base 2 tris + 4 side tris

            // Base
            verts[0] = baseVerts[0]; verts[1] = baseVerts[1];
            verts[2] = baseVerts[2]; verts[3] = baseVerts[3];
            // Sides
            for (int i = 0; i < 4; i++)
            {
                int b = 4 + i * 3;
                verts[b + 0] = baseVerts[i];
                verts[b + 1] = baseVerts[(i + 1) % 4];
                verts[b + 2] = apex;
            }

            int t = 0;
            // Base (normal down)
            tris[t++] = 0; tris[t++] = 3; tris[t++] = 2;
            tris[t++] = 0; tris[t++] = 2; tris[t++] = 1;
            // Sides
            for (int i = 0; i < 4; i++)
            {
                int b = 4 + i * 3;
                tris[t++] = b; tris[t++] = b + 1; tris[t++] = b + 2;
            }

            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
