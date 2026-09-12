using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Harmony postfix on PlayerInventory.LocalPlayerUpdateAimingReticle.
    ///
    /// The base game picks the aiming reticle with a switch over the equipped
    /// ItemType (RocketLauncher → ReticleType.RocketLauncher, ElephantGun →
    /// ElephantGun, and so on). Custom ItemType values match no case and fall
    /// through to <c>ReticleManager.SetReticle(ReticleType.None)</c>, so a custom
    /// gun shows no crosshair when the player right-clicks.
    ///
    /// The Golf Cart Launcher is aimed exactly like the base rocket launcher — the
    /// fire path reuses GetRocketLauncherBarrelFrontEndPosition / BarrelForward and
    /// the same 45-degree align-with-camera correction — so it gets the base game's
    /// own rocket launcher reticle rather than a mod-drawn one.
    ///
    /// A Postfix (not a Prefix) is used deliberately: the base method also handles
    /// the not-aiming case and the Thunderstorm aim preview cleanup, all of which
    /// should still run. This only overrides the final reticle choice.
    ///
    /// LocalPlayerUpdateAimingReticle is private, so it is located via AccessTools
    /// (same approach as FlamethrowerMovementPatch → ProcessMovementInput).
    /// </summary>
    [HarmonyPatch]
    static class GolfCartLauncherReticlePatch
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "LocalPlayerUpdateAimingReticle");

        [HarmonyPostfix]
        static void Postfix(PlayerInventory __instance)
        {
            if (!__instance.isLocalPlayer)
                return;

            // IsAimingItem is false when the aim button is released; the base method
            // has already cleared the reticle in that case and must not be overridden.
            if (!__instance.IsAimingItem)
            {
                ResetAimGuard();
                return;
            }

            // Pass ignoreEquipmentHiding: false to match the base method, so the
            // reticle disappears while equipment is force-hidden (emotes, cutscenes).
            if (
                __instance.GetEffectivelyEquippedItem(false)
                != ItemRegistry.GolfCartLauncherItemType
            )
            {
                ResetAimGuard();
                return;
            }

            ReticleManager.SetReticle(ReticleType.RocketLauncher);

            // The base game plays the aim sound from UpdateIsAimingItem, but only for a
            // hardcoded list of base ItemTypes that a custom type cannot match. Play it
            // here so aiming in sounds exactly like the real rocket launcher.
            //
            // This postfix runs on aim-state transitions rather than every frame, but
            // LocalPlayerUpdateAimingReticle is also called on equip and on first-person
            // aim changes, so the sound is gated on an actual not-aiming → aiming edge.
            if (!_wasAimingLauncher)
                __instance.PlayerInfo?.PlayerAudio?.PlayItemAimForAllClients(
                    ItemType.RocketLauncher
                );

            _wasAimingLauncher = true;
        }

        /// <summary>
        /// Tracks whether the local player was already aiming the launcher, so the aim
        /// sound plays once per right-click rather than on every reticle refresh.
        /// Static because only the local player reaches the code that sets it.
        /// </summary>
        private static bool _wasAimingLauncher;

        /// <summary>
        /// Clears the aim-sound edge guard whenever the reticle resolves to anything
        /// other than the launcher (aim released, item switched, equipment hidden).
        /// </summary>
        internal static void ResetAimGuard() => _wasAimingLauncher = false;
    }
}
