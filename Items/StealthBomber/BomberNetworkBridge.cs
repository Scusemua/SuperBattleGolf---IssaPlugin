using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to the player object via NetworkBridgePatches.
    /// Handles bomber Command (client→server) and visual broadcast (server→all clients).
    ///
    /// [ClientRpc] is not IL-weaved in plugin DLLs, so all server→client broadcasts
    /// use NetworkServer.SendToAll<T> with NetworkMessage structs instead.
    ///
    /// _isBombing is an instance field so concurrent bombing runs from different
    /// players don't block each other, while still preventing the same player
    /// from stacking multiple runs.
    public class BomberNetworkBridge : NetworkBehaviour
    {
        /// True on the server while this player's bombing coroutine is running.
        private bool _isBombing;

        /// <summary>
        /// Set by CmdPrepareBomberRocket when the owning client has the bomber
        /// locked on. Consumed by GunshipRocketHomingPatch when the next rocket spawns.
        /// </summary>
        public bool PendingBomberHoming;

        [Command]
        public void CmdRequestBombingRun(
            Vector3 center,
            Vector3 forward,
            float length,
            int equippedIndex
        )
        {
            if (_isBombing)
            {
                IssaPluginPlugin.Log.LogWarning("[Bomber] Run already in progress for this player.");
                return;
            }

            _isBombing = true;

            StartCoroutine(
                StealthBomberItem.ServerBombingPhase(
                    GetComponent<PlayerInventory>(),
                    equippedIndex,
                    new StealthBomberItem.BombingStripInfo
                    {
                        Center = center,
                        Forward = forward,
                        Length = length,
                    },
                    this,
                    () => _isBombing = false
                )
            );
        }

        /// <summary>
        /// Called by the lock-on detection patch while the local player has the bomber
        /// proxy locked on. Sets PendingBomberHoming so the next rocket homes toward it.
        /// </summary>
        [Command]
        public void CmdPrepareBomberRocket()
        {
            PendingBomberHoming = true;
        }

        // ================================================================
        //  Server → all clients (via NetworkMessage, not [ClientRpc])
        // ================================================================

        public void SendSpawnBomberVisual(Vector3 spawnPos, Vector3 exitPos, Vector3 direction, float speed)
        {
            NetworkServer.SendToAll(new BomberVisualSpawnMessage
            {
                SpawnPos  = spawnPos,
                ExitPos   = exitPos,
                Direction = direction,
                Speed     = speed,
            });
        }

        public void SendBomberShotDown(Vector3 crashDir, float crashSpeed, Vector3 impactDir, Vector3 torqueImpulse)
        {
            NetworkServer.SendToAll(new BomberShotDownMessage
            {
                CrashDir     = crashDir,
                CrashSpeed   = crashSpeed,
                ImpactDir    = impactDir,
                TorqueImpulse = torqueImpulse,
            });
        }

        // ================================================================
        //  Message handlers — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleBomberVisualSpawn(BomberVisualSpawnMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo("[Bomber] BomberVisualSpawnMessage received on client.");
            StealthBomberItem.LocalSpawnBomberVisual(msg.SpawnPos, msg.ExitPos, msg.Direction, msg.Speed);
        }

        public static void HandleBomberShotDown(BomberShotDownMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo("[Bomber] BomberShotDownMessage received — triggering physics crash.");

            var visual = StealthBomberItem.ActiveBomberVisual;
            if (visual == null)
                return;

            var fly = visual.GetComponent<BomberFlyBehaviour>();
            float flySpeed = fly != null ? fly.speed : msg.CrashSpeed;
            if (fly != null)
                fly.enabled = false;

            foreach (var col in visual.GetComponents<Collider>())
                col.enabled = false;

            var rb = visual.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity  = true;

                rb.linearVelocity = msg.CrashDir * flySpeed;

                if (msg.ImpactDir != Vector3.zero)
                    rb.AddForce(msg.ImpactDir * Configuration.BomberCrashImpactForce.Value, ForceMode.Impulse);

                rb.AddForce(Vector3.down * Configuration.BomberCrashDownwardForce.Value);
                rb.AddTorque(msg.TorqueImpulse, ForceMode.Impulse);
            }

            var crashBehavior = visual.AddComponent<BomberCrashBehaviour>();
            crashBehavior.Rigidbody = rb;
        }
    }
}
