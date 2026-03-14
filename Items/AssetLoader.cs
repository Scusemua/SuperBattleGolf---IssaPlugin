using System.IO;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class AssetLoader
    {
        public static Sprite BatIcon { get; private set; }
        public static Sprite BomberIcon { get; private set; }
        public static Sprite MissileIcon { get; private set; }
        public static Sprite AC130Icon { get; private set; }
        public static Sprite FreezeIcon { get; private set; }
        public static Sprite LowGravityIcon { get; private set; }

        public static Sprite SniperRifleIcon { get; private set; }
        public static Sprite UFOIcon { get; private set; }

        public static Texture2D SniperScopeTexture { get; private set; }

        public static GameObject BatModelPrefab { get; private set; }
        public static GameObject BomberPrefab { get; private set; }
        public static GameObject BomberProxyPrefab { get; private set; }
        public static GameObject AC130Prefab { get; private set; }
        public static GameObject BomberTabletPrefab { get; private set; }
        public static GameObject MissileTabletPrefab { get; private set; }
        public static GameObject Ac130TabletPrefab { get; private set; }
        public static GameObject FreezeModelPrefab { get; private set; }
        public static GameObject LowGravityModelPrefab { get; private set; }

        public static GameObject SniperRiflePrefab { get; private set; }
        public static GameObject UFOPrefab { get; private set; }
        public static GameObject UFOHandheldPrefab { get; private set; }

        public static GameObject BloodSplatterPrefab { get; private set; }

        /// Programmatically-built prefab for dropped custom items.
        /// Root carries NetworkIdentity, NetworkTransform, Rigidbody, SphereCollider,
        /// Entity, and DroppedCustomItem.  The visual child is added client-side in
        /// DroppedCustomItem.OnStartClient() from the synced ItemType.
        public static GameObject DroppedCustomItemPrefab { get; private set; }

        // ----------------------------------------------------------------
        //  AC130 Mayday — populate the asset bundle with these names.
        //  All are optional: code guards null checks before instantiating.
        // ----------------------------------------------------------------

        /// Particle / VFX prefab parented to the gunship during the mayday dive.
        public static GameObject AC130SmokePrefab { get; private set; }

        /// Explosion VFX prefab spawned at the crash impact point.
        public static GameObject AC130MaydayExplosionPrefab { get; private set; }

        /// Secondary debris / dust VFX spawned at the crash site.
        public static GameObject AC130ImpactVfxPrefab { get; private set; }

        // ----------------------------------------------------------------
        //  Audio
        // ----------------------------------------------------------------
        public static AudioClip AC130AboveClip { get; private set; }
        public static AudioClip HomerunAudioClip { get; private set; }

        // --- AC130 Mayday assets (placeholders — add to bundle when ready) ---
        /// Looping cockpit alarm that plays during the mayday dive.
        public static AudioClip MaydayAlarmClip { get; private set; }

        /// One-shot impact / explosion sound at crash site.
        public static AudioClip MaydayImpactClip { get; private set; }

        /// Smoke trail particle prefab — attached to the gunship during the dive.
        public static GameObject MaydaySmokeTrailPrefab { get; private set; }
        public static GameObject MaydayFireTrailPrefab { get; private set; }

        /// Impact explosion VFX prefab — spawned at the crash position.
        public static GameObject MaydayExplosionVfxPrefab { get; private set; }

        public static bool IsLoaded => _bundle != null;

        private static AssetBundle _bundle;

        public static void Load()
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string bundlePath = Path.Combine(pluginDir, "Bundle", "issamod");

            if (!File.Exists(bundlePath))
            {
                bundlePath = Path.Combine(pluginDir, "issamod");
                if (!File.Exists(bundlePath))
                {
                    IssaPluginPlugin.Log.LogWarning(
                        "[Assets] Asset bundle not found. "
                            + "Place 'issamod' in Bundle/ next to the plugin DLL."
                    );
                    return;
                }
            }

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null)
            {
                IssaPluginPlugin.Log.LogError("[Assets] Failed to load asset bundle.");
                return;
            }

            BatIcon = LoadSprite("bat_icon.png");
            BomberIcon = LoadSprite("bomber_icon.png");
            MissileIcon = LoadSprite("missile_icon.png");
            AC130Icon = LoadSprite("ac130_icon.png");
            FreezeIcon = LoadSprite("freeze_effect_icon.png");
            LowGravityIcon = LoadSprite("gravity_remote_icon.png");
            SniperRifleIcon = LoadSprite("sniper_rifle_icon.png");
            UFOIcon = LoadSprite("ufo_icon.png");

            SniperScopeTexture = LoadTexture2D("sniper_scope.png");

            if (SniperScopeTexture == null)
            {
                IssaPluginPlugin.Log.LogError("[Assets] Failed to load sniper scope texture.");
            }

            BatModelPrefab = Load<GameObject>("bat_model.prefab");
            DisableRigidbody(BatModelPrefab);
            BomberPrefab = Load<GameObject>("bomber_model.prefab");
            BomberProxyPrefab = Load<GameObject>("bomber_proxy.prefab");
            EnsureNetworkIdentity(BomberProxyPrefab, 0xB0AA0001u);
            if (BomberProxyPrefab != null)
                BomberProxyPrefab.AddComponent<BomberProxyClientSetup>();

            AC130Prefab = Load<GameObject>("ac130_model.prefab");
            EnsureNetworkIdentity(AC130Prefab, 0xAC130001u);
            if (AC130Prefab != null)
                AC130Prefab.AddComponent<AC130ClientSetup>();
            BomberTabletPrefab = Load<GameObject>("stealth_bomber_tablet.prefab");
            MissileTabletPrefab = Load<GameObject>("predator_missile_tablet.prefab");
            Ac130TabletPrefab = Load<GameObject>("ac130_tablet.prefab");
            FreezeModelPrefab = Load<GameObject>("snowball.prefab");
            LowGravityModelPrefab = Load<GameObject>("gravity_remote.prefab");
            SniperRiflePrefab = Load<GameObject>("intervention.prefab");
            BloodSplatterPrefab = Load<GameObject>("blood_splatter_critical.prefab");

            UFOPrefab = Load<GameObject>("ufo_vehicle.prefab");
            EnsureNetworkIdentity(UFOPrefab, 0xF0000001u);
            if (UFOPrefab != null)
                UFOPrefab.AddComponent<UFOClientSetup>();
            DisableRigidbody(UFOPrefab);

            UFOHandheldPrefab = Load<GameObject>("ufo_handheld.prefab");
            // StripNetworkComponents(UFOHandheldPrefab);

            // Set Kinematic to True and Use Gravity to False.
            // We'll toggle them to true if they're dropped.
            DisableRigidbody(BomberTabletPrefab);
            DisableRigidbody(MissileTabletPrefab);
            DisableRigidbody(Ac130TabletPrefab);
            DisableRigidbody(FreezeModelPrefab);
            DisableRigidbody(LowGravityModelPrefab);
            DisableRigidbody(SniperRiflePrefab);
            DisableRigidbody(UFOHandheldPrefab);

            // AudioClips must be loaded by asset name without the file extension.
            // Unity compiles audio into its own internal format at bundle-build
            // time, so the original .ogg path is never valid at runtime.
            AC130AboveClip = Load<AudioClip>("ac130_above.ogg");
            HomerunAudioClip = Load<AudioClip>("homerun.ogg");

            // Mayday assets — optional until added to the bundle; all usage sites null-check.
            MaydayAlarmClip = Load<AudioClip>("missile_locked.ogg");
            MaydayImpactClip = Load<AudioClip>("etfx_explosion_nuke.wav");
            MaydaySmokeTrailPrefab = Load<GameObject>("smoke_prefab.prefab");
            MaydayFireTrailPrefab = Load<GameObject>("fire_torch_intense.prefab");
            MaydayExplosionVfxPrefab = Load<GameObject>("NukeVerticalExplosionFire.prefab");

            DroppedCustomItemPrefab = Load<GameObject>("DroppedCustomItem.prefab");
            if (DroppedCustomItemPrefab != null)
            {
                EnsureNetworkIdentity(DroppedCustomItemPrefab, 0xD20D0001u);
                DroppedCustomItemPrefab.SetActive(false);
                // Force kinematic regardless of what the bundle has baked in.
                DisableRigidbody(DroppedCustomItemPrefab);
                // Make the pickup collider a trigger so it doesn't block player movement.
                // Physics.OverlapBoxNonAlloc uses QueryTriggerInteraction.Collide, so
                // triggers are still detected by PlayerInteractableTargeter.
                var dropCol = DroppedCustomItemPrefab.GetComponent<SphereCollider>();
                if (dropCol)
                    dropCol.isTrigger = true;
                DroppedCustomItemPrefab.AddComponent<Entity>();
                DroppedCustomItemPrefab.AddComponent<DroppedCustomItem>();
                GameObject.DontDestroyOnLoad(DroppedCustomItemPrefab);
            }

            IssaPluginPlugin.Log.LogInfo("[Assets] Bundle loaded.");
        }

        /// Ensures a prefab has a NetworkIdentity with a stable assetId so Mirror
        /// can spawn it on clients. If the prefab has no NetworkIdentity one is added.
        /// If the baked-in assetId is 0 (bundle built without Mirror's editor tool),
        /// the stable uint is written via reflection so RegisterPrefab doesn't skip it.
        private static void EnsureNetworkIdentity(GameObject prefab, uint stableAssetId)
        {
            if (prefab == null)
                return;

            var assetIdField = typeof(NetworkIdentity).GetField(
                "assetId",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            var ni = prefab.GetComponent<NetworkIdentity>();
            if (ni == null)
            {
                ni = prefab.AddComponent<NetworkIdentity>();
                assetIdField?.SetValue(ni, stableAssetId);
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] Added NetworkIdentity to {prefab.name} with assetId={stableAssetId}."
                );
            }
            else if (ni.assetId == 0)
            {
                assetIdField?.SetValue(ni, stableAssetId);
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] {prefab.name} had assetId=0; set stable assetId={stableAssetId}."
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] {prefab.name} already has NetworkIdentity (assetId={ni.assetId})."
                );
            }

            GameObject.DontDestroyOnLoad(prefab);
        }

        // Helper that warns on null.
        private static T Load<T>(string name)
            where T : UnityEngine.Object
        {
            var asset = _bundle.LoadAsset<T>(name);
            if (asset == null)
                IssaPluginPlugin.Log.LogWarning($"[Assets] Missing asset: {name}");
            return asset;
        }

        private static void DisableRigidbody(GameObject go)
        {
            if (go == null)
                return;
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            rb.isKinematic = true;
            rb.useGravity = false;
        }

        /// Destroys Mirror network tick components from a prefab that will only
        /// ever be used as a local visual (held item / dropped model).  Without
        /// this, NetworkTransformReliable and NetworkRigidbodyReliable start
        /// updating every frame and throw NullReferenceException because the
        /// prefab instance has no network context.
        private static void StripNetworkComponents(GameObject go)
        {
            if (go == null)
                return;
            foreach (var c in go.GetComponentsInChildren<NetworkBehaviour>(true))
                Object.DestroyImmediate(c);
            foreach (var ni in go.GetComponentsInChildren<NetworkIdentity>(true))
                Object.DestroyImmediate(ni);
        }

        private static Sprite LoadSprite(string name)
        {
            var tex = Load<Texture2D>(name);
            if (tex == null)
                return null;
            return Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );
        }

        private static Texture2D LoadTexture2D(string name)
        {
            return Load<Texture2D>(name);
        }

        public static void Unload()
        {
            _bundle?.Unload(true);
            _bundle = null;
        }
    }
}
