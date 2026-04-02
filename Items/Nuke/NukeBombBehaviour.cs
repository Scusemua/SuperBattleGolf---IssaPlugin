using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached server-side to the nuke bomb GameObject immediately after
    /// NetworkServer.Spawn.  Drives the bomb straight down at a fixed speed,
    /// then detonates on the first collision with terrain/ground.
    ///
    /// Detonation spawns a temporary game Rocket at the impact position and
    /// immediately calls ServerExplode on it (triggering the game's standard
    /// damage + ExplosionScaler bonus knockback), then broadcasts
    /// NukeExplosionMessage so every client applies the sky blast impulse and
    /// calls TryKnockOut on their own local PlayerMovement.
    /// </summary>
    public class NukeBombBehaviour : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Configuration  (set by NukeNetworkBridge before Start fires)
        // ----------------------------------------------------------------

        public PlayerInfo ThrowerInfo;
        public Rigidbody ThrowerRigidbody;
        public ItemUseId ItemUseId;
        public float DropSpeed = 80f;
        public float GroundProximityDistance = 3f;
        public float ExplosionScale = 8f;
        public float SkyBlastForce = 100f;
        public float SkyBlastRadius = 300f;
        public float SkyBlastVerticalBias = 0.6f;

        // ----------------------------------------------------------------
        //  Internal state
        // ----------------------------------------------------------------

        private Rigidbody _rb;
        private bool _detonated;

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                // Re-enable physics for the falling phase — AssetLoader.Load()
                // sets the prefab Rigidbody to kinematic/no-gravity so it stays
                // still while held; we take manual control here.
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }
        }

        private void FixedUpdate()
        {
            if (_detonated || _rb == null)
                return;

            _rb.linearVelocity = Vector3.down * DropSpeed;
            _rb.angularVelocity = Vector3.zero;

            // Raycast-based proximity check: detonates even if the bomb prefab has
            // no collider of its own.  OnCollisionEnter below acts as a complementary
            // trigger when a collider is present.
            if (
                Physics.Raycast(
                    transform.position,
                    Vector3.down,
                    GroundProximityDistance,
                    ItemHelper.GroundLayerMask
                )
            )
                Detonate();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_detonated)
                return;

            // Only detonate on terrain/ground layers, not on other objects.
            if ((ItemHelper.GroundLayerMask & (1 << collision.gameObject.layer)) != 0)
                Detonate();
        }

        // ----------------------------------------------------------------
        //  Detonation
        // ----------------------------------------------------------------

        public void Detonate()
        {
            if (_detonated)
                return;
            _detonated = true;

            Vector3 pos = transform.position;
            IssaPluginPlugin.Log.LogInfo($"[Nuke] Bomb detonating at {pos:F1}.");

            // Broadcast to every client: spawn VFX/sound, apply sky blast impulse,
            // and call TryKnockOut on their local PlayerMovement.
            NetworkServer.SendToAll(
                new NukeExplosionMessage
                {
                    Position = pos,
                    ThrowerInfo = ThrowerInfo,
                    ItemUseId = ItemUseId,
                    SkyBlastForce = SkyBlastForce,
                    SkyBlastVerticalBias = SkyBlastVerticalBias,
                    NukeExplosionVfxScale = ModConfig.Nuke.ExplosionVfxScale.Value,
                }
            );

            // Spawn a temporary game Rocket at the impact position and immediately
            // call ServerExplode.  This lets the game's own damage and
            // ExplosionScaler systems handle player knockback correctly.
            var tempRocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                pos,
                Quaternion.identity
            );

            if (tempRocket != null)
            {
                tempRocket.gameObject.AddComponent<CustomSpawnedRocket>();
                tempRocket.ServerInitialize(ThrowerInfo, null, ItemUseId);
                NetworkServer.Spawn(tempRocket.gameObject);
                ExplosionScaler.Register(tempRocket, ExplosionScale);
                ServerExplodeMethod?.Invoke(tempRocket, new object[] { pos });
            }

            IssaPluginPlugin.Log.LogInfo("[Nuke] Detonation complete.");

            NetworkServer.Destroy(gameObject);
        }
    }
}
