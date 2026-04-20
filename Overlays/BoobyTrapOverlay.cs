using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Client-side HUD component on the Plugin's persistent GameObject.
    ///
    /// Modes
    /// ─────
    ///   WorldTarget   — scans for item boxes and golf carts within range;
    ///                   camera-direction bias picks the best one (highest dot
    ///                   product with camera forward, same as GravityGunOverlay).
    ///   InventoryItem — renders slot icons; player cycles with Q/E keys then
    ///                   confirms with Use.
    ///
    /// Right-click toggles between the two modes at any time.
    /// </summary>
    public class BoobyTrapOverlay : MonoBehaviour
    {
        // ── Mode ──────────────────────────────────────────────────────────────

        public enum TrapMode
        {
            WorldTarget,
            InventoryItem,
        }

        // ── Singleton ─────────────────────────────────────────────────────────

        public static BoobyTrapOverlay Instance { get; private set; }

        // ── World-target state (updated in Update, read by OnGUI + ItemDefinition) ──

        public TrapMode Mode { get; private set; } = TrapMode.WorldTarget;
        public bool HasWorldTarget { get; private set; }
        public BoobyTrapTargetType BestTargetType { get; private set; }
        public uint BestTargetNetId { get; private set; }

        private Vector3 _bestTargetScreenPos;

        // ── Inventory-item state ──────────────────────────────────────────────

        public int HighlightedSlotIndex { get; private set; } = -1;

        // ── World-object caches ───────────────────────────────────────────────

        private ItemSpawner[] _cachedSpawners = System.Array.Empty<ItemSpawner>();
        private GolfCartInfo[] _cachedCarts = System.Array.Empty<GolfCartInfo>();
        private float _objectCacheTime = float.MinValue;
        private const float ObjectCacheInterval = 1f;

        // ── VFX tracking for hole-transition cleanup ──────────────────────────

        private readonly Dictionary<uint, GameObject> _trappedVfxByNetId = new();

        // ── "No target" feedback ──────────────────────────────────────────────

        private float _noTargetUntil;

        // ── GUI state ─────────────────────────────────────────────────────────

        private GUIStyle _labelStyle;
        private GUIStyle _slotLabelStyle;
        private float _sw;
        private float _sh;

        // Input edge-detect
        private bool _wasRightClickDown;
        private bool _wasCycleLeftDown;
        private bool _wasCycleRightDown;

        // Reflected access to the private slots list on PlayerInventory.
        private static readonly System.Reflection.FieldInfo SlotsField = AccessTools.Field(
            typeof(PlayerInventory),
            "slots"
        );

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            _sw = Screen.width;
            _sh = Screen.height;

            if (!IsBoobyTrapEquipped())
            {
                HasWorldTarget = false;
                return;
            }

            HandleModeToggle();

            if (Mode == TrapMode.WorldTarget)
                UpdateWorldTargetScan();
            else
                UpdateInventorySlotCycle();
        }

        // ── Equipped check ────────────────────────────────────────────────────

        private static bool IsBoobyTrapEquipped()
        {
            var inv = GameManager.LocalPlayerInventory;
            return inv != null
                && inv.GetEffectivelyEquippedItem(true) == ItemRegistry.BoobyTrapItemType;
        }

        // ── Mode toggle ───────────────────────────────────────────────────────

        private void HandleModeToggle()
        {
            bool rightDown = Mouse.current != null && Mouse.current.rightButton.isPressed;

            if (rightDown && !_wasRightClickDown)
            {
                Mode = Mode == TrapMode.WorldTarget ? TrapMode.InventoryItem : TrapMode.WorldTarget;

                HasWorldTarget = false;
                HighlightedSlotIndex = -1;
            }

            _wasRightClickDown = rightDown;
        }

        // ── World-target scan (camera-direction bias) ─────────────────────────

        private void UpdateWorldTargetScan()
        {
            HasWorldTarget = false;
            BestTargetNetId = 0;

            var cam = Camera.main;
            if (cam == null)
                return;

            float range = ModConfig.BoobyTrap.Range.Value;
            float rangeSq = range * range;
            float cosHalf = Mathf.Cos(
                ModConfig.BoobyTrap.AimConeAngle.Value * 0.5f * Mathf.Deg2Rad
            );

            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;

            float bestDot = -1f;
            Transform bestTransform = null;
            uint bestNetId = 0;
            BoobyTrapTargetType bestType = BoobyTrapTargetType.ItemBox;

            RefreshObjectCaches();

            // ── Scan item boxes ───────────────────────────────────────────────
            foreach (var spawner in _cachedSpawners)
            {
                if (spawner == null || !spawner.gameObject.activeInHierarchy)
                    continue;
                if (!spawner.NetworkhasItemBox)
                    continue;

                var ni = spawner.GetComponent<NetworkIdentity>();
                if (ni == null)
                    continue;

                Vector3 toTarget = spawner.transform.position - camPos;
                if (toTarget.sqrMagnitude > rangeSq)
                    continue;

                float dot = Vector3.Dot(camFwd, toTarget.normalized);
                if (dot < cosHalf || dot <= bestDot)
                    continue;

                bestDot = dot;
                bestTransform = spawner.transform;
                bestNetId = ni.netId;
                bestType = BoobyTrapTargetType.ItemBox;
            }

            // ── Scan golf carts ───────────────────────────────────────────────
            foreach (var cart in _cachedCarts)
            {
                if (cart == null || !cart.gameObject.activeInHierarchy)
                    continue;

                var ni = cart.GetComponent<NetworkIdentity>();
                if (ni == null)
                    continue;

                Vector3 toTarget = cart.transform.position - camPos;
                if (toTarget.sqrMagnitude > rangeSq)
                    continue;

                float dot = Vector3.Dot(camFwd, toTarget.normalized);
                if (dot < cosHalf || dot <= bestDot)
                    continue;

                bestDot = dot;
                bestTransform = cart.transform;
                bestNetId = ni.netId;
                bestType = BoobyTrapTargetType.GolfCart;
            }

            if (bestTransform == null)
                return;

            Vector3 screenPos = cam.WorldToScreenPoint(bestTransform.position);
            if (screenPos.z <= 0f)
                return;

            BestTargetNetId = bestNetId;
            BestTargetType = bestType;
            _bestTargetScreenPos = new Vector3(screenPos.x, _sh - screenPos.y, 0f);
            HasWorldTarget = true;
        }

        private void RefreshObjectCaches()
        {
            if (!(Time.time - _objectCacheTime >= ObjectCacheInterval))
                return;

            _cachedSpawners = FindObjectsByType<ItemSpawner>(FindObjectsSortMode.None);
            _cachedCarts = FindObjectsByType<GolfCartInfo>(FindObjectsSortMode.None);
            _objectCacheTime = Time.time;
        }

        // ── Inventory-slot cycling ────────────────────────────────────────────

        private void UpdateInventorySlotCycle()
        {
            var inv = GameManager.LocalPlayerInventory;
            if (inv == null)
                return;

            if (HighlightedSlotIndex < 0)
                HighlightedSlotIndex = FindNextValidSlot(inv, -1, +1);

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            bool cycleLeft = keyboard.qKey.isPressed;
            bool cycleRight = keyboard.eKey.isPressed;

            if (cycleLeft && !_wasCycleLeftDown)
                HighlightedSlotIndex = FindNextValidSlot(inv, HighlightedSlotIndex, -1);

            if (cycleRight && !_wasCycleRightDown)
                HighlightedSlotIndex = FindNextValidSlot(inv, HighlightedSlotIndex, +1);

            _wasCycleLeftDown = cycleLeft;
            _wasCycleRightDown = cycleRight;
        }

        private static InventorySlot GetSlot(PlayerInventory inv, int index)
        {
            var slots = SlotsField?.GetValue(inv) as IList<InventorySlot>;
            if (slots == null || index < 0 || index >= slots.Count)
                return InventorySlot.Empty;
            return slots[index];
        }

        private static int GetSlotCount(PlayerInventory inv)
        {
            var slots = SlotsField?.GetValue(inv) as IList<InventorySlot>;
            return slots?.Count ?? GameManager.PlayerInventorySettings.MaxItems;
        }

        /// Finds the next valid inventory slot in <paramref name="direction"/> (+1 or -1),
        /// wrapping around. Returns -1 if no valid slot exists.
        private static int FindNextValidSlot(PlayerInventory inv, int current, int direction)
        {
            int count = GetSlotCount(inv);
            if (count <= 0)
                return -1;

            int start = current < 0 ? 0 : (current + direction + count) % count;

            for (int i = 0; i < count; i++)
            {
                int idx = (start + i * (direction >= 0 ? 1 : -1) + count * count) % count;
                var slot = GetSlot(inv, idx);
                if (
                    slot.itemType != ItemType.None
                    && slot.itemType != ItemRegistry.BoobyTrapItemType
                )
                    return idx;
            }

            return -1;
        }

        // ── VFX: trap-placed notification from server ─────────────────────────

        /// Called by BoobyTrapNetworkBridge.HandleBoobyTrapPlaced.
        public void ClientOnTrapPlaced(uint targetNetId, BoobyTrapTargetType targetType)
        {
            if (_trappedVfxByNetId.TryGetValue(targetNetId, out var existing))
            {
                if (existing != null)
                    Destroy(existing);
                _trappedVfxByNetId.Remove(targetNetId);
            }

            if (AssetLoader.BoobyTrapVfxPrefab == null)
                return;

            if (!NetworkClient.spawned.TryGetValue(targetNetId, out var ni) || ni == null)
                return;

            var vfx = Instantiate(AssetLoader.BoobyTrapVfxPrefab, ni.transform);
            vfx.transform.localPosition = Vector3.up * 0.5f;
            _trappedVfxByNetId[targetNetId] = vfx;
        }

        // ── "No target" hint ──────────────────────────────────────────────────

        public void ShowNoTarget() => _noTargetUntil = Time.time + 1.5f;

        // ── Hole cleanup ──────────────────────────────────────────────────────

        public void ClientHoleCleanup()
        {
            foreach (var vfx in _trappedVfxByNetId.Values)
                if (vfx != null)
                    Destroy(vfx);

            _trappedVfxByNetId.Clear();
            HasWorldTarget = false;
            Mode = TrapMode.WorldTarget;
            HighlightedSlotIndex = -1;
            _noTargetUntil = 0f;
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (!IsBoobyTrapEquipped())
                return;

            if (_sw <= 0f || _sh <= 0f)
                return;

            EnsureStyles();

            if (Mode == TrapMode.WorldTarget)
                DrawWorldTargetHUD();
            else
                DrawInventoryHUD();

            if (Time.time < _noTargetUntil)
            {
                _labelStyle.normal.textColor = new Color(1f, 0.4f, 0.1f, 0.9f);
                GUI.Label(
                    new Rect(0, _sh - 80f, _sw, 40f),
                    "No target — aim at an item box or golf cart, or switch to inventory mode",
                    _labelStyle
                );
                _labelStyle.normal.textColor = Color.white;
            }

            _slotLabelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            string modeHint =
                Mode == TrapMode.WorldTarget
                    ? "[RMB] Switch to Inventory mode"
                    : "[RMB] Switch to World-Target mode";
            GUI.Label(new Rect(0, _sh - 22f, _sw, 20f), modeHint, _slotLabelStyle);
            _slotLabelStyle.normal.textColor = Color.white;
        }

        // ── World-target HUD ──────────────────────────────────────────────────

        private void DrawWorldTargetHUD()
        {
            if (!HasWorldTarget)
                return;

            float cx = _bestTargetScreenPos.x;
            float cy = _bestTargetScreenPos.y;

            Color col =
                BestTargetType == BoobyTrapTargetType.ItemBox
                    ? new Color(1f, 0.85f, 0f, 0.9f) // yellow for item boxes
                    : new Color(1f, 0.25f, 0.25f, 0.9f); // red for golf carts

            float inner = 22f,
                outer = 34f,
                thick = 3f;

            GUI.color = col;
            GUI.DrawTexture(
                new Rect(cx - outer, cy - thick * 0.5f, outer - inner, thick),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx + inner, cy - thick * 0.5f, outer - inner, thick),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy - outer, thick, outer - inner),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy + inner, thick, outer - inner),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string label =
                BestTargetType == BoobyTrapTargetType.ItemBox ? "TRAP ITEM BOX" : "TRAP GOLF CART";
            _labelStyle.normal.textColor = col;
            GUI.Label(new Rect(cx - 80f, cy + outer + 4f, 160f, 36f), label, _labelStyle);
            _labelStyle.normal.textColor = Color.white;
        }

        // ── Inventory HUD ─────────────────────────────────────────────────────

        private void DrawInventoryHUD()
        {
            var inv = GameManager.LocalPlayerInventory;
            if (inv == null)
                return;

            int slotCount = GetSlotCount(inv);
            if (slotCount <= 0)
                return;

            const float SlotSize = 52f;
            const float SlotPad = 6f;
            float totalW = slotCount * (SlotSize + SlotPad) - SlotPad;
            float startX = (_sw - totalW) * 0.5f;
            float startY = _sh - SlotSize - 48f;

            for (int i = 0; i < slotCount; i++)
            {
                var slot = GetSlot(inv, i);
                float x = startX + i * (SlotSize + SlotPad);
                bool isHighlighted = i == HighlightedSlotIndex;
                bool isUnusable =
                    slot.itemType == ItemType.None
                    || slot.itemType == ItemRegistry.BoobyTrapItemType;

                // Background box.
                GUI.color = isHighlighted
                    ? new Color(1f, 0.35f, 0.1f, 0.85f)
                    : new Color(0.1f, 0.1f, 0.1f, 0.6f);
                GUI.DrawTexture(new Rect(x, startY, SlotSize, SlotSize), Texture2D.whiteTexture);
                GUI.color = Color.white;

                if (slot.itemType != ItemType.None)
                {
                    var def = ItemRegistry.GetDefinition(slot.itemType);
                    if (def?.Icon != null)
                    {
                        GUI.color = isUnusable ? new Color(1, 1, 1, 0.3f) : Color.white;
                        GUI.DrawTexture(
                            new Rect(x + 4f, startY + 4f, SlotSize - 8f, SlotSize - 8f),
                            def.Icon.texture
                        );
                        GUI.color = Color.white;
                    }

                    _slotLabelStyle.normal.textColor = isHighlighted
                        ? Color.white
                        : new Color(1f, 1f, 1f, 0.7f);
                    GUI.Label(
                        new Rect(x, startY + SlotSize - 18f, SlotSize, 18f),
                        $"×{slot.remainingUses}",
                        _slotLabelStyle
                    );
                    _slotLabelStyle.normal.textColor = Color.white;
                }

                // Red X bar over unusable slots.
                if (isUnusable)
                {
                    GUI.color = new Color(1f, 0.2f, 0.2f, 0.6f);
                    GUI.DrawTexture(
                        new Rect(x, startY + SlotSize * 0.5f - 1f, SlotSize, 2f),
                        Texture2D.whiteTexture
                    );
                    GUI.color = Color.white;
                }
            }

            _labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(
                new Rect(0, startY - 30f, _sw, 26f),
                "[Q] Prev   [E] Next   [Use] Confirm Trap",
                _labelStyle
            );
            _labelStyle.normal.textColor = Color.white;
        }

        // ── Style helpers ─────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white },
                };
            }

            if (_slotLabelStyle == null)
            {
                _slotLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white },
                };
            }
        }
    }
}
