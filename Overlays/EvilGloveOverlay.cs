using System.Collections.Generic;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Client HUD for Evil Glove aim lock-on: screen reticle on the best ball in
    /// the aim cone while RMB is held. Exposes <see cref="BestTargetOwnerNetId"/>
    /// for <see cref="GloveNetworkBridge.ClientRequestEvilPickup"/>.
    /// </summary>
    public class EvilGloveOverlay : MonoBehaviour
    {
        public static EvilGloveOverlay Instance { get; private set; }

        private GUIStyle _labelStyle;
        private readonly List<(PlayerInfo owner, float angle, float sqDist)> _scratch = new();

        private bool _aimingIn;
        private bool _hasTarget;
        private Vector3 _targetScreenPos;
        private float _sh;

        /// <summary>Player netId of the locked ball owner, or 0.</summary>
        public uint BestTargetOwnerNetId { get; private set; }

        /// <summary>The locked GolfBall this frame, or null.</summary>
        public GolfBall BestTargetBall { get; private set; }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            BestTargetOwnerNetId = 0;
            BestTargetBall = null;
            _hasTarget = false;
            _aimingIn = false;

            var local = GameManager.LocalPlayerInfo;
            var inventory = local?.Inventory;
            if (inventory == null)
                return;

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.EvilGloveItemType)
                return;

            var bridge = local.GetComponent<GloveNetworkBridge>();
            if (bridge != null && bridge.IsHolding)
                return;

            var input = local.Input;
            if (input == null || !input.IsHoldingAimSwing)
                return;

            _aimingIn = true;
            _sh = Screen.height;

            var cam = Camera.main;
            if (cam == null)
                return;

            var owner = GolfBallAimTargeting.SelectBallOwner(
                cam.transform.position,
                cam.transform.forward,
                ModConfig.EvilGlove.MaxAimAngle.Value,
                ModConfig.EvilGlove.MaxTargetDistance.Value,
                _scratch,
                GloveNetworkBridge.IsBallBusy
            );

            if (owner == null)
                return;

            var ball = owner.AsGolfer?.OwnBall;
            if (ball == null)
                return;

            BestTargetOwnerNetId = PlayerBallResolver.GetPlayerNetId(owner);
            BestTargetBall = ball;
            _hasTarget = BestTargetOwnerNetId != 0;

            Vector3 sp = cam.WorldToScreenPoint(ball.transform.position);
            if (sp.z < 0.1f)
            {
                _hasTarget = false;
                BestTargetOwnerNetId = 0;
                BestTargetBall = null;
                return;
            }

            // GUI y is top-down; WorldToScreenPoint is bottom-up.
            _targetScreenPos = new Vector3(sp.x, _sh - sp.y, sp.z);
        }

        private void OnGUI()
        {
            if (!_aimingIn || !_hasTarget)
                return;

            DrawLockOnReticle();
        }

        private void DrawLockOnReticle()
        {
            float cx = _targetScreenPos.x;
            float cy = _targetScreenPos.y;

            float inner = 24f;
            float outer = 36f;
            float thick = 4f;
            // Distinct from Gravity Gun cyan — reddish for "evil".
            Color col = new Color(0.95f, 0.25f, 0.2f, 0.9f);

            GUI.color = col;
            float gap = inner;
            float len = outer - inner;

            GUI.DrawTexture(
                new Rect(cx - outer, cy - thick * 0.5f, len, thick),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx + gap, cy - thick * 0.5f, len, thick),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy - outer, thick, len),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy + gap, thick, len),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = col;
            GUI.Label(new Rect(cx - 80f, cy + outer + 4f, 160f, 24f), "LOCK ON", _labelStyle);
        }
    }
}
