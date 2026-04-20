using IssaPlugin.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a circular crosshair for gun-type items:
    ///   - AK47: shown while right-click is held; ring radius maps the inaccuracy
    ///     config angle onto screen space so bullets land within the ring.
    ///   - Flamethrower: always shown; fixed small ring indicating centre aim.
    ///   - Hunter Drone: shown while right-click is held; fixed small ring indicating
    ///     the target point where the drone will be sent.
    ///
    /// Also corrects the player model's horizontal rotation each frame so the
    /// held weapon tracks the camera:
    ///   - AK47: corrected while right-click is held.
    ///   - Hunter Drone: corrected while right-click is held.
    ///   - Flamethrower: corrected always so flame particles track the camera
    ///     regardless of fire or aim-in state.
    ///
    /// Rendered with GL.QUADS for a fixed-pixel-width ring regardless of size.
    /// A black shadow ring is drawn first for visibility against bright backgrounds.
    /// </summary>
    public class GunCrosshairOverlay : MonoBehaviour
    {
        private Material _mat;
        private Camera _camera;

        private const int Segments = 64;
        private const float LineWidth = 2f; // ring thickness in pixels
        private const float ShadowOffset = 1f; // shadow displacement in pixels

        private bool _aimingIn;

        private void Awake()
        {
            _mat = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
        }

        private void OnDestroy()
        {
            if (_mat != null)
                Destroy(_mat);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo?.Inventory == null)
                return;

            var equipped = localInfo.Inventory.GetEffectivelyEquippedItem(true);
            bool isAK47 = equipped == ItemRegistry.AK47ItemType;
            bool isFlamethrower = equipped == ItemRegistry.FlamethrowerItemType;
            bool isHunterDrone = equipped == ItemRegistry.HunterDroneItemType;

            if (!isAK47 && !isFlamethrower && !isHunterDrone)
                return;

            float screenRadius;

            if (isAK47)
            {
                // AK47: only show while aiming in (right-click held).
                if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
                {
                    if (_aimingIn)
                        FixLookRotationAfterScope();
                    _aimingIn = false;
                    return;
                }

                _aimingIn = true;

                float inaccuracy = ModConfig.AK47.Inaccuracy.Value;
                if (inaccuracy <= 0f || _mat == null)
                    return;

                // Project the inaccuracy half-angle onto screen space using the camera's
                // vertical FOV so the ring edge matches where bullets can actually land.
                _camera ??= Camera.main;
                float vFov = _camera != null ? _camera.fieldOfView : 60f;
                screenRadius =
                    Screen.height
                    / 2f
                    * Mathf.Tan(inaccuracy * Mathf.Deg2Rad)
                    / Mathf.Tan(vFov / 2f * Mathf.Deg2Rad);
            }
            else if (isHunterDrone)
            {
                // Hunter Drone: only show while aiming in (right-click held).
                if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
                    return;

                if (_mat == null)
                    return;
                screenRadius = 20f;
            }
            else // flamethrower — always show, fixed small radius
            {
                if (_mat == null)
                    return;
                screenRadius = 15f;
            }

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;

            GL.PushMatrix();
            GL.LoadPixelMatrix(); // y=0 at bottom; screen centre stays at (cx, cy)
            _mat.SetPass(0);

            // Shadow pass — offset down-right for readability on bright backgrounds.
            DrawRing(
                cx + ShadowOffset,
                cy - ShadowOffset,
                screenRadius,
                new Color(0f, 0f, 0f, 0.6f)
            );

            // Foreground pass — white ring.
            DrawRing(cx, cy, screenRadius, new Color(1f, 1f, 1f, 0.9f));

            GL.PopMatrix();

            if (isAK47 || isHunterDrone)
                CorrectAimRotation();
        }

        private void FixLookRotationAfterScope()
        {
            _camera ??= Camera.main;
            if (_camera != null)
                GameManager
                    .LocalPlayerInfo
                    ?.Movement
                    ?.transform.rotation = Quaternion.LookRotation(
                        _camera.transform.forward.normalized
                    );
        }

        private void LateUpdate()
        {
            // Force the character root to face the camera's aim direction every frame.
            // LateUpdate runs after all Update() calls so this wins the frame for
            // rendering and networking without fighting the physics step.
            var li = GameManager.LocalPlayerInfo;
            if (li?.Inventory == null)
                return;

            if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
                return;

            var equipped = li.Inventory.GetEffectivelyEquippedItem(true);

            if (equipped == ItemRegistry.AK47ItemType || equipped == ItemRegistry.HunterDroneItemType)
            {
                CorrectAimRotation();
                return;
            }

            if (equipped != ItemRegistry.FlamethrowerItemType)
            {
                return;
            }

            if (Mouse.current.leftButton.isPressed)
                CorrectAimRotation(5f);
            else
                CorrectAimRotation(65f);
        }

        private void CorrectAimRotation(float yDegrees = 0f)
        {
            var li = GameManager.LocalPlayerInfo;
            if (li?.Movement == null)
                return;
            _camera ??= Camera.main;
            var cam = _camera;
            if (cam == null)
                return;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                return;
            li.Movement.transform.rotation =
                Quaternion.LookRotation(fwd.normalized) * Quaternion.Euler(0f, yDegrees, 0f);
        }

         
          static void DrawRing(float cx, float cy, float radius, Color color)
        {
            float outerR = radius + LineWidth / 2f;
            float innerR = Mathf.Max(0f, radius - LineWidth / 2f);
            float step = 2f * Mathf.PI / Segments;

            GL.Begin(GL.QUADS);
            GL.Color(color);

            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                float cos0 = Mathf.Cos(a0),
                    sin0 = Mathf.Sin(a0);
                float cos1 = Mathf.Cos(a1),
                    sin1 = Mathf.Sin(a1);

                GL.Vertex3(cx + cos0 * outerR, cy + sin0 * outerR, 0f);
                GL.Vertex3(cx + cos1 * outerR, cy + sin1 * outerR, 0f);
                GL.Vertex3(cx + cos1 * innerR, cy + sin1 * innerR, 0f);
                GL.Vertex3(cx + cos0 * innerR, cy + sin0 * innerR, 0f);
            }

            GL.End();
        }
    }
}
