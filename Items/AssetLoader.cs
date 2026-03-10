using System.IO;
using System.Reflection;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class AssetLoader
    {
        public static Sprite BatIcon { get; private set; }
        public static Sprite BomberIcon { get; private set; }
        public static Sprite MissileIcon { get; private set; }
        public static Sprite AC130Icon { get; private set; }

        public static GameObject BatModelPrefab { get; private set; }
        public static GameObject BomberPrefab { get; private set; }
        public static GameObject AC130Prefab { get; private set; }
        public static GameObject BomberTabletPrefab { get; private set; }
        public static GameObject MissileTabletPrefab { get; private set; }

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

            BatModelPrefab = _bundle.LoadAsset<GameObject>("bat_model.prefab");
            BomberPrefab = _bundle.LoadAsset<GameObject>("bomber_model.prefab");
            AC130Prefab = _bundle.LoadAsset<GameObject>("ac130_model.prefab");
            BomberTabletPrefab = _bundle.LoadAsset<GameObject>("stealth_bomber_tablet.prefab");
            MissileTabletPrefab = _bundle.LoadAsset<GameObject>("predator_missile_tablet.prefab");

            // AudioClips must be loaded by asset name without the file extension.
            // Unity compiles audio into its own internal format at bundle-build
            // time, so the original .ogg path is never valid at runtime.
            AC130AboveClip = _bundle.LoadAsset<AudioClip>("ac130_above.ogg");
            HomerunAudioClip = _bundle.LoadAsset<AudioClip>("homerun.ogg");

            // Mayday assets — these will be null until the prefabs/clips are
            // added to the asset bundle. All mayday code null-checks before use.
            MaydayAlarmClip = _bundle.LoadAsset<AudioClip>("missile_locked.ogg");
            MaydayImpactClip = _bundle.LoadAsset<AudioClip>("etfx_explosion_nuke.wav");
            MaydaySmokeTrailPrefab = _bundle.LoadAsset<GameObject>("smoke_prefab.prefab");
            MaydayExplosionVfxPrefab = _bundle.LoadAsset<GameObject>("NukeExplosionFire.prefab");

            IssaPluginPlugin.Log.LogInfo(
                $"[Assets] Bundle loaded. "
                    + $"Icons: bat={BatIcon != null}, bomber={BomberIcon != null}, "
                    + $"missile={MissileIcon != null}, ac130={AC130Icon != null}. "
                    + $"Models: bat={BatModelPrefab != null}, bomber={BomberPrefab != null}, "
                    + $"ac130={AC130Prefab != null}, "
                    + $"bomberTablet={BomberTabletPrefab != null}, "
                    + $"missileTablet={MissileTabletPrefab != null}. "
                    + $"Audio: ac130Above={AC130AboveClip != null}, homerun={HomerunAudioClip != null}."
            );
        }

        private static Sprite LoadSprite(string assetPath)
        {
            var tex = _bundle.LoadAsset<Texture2D>(assetPath);
            if (tex == null)
                return null;
            return Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );
        }

        public static void Unload()
        {
            _bundle?.Unload(true);
            _bundle = null;
        }
    }
}
