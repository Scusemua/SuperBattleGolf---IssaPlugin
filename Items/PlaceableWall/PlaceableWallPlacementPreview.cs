using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace IssaPlugin.Items
{
    /// Client-side component added to the local player's inventory object while the
    /// Placeable Wall item is equipped (via PlaceableWallItemDefinition.OnEquip).
    ///
    /// While right-click is held:
    ///   • Raycasts from the camera to find a ground surface.
    ///   • Shows a ghost of the wall at the projected position.
    ///   • Colours the ghost green (valid) or red (invalid).
    ///
    /// Placement is invalid when:
    ///   • The position is within MinHoleDistance of the golf hole, or
    ///   • The position directly overlaps an existing player.
    ///
    /// Press R while right-click is held to rotate the wall in 90° increments.
    /// The accumulated rotation is exposed as CurrentRotationOffset so that
    /// ClientPlace() can forward it to the server.
    ///
    /// The component removes itself when the wall item is no longer equipped.
    public class PlaceableWallPlacementPreview : MonoBehaviour
    {
        // ── State ────────────────────────────────────────────────────────

        /// Accumulated extra yaw (degrees, multiples of 90) from the R key.
        /// Read by PlaceableWallNetworkBridge.ClientPlace().
        public float CurrentRotationOffset { get; private set; }

        private GameObject _ghost;

        // ── Materials ────────────────────────────────────────────────────

        // Lazily created and shared across all preview instances (one per session).
        private static Material _validMat;
        private static Material _invalidMat;

        // ── Unity lifecycle ──────────────────────────────────────────────

        private void Update()
        {
            var inventory = GetComponent<PlayerInventory>();
            if (
                inventory == null
                || inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.PlaceableWallItemType
            )
            {
                // Item was unequipped — clean up and remove this component.
                DestroyGhost();
                Destroy(this);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
            {
                HideGhost();
                return;
            }

            // Scroll wheel rotates the wall: up = clockwise, down = counter-clockwise.
            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (scrollY != 0f)
                CurrentRotationOffset =
                    (CurrentRotationOffset + Mathf.Sign(scrollY) * Configuration.PlaceableWallRotationStep.Value)
                    % 360f;

            var cam = Camera.main;
            if (cam == null)
            {
                HideGhost();
                return;
            }

            float maxDist = Configuration.PlaceableWallMaxPlacementDistance.Value;
            if (
                !Physics.Raycast(
                    cam.transform.position,
                    cam.transform.forward,
                    out RaycastHit hit,
                    maxDist,
                    ItemHelper.GroundLayerMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                HideGhost();
                return;
            }

            Vector3 placePos = hit.point;

            Vector3 cameraXZ = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (cameraXZ.sqrMagnitude < 0.001f)
                cameraXZ = Vector3.forward;
            Quaternion baseRot = Quaternion.LookRotation(-cameraXZ.normalized, Vector3.up);
            Quaternion finalRot = baseRot * Quaternion.Euler(0f, CurrentRotationOffset, 0f);

            ShowGhost(placePos, finalRot);

            bool valid = IsPlacementValid(placePos, finalRot);
            ApplyMaterial(valid ? GetValidMat() : GetInvalidMat());
        }

        private void OnDestroy()
        {
            DestroyGhost();
        }

        // ── Ghost management ─────────────────────────────────────────────

        private void ShowGhost(Vector3 position, Quaternion rotation)
        {
            if (_ghost == null)
            {
                if (AssetLoader.WallGhostPrefab == null)
                    return;

                _ghost = Object.Instantiate(
                    AssetLoader.WallGhostPrefab,
                    position,
                    rotation
                );
                _ghost.name = "PlaceableWall_Ghost";
                _ghost.SetActive(true);
            }
            else
            {
                _ghost.transform.SetPositionAndRotation(position, rotation);
                if (!_ghost.activeSelf)
                    _ghost.SetActive(true);
            }
        }

        private void HideGhost()
        {
            if (_ghost != null && _ghost.activeSelf)
                _ghost.SetActive(false);
        }

        private void DestroyGhost()
        {
            if (_ghost != null)
            {
                Object.Destroy(_ghost);
                _ghost = null;
            }
        }

        private void ApplyMaterial(Material mat)
        {
            if (_ghost == null || mat == null)
                return;

            foreach (var mr in _ghost.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = new Material[mr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = mat;
                mr.sharedMaterials = mats;
            }
        }

        // ── Placement validity ───────────────────────────────────────────

        private static bool IsPlacementValid(Vector3 position, Quaternion rotation)
        {
            // ── Hole distance check ──────────────────────────────────────
            float minHoleDist = Configuration.PlaceableWallMinHoleDistance.Value;
            if (minHoleDist > 0f)
            {
                var mainHole = GolfHoleManager.MainHole;
                if (mainHole != null)
                {
                    Vector3 holePos = mainHole.transform.position;
                    float xzDist = new Vector2(
                        position.x - holePos.x,
                        position.z - holePos.z
                    ).magnitude;
                    if (xzDist < minHoleDist)
                        return false;
                }
            }

            // ── Player overlap check ─────────────────────────────────────
            // Use the wall prefab's local scale to approximate the wall's extents.
            // A small inward margin avoids false positives from edge-grazing colliders.
            Vector3 scale =
                AssetLoader.WallPrefab != null
                    ? AssetLoader.WallPrefab.transform.localScale
                    : new Vector3(4f, 3f, 0.3f);
            Vector3 halfExtents = scale * 0.45f;

            // Centre the box at wall mid-height above the placement point.
            Vector3 boxCenter = position + Vector3.up * halfExtents.y;

            var overlaps = Physics.OverlapBox(
                boxCenter,
                halfExtents,
                rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore
            );

            foreach (var col in overlaps)
            {
                if (col.GetComponentInParent<PlayerMovement>() != null)
                    return false;
            }

            return true;
        }

        // ── Material helpers ─────────────────────────────────────────────

        private static Material GetValidMat()
        {
            if (_validMat == null)
                _validMat = BuildGhostMaterial(new Color(0f, 1f, 0f, 0.35f));
            return _validMat;
        }

        private static Material GetInvalidMat()
        {
            if (_invalidMat == null)
                _invalidMat = BuildGhostMaterial(new Color(1f, 0f, 0f, 0.35f));
            return _invalidMat;
        }

        /// Builds one ghost material.
        ///
        /// Priority:
        ///   1. Custom/GhostPlacement shader — if the user's shader is in the loaded
        ///      asset bundle, Shader.Find returns it immediately after bundle.Load().
        ///   2. Standard shader in transparent mode — universal fallback.
        private static Material BuildGhostMaterial(Color color)
        {
            // 1. Try the custom fresnel ghost shader.
            var ghostShader = Shader.Find("Custom/GhostPlacement");
            if (ghostShader != null)
            {
                var mat = new Material(ghostShader);
                mat.SetColor("_Color", color);
                return mat;
            }

            // 2. Fallback: Standard shader configured for alpha transparency.
            var fallback = Shader.Find("Standard");
            if (fallback == null)
                return null;

            var standard = new Material(fallback);
            // Standard shader transparency mode = 3 (Transparent).
            standard.SetFloat("_Mode", 3f);
            standard.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            standard.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            standard.SetInt("_ZWrite", 0);
            standard.DisableKeyword("_ALPHATEST_ON");
            standard.EnableKeyword("_ALPHABLEND_ON");
            standard.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            standard.renderQueue = 3000;
            standard.color = color;
            return standard;
        }
    }
}
