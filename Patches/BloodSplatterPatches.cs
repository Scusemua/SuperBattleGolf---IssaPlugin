using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Spawns a directional blood splatter on all clients whenever any gun hits a player.
    /// Hooks the two private VfxManager.Play*HitInternal methods — these already run on
    /// every client (local shooter + all remote clients via the RPC chain), so no extra
    /// networking is needed.
    ///
    /// Skipped when:
    ///   • the bullet hit an electromagnet shield (hitElectromagnetShield == true)
    ///   • the target is not a player (hitHittable.AsEntity.IsPlayer == false)
    ///   • the blood splatter prefab wasn't loaded from the asset bundle
    [HarmonyPatch(typeof(VfxManager), "PlayElephantGunHitInternal")]
    static class ElephantGunBloodSplatterPatch
    {
        static void Postfix(
            PlayerInventory shootingPlayer,
            ref VfxManager.GunShotHitVfxData hitData
        )
        {
            if (hitData.hitElectromagnetShield)
                return;

            if (hitData.hitHittable == null || !hitData.hitHittable.AsEntity.IsPlayer)
                return;

            Vector3 barrelEnd = shootingPlayer.GetElephantGunBarrelEndPosition();
            BloodSplatterHelper.SpawnBloodSplatter(hitData.GetHitPoint(), barrelEnd);
        }
    }

    [HarmonyPatch(typeof(VfxManager), "PlayDuelingPistolHitInternal")]
    static class DuelingPistolBloodSplatterPatch
    {
        static void Postfix(
            PlayerInventory shootingPlayer,
            ref VfxManager.GunShotHitVfxData hitData
        )
        {
            if (hitData.hitElectromagnetShield)
                return;

            if (hitData.hitHittable == null || !hitData.hitHittable.AsEntity.IsPlayer)
                return;

            Vector3 barrelEnd = shootingPlayer.GetDuelingPistolBarrelEndPosition();
            BloodSplatterHelper.SpawnBloodSplatter(hitData.GetHitPoint(), barrelEnd);
        }
    }

    static class BloodSplatterHelper
    {
        // Auto-destroy delay (seconds) — long enough for the particle system to finish.
        private const float Lifetime = 3f;

        internal static void SpawnBloodSplatter(Vector3 hitPoint, Vector3 barrelEnd)
        {
            var prefab = AssetLoader.BloodSplatterPrefab;
            if (prefab == null)
                return;

            // Orient so the prefab's forward axis points in the shot direction.
            Vector3 shotDir = (hitPoint - barrelEnd).normalized;
            if (shotDir == Vector3.zero)
                shotDir = Vector3.forward;

            var go = Object.Instantiate(prefab, hitPoint, Quaternion.LookRotation(shotDir));
            Object.Destroy(go, Lifetime);
        }
    }
}
