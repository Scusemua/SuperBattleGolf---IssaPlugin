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
            float extra = SuperJumboBurgerBehaviour.CameraDistanceAddition(
                SuperJumboBurgerCameraSubject.GetScale(orbitCamera)
            );
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
    /// Drives the camera-distance update every frame while a super form is active.
    ///
    /// SuperJumboBurgerCameraPatch below only applies its surplus when the base game
    /// calls UpdateCameraDistanceAddition — and the base game only calls it from event
    /// handlers and from its own giant-form camera routine, which runs for
    /// JumboBurgerGrowCameraDistanceAdditionDuration and then stops. Once that short
    /// routine finished, nothing called the method again, so the extra distance
    /// silently reverted and the CameraDistancePerScale setting appeared to do nothing.
    ///
    /// It also meant the pull-back finished on the base game's schedule rather than
    /// ours, so it never lined up with a GrowDuration the player had configured.
    ///
    /// Calling the base game's own method (rather than SetDistanceAddition directly)
    /// keeps the aim and swing-charge terms in the sum, and lets the existing Postfix
    /// remain the single place the surplus is applied.
    /// </summary>
    /// <summary>
    /// Resolves the CharacterScale of whichever player the orbit camera is looking at,
    /// cached per subject transform.
    ///
    /// Shared by the distance and height patches so both read the SAME player. The
    /// camera can be following a spectated player rather than the local one, and if the
    /// two patches disagreed the camera would be raised for a giant while still sitting
    /// at the close, normal-sized distance.
    /// </summary>
    static class SuperJumboBurgerCameraSubject
    {
        private static Transform _cachedSubject;
        private static PlayerMovement _cachedMovement;

        /// True when the last lookup actually found a PlayerMovement. Lets a destroyed
        /// one force a re-resolve while a genuine "this subject has none" (a golf cart)
        /// stays cached.
        private static bool _cachedResolved;

        /// Current subject's scale, or 1 (normal) when there is no player subject.
        public static float GetScale(OrbitCameraModule orbitCamera)
        {
            if (orbitCamera == null)
                return 1f;

            var subject = orbitCamera.Subject;
            if (subject == null)
                return 1f;

            // Re-resolved when the subject changes, and when a previously found
            // PlayerMovement has been destroyed underneath us — Unity's overloaded ==
            // reports a destroyed object as null, so a stale entry would otherwise
            // survive as long as the subject reference compared equal.
            if (subject != _cachedSubject || (_cachedResolved && _cachedMovement == null))
            {
                _cachedSubject = subject;
                _cachedMovement = subject.GetComponentInParent<PlayerMovement>();
                _cachedResolved = _cachedMovement != null;
            }

            return _cachedMovement != null ? _cachedMovement.CharacterScale : 1f;
        }
    }

    [HarmonyPatch(typeof(GameplayCameraManager), "Update")]
    static class SuperJumboBurgerCameraDriverPatch
    {
        /// Bound once as an open-instance delegate rather than invoked reflectively:
        /// this runs every frame while a super form is active, and MethodInfo.Invoke
        /// allocates an args array and boxes on each call.
        private static readonly System.Action<GameplayCameraManager, OrbitCameraModule> UpdateDistance =
            BuildUpdateDistance();

        private static System.Action<GameplayCameraManager, OrbitCameraModule> BuildUpdateDistance()
        {
            var method = AccessTools.Method(
                typeof(GameplayCameraManager),
                "UpdateCameraDistanceAddition"
            );
            if (method == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[SuperJumboBurger] GameplayCameraManager.UpdateCameraDistanceAddition "
                        + "not found — the camera will not pull back for large scales."
                );
                return null;
            }

            return (System.Action<GameplayCameraManager, OrbitCameraModule>)
                System.Delegate.CreateDelegate(
                    typeof(System.Action<GameplayCameraManager, OrbitCameraModule>),
                    method
                );
        }

        /// Tracks whether the surplus was nonzero last frame, so one final update runs
        /// after it reaches zero. Without it the camera would keep the last nonzero
        /// surplus forever once the player returned to normal size.
        private static bool _wasActive;

        static void Postfix(GameplayCameraManager __instance)
        {
            if (__instance == null || UpdateDistance == null)
                return;

            // The orbit module is briefly unavailable during scene transitions and
            // camera-mode switches — which includes hole changes, exactly when the
            // surplus is dropping to zero. Bail BEFORE latching: clearing _wasActive
            // here would skip the final update forever, leaving the camera pulled back
            // at a stale distance with nothing left to reset it.
            if (!CameraModuleController.TryGetOrbitModule(out var orbitCamera))
                return;

            float scale = SuperJumboBurgerCameraSubject.GetScale(orbitCamera);
            bool active = SuperJumboBurgerBehaviour.CameraDistanceAddition(scale) > 0f;
            if (!active && !_wasActive)
                return;

            _wasActive = active;
            UpdateDistance(__instance, orbitCamera);
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

        static void Postfix(OrbitCameraModule __instance, ref Vector3 __result)
        {
            if (__instance == null || __instance.Subject == null)
                return;

            // Shares SuperJumboBurgerCameraSubject with the distance patch so both read
            // the SAME player — the camera may be following a spectated giant rather
            // than the local one, and if the two disagreed the camera would be raised
            // for a giant while still sitting at the close, normal-sized distance.
            float height = SuperJumboBurgerBehaviour.GetCameraHeightAddition(
                SuperJumboBurgerCameraSubject.GetScale(__instance)
            );
            if (height <= 0f)
                return;

            // World up, not subject up: the offset must not tilt as the player turns or
            // walks up a slope.
            __result += Vector3.up * height;
        }
    }
}
