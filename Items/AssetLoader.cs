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

        public static GameObject BatModelPrefab { get; private set; }
        public static GameObject BomberPrefab { get; private set; }
        public static GameObject BomberTabletPrefab { get; private set; }
        public static GameObject MissileTabletPrefab { get; private set; }

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

            BatIcon = LoadSprite("Assets/bat_icon.png");
            BomberIcon = LoadSprite("Assets/bomber_icon.png");
            MissileIcon = LoadSprite("Assets/missile_icon.png");

            BatModelPrefab = _bundle.LoadAsset<GameObject>("Assets/bat_model.prefab");
            BomberPrefab = _bundle.LoadAsset<GameObject>("Assets/bomber_model.prefab");
            BomberTabletPrefab = _bundle.LoadAsset<GameObject>(
                "Assets/stealth_bomber_tablet.prefab"
            );
            MissileTabletPrefab = _bundle.LoadAsset<GameObject>(
                "Assets/predator_missile_tablet.prefab"
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Assets] Bundle loaded. "
                    + $"Icons: bat={BatIcon != null}, bomber={BomberIcon != null}, missile={MissileIcon != null}. "
                    + $"Models: bat={BatModelPrefab != null}, bomber={BomberPrefab != null}, "
                    + $"bomberTablet={BomberTabletPrefab != null}, "
                    + $"missileTablet={MissileTabletPrefab != null}."
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
