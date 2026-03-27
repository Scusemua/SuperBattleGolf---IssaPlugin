using IssaPlugin.Network;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Static handlers for AC-130 NetworkMessages that execute on clients.
    /// Registered in NetworkManagerPatches. Extracted from AC130NetworkBridge
    /// to reduce its line count.
    /// </summary>
    internal static class AC130ClientHandlers
    {
        internal static void HandleAC130Sound(AC130SoundMessage msg)
        {
            var clip = AssetLoader.AC130AboveClip;
            if (clip == null)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Audio clip not loaded.");
                return;
            }

            var go = new GameObject("AC130_Sound");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        internal static void HandleAC130MaydayVfx(AC130MaydayVfxMessage msg)
        {
            // Skip for the owning client — TargetBeginMayday handles the cockpit path.
            // All other clients get the external smoke/fire mayday behaviour here.
            var localBridge = NetworkClient.localPlayer?.GetComponent<AC130NetworkBridge>();
            if (localBridge != null && localBridge.LocalSessionActive)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.GunshipNetId, out var ni) || ni == null)
                return;

            var gunship = ni.gameObject;
            if (gunship.GetComponent<AC130MaydayBehaviour>() == null)
            {
                var mayday = gunship.AddComponent<AC130MaydayBehaviour>();
                mayday.IsLocalPlayer = false;
                mayday.OrbitCenter =
                    gunship.GetComponent<AC130FlyBehaviour>()?.orbitCenter ?? Vector3.zero;
            }
        }

        internal static void HandleAC130Damaged(AC130DamagedMessage msg)
        {
            if (AssetLoader.MaydaySmokeTrailPrefab == null)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.GunshipNetId, out var ni) || ni == null)
                return;

            IssaPluginPlugin.Log.LogInfo("[AC130] Spawning damage smoke trail.");
            var smoke = Object.Instantiate(
                AssetLoader.MaydaySmokeTrailPrefab,
                ni.transform.position,
                Quaternion.identity
            );
            smoke.transform.SetParent(ni.transform, worldPositionStays: true);
        }

        internal static void HandleAC130MaydayImpact(AC130MaydayImpactMessage msg)
        {
            float duration = Configuration.AC130MaydayExplosionDuration.Value;

            if (AssetLoader.MaydayExplosionVfxPrefab != null)
            {
                var vfxGo = Object.Instantiate(
                    AssetLoader.MaydayExplosionVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Object.Destroy(vfxGo, duration);
            }
            else
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    msg.ImpactPos,
                    Quaternion.identity,
                    Vector3.one * Configuration.AC130MaydayExplosionScale.Value
                );
            }

            if (AssetLoader.ImpactVfxPrefab != null)
            {
                var debrisGo = Object.Instantiate(
                    AssetLoader.ImpactVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Object.Destroy(debrisGo, duration);
            }

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.ImpactPos
            );
        }
    }
}
