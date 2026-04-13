using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Detects when any firearm bullet raycast hits a Hunter Drone and triggers
    /// server-side detonation.
    ///
    /// HOW IT WORKS
    /// ─────────────
    /// All firearm bullet paths in both the base game (Dueling Pistol, Elephant Gun)
    /// and custom weapons (AK47, Sniper Rifle) funnel through the private
    /// <c>PlayerInventory.TryParseFirearmRaycastResults</c>.  The method iterates
    /// <c>PlayerGolfer.raycastHitBuffer</c> and returns the closest hit that has a
    /// <c>Hittable</c> component.
    ///
    /// The hunter drone does NOT have a <c>Hittable</c> component, so the method
    /// skips it — but the raw <c>RaycastHit</c> entry is still in the buffer if the
    /// drone's collider is on a layer included in <c>GunHittablesMask</c>.
    /// (<see cref="HunterDroneNetworkBridge.SpawnDrone"/> sets the drone to
    /// <c>GameManager.LayerSettings.HittablesLayer</c> which is within that mask.)
    ///
    /// This Postfix inspects the full raw buffer after the method returns and signals
    /// the server when a drone is found.  On the host the server method is called
    /// directly; on a remote client a <see cref="HunterDroneShotMessage"/> is sent.
    ///
    /// A single Postfix covers every firearm without touching individual weapon files.
    /// Rocket-based weapons (Rocket Launcher, Javelin, etc.) are handled separately
    /// in <see cref="RocketPatches.Patch_Rocket_ServerExplode"/>.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "TryParseFirearmRaycastResults")]
    static class HunterDroneFirearmHitPatch
    {
        // Harmony injects the named parameters that match the original signature.
        // We only need the raw buffer and hit count; __instance gives us the
        // connection-to-server for remote clients.
        static void Postfix(
            PlayerInventory __instance,
            RaycastHit[] raycastResults,
            int raycastHitCount
        )
        {
            // Only run on the local client that fired the shot.
            if (!NetworkClient.active)
                return;

            for (int i = 0; i < raycastHitCount; i++)
            {
                var collider = raycastResults[i].collider;
                if (collider == null)
                    continue;

                var behaviour = collider.GetComponentInParent<HunterDroneBehaviour>();
                if (behaviour == null)
                    continue;

                var ni = behaviour.GetComponent<NetworkIdentity>();
                if (ni == null)
                    continue;

                uint droneNetId = ni.netId;

                if (NetworkServer.active)
                {
                    // Host path: call the server method directly without a round-trip.
                    behaviour.ShootDown();
                }
                else
                {
                    // Remote client path: ask the server to destroy the drone.
                    __instance.connectionToServer?.Send(
                        new HunterDroneShotMessage { DroneNetId = droneNetId }
                    );
                }

                // One hit is enough — stop after the first drone found.
                break;
            }
        }
    }
}
