using UnityEngine;

namespace IssaPlugin.Items
{
    /// Applied to a GolfBall's GameObject when the cube-ball effect is active.
    ///
    /// On Apply():
    ///   • Disables the GolfBall's SphereCollider and adds a BoxCollider of the
    ///     same diameter so the server's physics simulation rolls it as a cube.
    ///   • Swaps every MeshFilter in the ball's hierarchy to a cube mesh so the
    ///     ball is visually a cube on all clients.
    ///
    /// On Revert():
    ///   • Re-enables the SphereCollider and destroys the BoxCollider.
    ///   • Restores all original meshes.
    ///   • Destroys this component.
    public class CubeBallState : MonoBehaviour
    {
        private SphereCollider _sphere;
        private BoxCollider _box;
        private MeshFilter[] _filters;
        private Mesh[] _originalMeshes;

        public void Apply()
        {
            // ── Collider swap ─────────────────────────────────────────────────
            _sphere = GetComponent<SphereCollider>();

            if (_sphere != null)
            {
                _sphere.enabled = false;

                _box = gameObject.AddComponent<BoxCollider>();
                float diameter = _sphere.radius * 2f;
                _box.size = new Vector3(diameter, diameter, diameter);
                _box.center = _sphere.center;
                _box.sharedMaterial = _sphere.sharedMaterial;
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] CubeBallState.Apply: no SphereCollider found on ball."
                );
            }

            // ── Mesh swap ─────────────────────────────────────────────────────
            var cubeMesh = AssetLoader.CubeMesh;
            if (cubeMesh == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] CubeBallState.Apply: CubeMesh is null — skipping mesh swap."
                );
                return;
            }

            _filters = GetComponentsInChildren<MeshFilter>(includeInactive: true);
            _originalMeshes = new Mesh[_filters.Length];
            for (int i = 0; i < _filters.Length; i++)
            {
                _originalMeshes[i] = _filters[i].sharedMesh;
                _filters[i].sharedMesh = cubeMesh;
            }
        }

        public void Revert()
        {
            // ── Restore colliders ─────────────────────────────────────────────
            if (_sphere != null)
                _sphere.enabled = true;

            if (_box != null)
                Destroy(_box);

            // ── Restore meshes ────────────────────────────────────────────────
            if (_filters != null)
            {
                for (int i = 0; i < _filters.Length; i++)
                {
                    if (_filters[i] != null)
                        _filters[i].sharedMesh = _originalMeshes[i];
                }
            }

            Destroy(this);
        }
    }
}
