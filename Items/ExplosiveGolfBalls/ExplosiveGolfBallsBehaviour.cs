using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached server-side to a golf ball the instant it is hit by a player who has
    /// Explosive Golf Balls in their inventory.  On the ball's first real collision
    /// (after a short grace period), triggers a scaled explosion and broadcasts the
    /// VFX message to all clients.
    ///
    /// Destroyed immediately after exploding or after MaxLifetime seconds (safety valve
    /// so it never leaks onto a ball that goes out of bounds or into a hole).
    /// </summary>
    public class ExplosiveGolfBallsBehaviour : MonoBehaviour
    {
        public PlayerGolfer Hitter;
        public float ExplosionScale = 1.5f;

        private const float GraceTime = 0.2f;
        private const float MaxLifetime = 15f;

        private float _elapsed;
        private bool _triggered;

        private static readonly MethodInfo RocketServerExplode = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        private void FixedUpdate()
        {
            if (_triggered)
                return;
            _elapsed += Time.fixedDeltaTime;
            if (_elapsed >= MaxLifetime)
            {
                _triggered = true;
                Destroy(this);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_triggered)
                return;
            if (_elapsed < GraceTime)
                return;
            Explode();
        }

        private void Explode()
        {
            if (_triggered)
                return;
            _triggered = true;

            Vector3 pos = transform.position;

            IssaPluginPlugin.Log.LogInfo($"[ExplosiveGolfBalls] Exploding at {pos}");

            NetworkServer.SendToAll(new ExplosiveGolfBallsExplodeMessage { WorldPosition = pos });

            var rocketPrefab = GameManager.ItemSettings.RocketPrefab;
            if (rocketPrefab != null && Hitter?.PlayerInfo != null)
            {
                var tempRocket = Object.Instantiate(rocketPrefab, pos, Quaternion.identity);
                if (tempRocket != null)
                {
                    var useId = new ItemUseId(
                        (ulong)Hitter.PlayerInfo.PlayerId.Guid,
                        ExplosiveGolfBallsItem.NextUseIndex(),
                        ItemType.RocketLauncher,
                        false
                    );
                    tempRocket.ServerInitialize(Hitter.PlayerInfo, null, useId);
                    NetworkServer.Spawn(tempRocket.gameObject);
                    ExplosionScaler.Register(tempRocket, ExplosionScale);
                    RocketServerExplode?.Invoke(tempRocket, new object[] { pos });
                }
            }

            Destroy(this);
        }
    }
}
