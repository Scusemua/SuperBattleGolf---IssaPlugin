using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Pulls the orbit camera further back while the local player is in a Super Jumbo
    /// Burger giant form.
    ///
    /// The base game already pulls the camera back for its own giant form: an animated
    /// jumboBurgerGiantFormCameraDistanceAddition is summed with the aim and
    /// swing-charge additions in GameplayCameraManager.UpdateCameraDistanceAddition and
    /// pushed to OrbitCameraModule.SetDistanceAddition. That addition is a fixed
    /// constant sized for the vanilla giant scale, so at a configured scale of 10 the
    /// camera ends up staring at the player's back.
    ///
    /// Patching UpdateCameraDistanceAddition rather than calling SetDistanceAddition
    /// ourselves is deliberate: it is the single chokepoint through which every camera
    /// distance write passes, so our extra distance survives aim changes and swing
    /// charges instead of being overwritten by the next base-game write, and it rides
    /// the base game's own grow/shrink easing for free.
    /// </summary>
    [HarmonyPatch]
    static class SuperJumboBurgerCameraPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(GameplayCameraManager), "UpdateCameraDistanceAddition");

        private static readonly FieldInfo AimAdditionField = AccessTools.Field(
            typeof(GameplayCameraManager),
            "aimCameraDistanceAddition"
        );
        private static readonly FieldInfo SwingAdditionField = AccessTools.Field(
            typeof(GameplayCameraManager),
            "swingChargeCameraDistanceAddition"
        );
        private static readonly FieldInfo GiantAdditionField = AccessTools.Field(
            typeof(GameplayCameraManager),
            "jumboBurgerGiantFormCameraDistanceAddition"
        );

        /// True once the three private fields have been confirmed present. If the game
        /// renames one, the patch disables itself rather than silently feeding the
        /// camera a sum with a missing term.
        private static readonly bool FieldsResolved =
            AimAdditionField != null && SwingAdditionField != null && GiantAdditionField != null;

        private static bool _loggedMissingFields;

        static void Postfix(OrbitCameraModule orbitCamera, GameplayCameraManager __instance)
        {
            if (orbitCamera == null || __instance == null)
                return;

            if (!FieldsResolved)
            {
                if (!_loggedMissingFields)
                {
                    _loggedMissingFields = true;
                    IssaPluginPlugin.Log.LogWarning(
                        "[SuperJumboBurger] Camera distance fields not found — the camera "
                            + "will not pull back for large scales. A game update likely "
                            + "renamed them."
                    );
                }
                return;
            }

            // Derived from the player's live scale, so it eases in and out with the
            // grow/shrink animation and is exactly 0 at normal size — which is what
            // makes this patch inert outside a super form, with no state flag needed.
            float extra = SuperJumboBurgerBehaviour.CameraDistanceAddition();
            if (extra <= 0f)
                return;

            // Rebuild the base game's own sum and add our surplus, so the aim and
            // swing-charge contributions it just applied are preserved.
            float baseSum =
                (float)AimAdditionField.GetValue(__instance)
                + (float)SwingAdditionField.GetValue(__instance)
                + (float)GiantAdditionField.GetValue(__instance);

            orbitCamera.SetDistanceAddition(baseSum + extra);
        }
    }

    /// <summary>
    /// Raises the point the orbit camera looks at while a giant is oversized, so the
    /// camera sits above the player rather than level with their back.
    ///
    /// Pulling the camera back (see SuperJumboBurgerCameraPatch) fixes how much of the
    /// player fills the frame, but not the angle: the camera still tracks a point near
    /// the middle of a very tall model, so the view stays level with the player's back
    /// and the course ahead is hidden behind them. Lifting the tracked point moves the
    /// camera up and tilts it down, which is what actually restores the view.
    ///
    /// GetCurrentTargetTrackedPoint is the single method every camera position path
    /// resolves the tracked point through, so one Postfix covers them all.
    ///
    /// Deliberately NOT implemented with SetSubjectSpaceTrackingOffset. That offset is
    /// stored in a flags field the base game tests with equality rather than a bitmask
    /// (trackingOffsetMode != SubjectSpace / == CameraSpace), so setting it while the
    /// aim offset is active produces a combined value matching neither branch and
    /// silently disables the aim tracking offset. Adding world-space height to the
    /// returned point avoids that interaction entirely.
    /// </summary>
    [HarmonyPatch]
    static class SuperJumboBurgerCameraHeightPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(OrbitCameraModule), "GetCurrentTargetTrackedPoint");

        private static Transform _cachedSubject;
        private static PlayerMovement _cachedMovement;

        /// True when the last lookup actually found a PlayerMovement. Lets a destroyed
        /// one force a re-resolve while a genuine "this subject has none" stays cached.
        private static bool _cachedResolved;

        static void Postfix(OrbitCameraModule __instance, ref Vector3 __result)
        {
            if (__instance == null || __instance.Subject == null)
                return;

            // The camera can be tracking any player (spectating included), so resolve the
            // scale from the subject rather than assuming it is the local player.
            //
            // Cached per subject transform: this runs on every camera position update,
            // and the subject only changes when the camera retargets (respawn, entering
            // a cart, switching spectator target).
            // Re-resolved when the subject changes, and also when the cached
            // PlayerMovement has been destroyed underneath us — Unity's overloaded ==
            // reports a destroyed object as null, so a stale entry would otherwise
            // survive as long as the subject reference compared equal.
            // Re-resolved when the subject changes, and also when a previously found
            // PlayerMovement has been destroyed underneath us — Unity's overloaded ==
            // reports a destroyed object as null, so a stale entry would otherwise
            // survive as long as the subject reference compared equal.
            //
            // _cachedResolved distinguishes "not looked up yet" from "looked up and
            // there genuinely is none" (the subject is a golf cart), so the negative
            // result is cached too rather than re-running the search every call.
            var subject = __instance.Subject;
            if (subject != _cachedSubject || (_cachedResolved && _cachedMovement == null))
            {
                _cachedSubject = subject;
                _cachedMovement = subject.GetComponentInParent<PlayerMovement>();
                _cachedResolved = _cachedMovement != null;
            }

            var movement = _cachedMovement;
            if (movement == null)
                return;

            float height = SuperJumboBurgerBehaviour.GetCameraHeightAddition(
                movement.CharacterScale
            );
            if (height <= 0f)
                return;

            // World up, not subject up: the offset must not tilt as the player turns or
            // walks up a slope.
            __result += Vector3.up * height;
        }
    }
}
