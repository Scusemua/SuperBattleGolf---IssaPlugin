using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using Unity.Collections;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Harmony postfix on GolfBall.UpdateFrictionMode.
    ///
    /// The game calls UpdateFrictionMode whenever the ball transitions between air
    /// and ground states, immediately setting rigidbody.angularDamping from
    /// GolfBallSettings. This undoes our angular damping override every time.
    ///
    /// This postfix re-applies the configurable damping value after the game's
    /// reset, but only when the ball has an active ShapeShifterState component.
    [HarmonyPatch]
    static class ShapeShifterPhysicsPatch
    {
        private static readonly MethodBase TargetMb;

        static ShapeShifterPhysicsPatch()
        {
            var type = AccessTools.TypeByName("GolfBall");
            if (type == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] GolfBall type not found — angular damping patch skipped."
                );
                return;
            }

            TargetMb = AccessTools.Method(type, "UpdateFrictionMode");
            if (TargetMb == null)
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] GolfBall.UpdateFrictionMode not found — angular damping patch skipped."
                );
        }

        static MethodBase TargetMethod() => TargetMb;

        static void Postfix(Component __instance)
        {
            if (__instance.GetComponent<ShapeShifterState>() == null)
                return;

            var rb = __instance.GetComponent<Rigidbody>();
            if (rb != null)
                rb.angularDamping = ModConfig.ShapeShifter.PhysicsAngularDamping.Value;
        }
    }

    /// Logs contact pairs involving the active shape collider so we can see
    /// what collider types PhysicsManager assigns and whether the zero-mass
    /// override actually fires for Ball vs LocalPlayer contacts.
    ///
    /// Remove once the knockdown bug is diagnosed.
    [HarmonyPatch]
    static class ShapeShifterContactDebugPatch
    {
        private static readonly MethodBase TargetMb;

        static ShapeShifterContactDebugPatch()
        {
            var type = AccessTools.TypeByName("PhysicsManager");
            TargetMb = type != null
                ? AccessTools.Method(type, "ModifyContactsInternal")
                : null;
            if (TargetMb == null)
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter][DEBUG] PhysicsManager.ModifyContactsInternal not found — contact debug patch skipped."
                );
        }

        static MethodBase TargetMethod() => TargetMb;

        // Runs after ModifyContactsInternal so mass overrides are already applied.
        static void Postfix(NativeArray<ModifiableContactPair> contactPairs)
        {
            // Find any ball currently shape-shifted on this client.
            // We read the registered collider ID directly from ShapeShifterState.
            // Using a static field on ShapeShifterState would be cleaner, but a
            // find is fine for debug purposes.
            int shapeId = ShapeShifterContactDebugPatch.GetActiveShapeColliderId();
            if (shapeId == 0)
                return;

            int localPlayerRbId = GetLocalPlayerRigidbodyId();

            for (int i = 0; i < contactPairs.Length; i++)
            {
                var pair = contactPairs[i];
                int cA = pair.colliderInstanceID;
                int cB = pair.otherColliderInstanceID;

                if (cA != shapeId && cB != shapeId)
                    continue;

                int rbA = pair.bodyInstanceID;
                int rbB = pair.otherBodyInstanceID;
                bool involvedLocalPlayer = (rbA == localPlayerRbId || rbB == localPlayerRbId);

                var massProps = pair.massProperties;

                IssaPluginPlugin.Log.LogInfo(
                    $"[ShapeShifter][DEBUG] Contact pair involving shape collider:" +
                    $" colliderA={cA} colliderB={cB}" +
                    $" rbA={rbA} rbB={rbB}" +
                    $" localPlayerRbId={localPlayerRbId}" +
                    $" involvedLocalPlayer={involvedLocalPlayer}" +
                    $" inverseMassScale={massProps.inverseMassScale:F4}" +
                    $" otherInverseMassScale={massProps.otherInverseMassScale:F4}" +
                    $" contacts={pair.contactCount}"
                );
            }
        }

        private static int GetActiveShapeColliderId()
        {
            // Walk all active ShapeShifterState components to find the local player's.
            var states = UnityEngine.Object.FindObjectsByType<ShapeShifterState>(
                UnityEngine.FindObjectsSortMode.None
            );
            foreach (var s in states)
            {
                // ShapeColliderId is private — expose it via a public property or
                // just use reflection here since this is debug-only code.
                var field = typeof(ShapeShifterState).GetField(
                    "_shapeColliderId",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (field == null) return 0;
                return (int)field.GetValue(s);
            }
            return 0;
        }

        private static int GetLocalPlayerRigidbodyId()
        {
            var lp = NetworkClient.localPlayer;
            if (lp == null) return 0;
            var rb = lp.GetComponent<Rigidbody>();
            return rb != null ? rb.GetInstanceID() : 0;
        }
    }

    /// Harmony postfix on Hittable.HitWithGolfSwingInternal.
    ///
    /// When a cubed ball is hit, the flat face in contact with the ground means
    /// ground friction produces zero net torque — the cube slides instead of
    /// rolling.  This postfix injects an angular impulse about the transverse axis
    /// (perpendicular to velocity, horizontal) so the cube immediately tumbles.
    [HarmonyPatch]
    static class ShapeShifterHitSpinPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        static void Postfix(Hittable __instance)
        {
            if (!NetworkServer.active)
                return;

            if (!__instance.AsEntity.IsGolfBall)
                return;

            if (__instance.GetComponent<ShapeShifterState>() == null)
                return;

            float spinFactor = ModConfig.ShapeShifter.PhysicsHitSpinFactor.Value;
            if (spinFactor <= 0f)
                return;

            var rb = __instance.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            float speed = rb.linearVelocity.magnitude;
            if (speed < 0.01f)
                return;

            // Axis perpendicular to velocity and gravity → produces forward rolling spin.
            Vector3 rollAxis = Vector3.Cross(rb.linearVelocity.normalized, Vector3.up);
            if (rollAxis.sqrMagnitude < 0.01f)
                rollAxis = Vector3.right;

            // Primary spin + 30% random chaos to prevent perfectly predictable tumbling.
            rb.angularVelocity +=
                rollAxis * (speed * spinFactor)
                + Random.insideUnitSphere * (speed * spinFactor * 0.3f);
        }
    }
}
