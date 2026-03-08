using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

// This class bridges the client-side overlay/input logic with the server-side session management.
namespace IssaPlugin.Items
{
    public class AC130NetworkBridge : NetworkBehaviour
    {
        public static AC130NetworkBridge Instance { get; private set; }

        private PlayerInventory _inventory;

        public static void ServerSpawn(PlayerInventory inventory)
        {
            if (!NetworkServer.active)
                return;

            var go = new GameObject("AC130NetworkBridge");
            NetworkServer.Spawn(go);

            var bridge = go.AddComponent<AC130NetworkBridge>();
            bridge._inventory = inventory;
            Instance = bridge;
        }

        public static void ServerDespawn()
        {
            if (Instance == null)
                return;
            if (NetworkServer.active)
                NetworkServer.Destroy(Instance.gameObject);
            Instance = null;
        }

        // Called by clients to request a rocket fire.
        [Command(requiresAuthority = false)]
        public void CmdRequestFire(Vector3 gunshipPos, Vector3 aimDirection)
        {
            if (!AC130Item.IsActive)
                return;

            float angularJitter = Configuration.AC130RocketAngularJitter.Value;
            Quaternion jitter = Quaternion.Euler(
                Random.Range(-angularJitter, angularJitter),
                Random.Range(-angularJitter, angularJitter),
                0f
            );

            Quaternion fireRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
            AC130Item.SpawnRocketInDirection(_inventory, gunshipPos, jitter * fireRotation);
        }

        // Expose for client routine so it can call via the static instance.
        public static void CmdRequestFire(PlayerInventory inventory, Vector3 pos, Vector3 dir)
        {
            if (Instance != null)
                Instance.CmdRequestFire(pos, dir);
        }
    }
}
