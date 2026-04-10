using IssaPlugin.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a circular crosshair while the AK47 is equipped AND the player is aiming
    /// (right-click held). The circle radius maps the inaccuracy config angle onto
    /// screen space — bullets land anywhere within the ring.
    ///
    /// Rendered with GL.QUADS for a fixed-pixel-width ring regardless of circle size.
    /// A black shadow ring is drawn first for visibility against bright backgrounds.
    /// </summary>
    public class AK47CrosshairOverlay : MonoBehaviour
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

            // Only show while the AK47 is equipped.
            if (localInfo.Inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.AK47ItemType)
                return;

            // Only show while aiming in (right-click held).
            if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
            {
                if (_aimingIn)
                {
                    FixLookRotationAfterScope();
                }
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
            float screenRadius =
                Screen.height
                / 2f
                * Mathf.Tan(inaccuracy * Mathf.Deg2Rad)
                / Mathf.Tan(vFov / 2f * Mathf.Deg2Rad);

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
            // Force the character root to face the camera's aim direction every frame
            // while scoped. The movement system lerps toward camera facing on its own
            // (IsAimingItem=true via the base game path), but without IsAimingSwing=true
            // the lerp is slow enough that the player sees a constant CCW offset for the
            // first several frames. LateUpdate runs after all Update() calls, so this
            // wins the frame for rendering and networking without fighting the physics step.
            // Only show while the AK47 is equipped.
            if (
                GameManager.LocalPlayerInfo?.Inventory.GetEffectivelyEquippedItem(true)
                != ItemRegistry.AK47ItemType
            )
            {
                return;
            }

            // Only show while aiming in (right-click held).
            if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
            {
                return;
            }

            CorrectAimRotation();
        }

        private void CorrectAimRotation()
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
                Quaternion.LookRotation(fwd.normalized) * Quaternion.Euler(0f, 5f, 0f);
        }

        private static void DrawRing(float cx, float cy, float radius, Color color)
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
