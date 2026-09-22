using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// On pure clients the bowling ball Rigidbody stays kinematic (NetworkTransform-driven).
    /// A kinematic body can still shove a dynamic local player when the visual catches the
    /// hit, which stacks on top of the scripted VelocityChange and makes remote hits feel
    /// far stronger than the host. Ignore the local player so only the knockout message
    /// applies the shove.
    /// </summary>
    public class BowlingBallClientSetup : MonoBehaviour
    {
        private void Start()
        {
            // Listen-host / dedicated server use real physics for detection; only pure
            // clients have the kinematic replica problem.
            if (!NetworkClient.active || NetworkServer.active)
                return;

            var local = NetworkClient.localPlayer;
            if (local == null)
                return;

            var ballCols = GetComponentsInChildren<Collider>(true);
            var playerCols = local.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < ballCols.Length; i++)
            {
                var ballCol = ballCols[i];
                if (ballCol == null)
                    continue;

                for (int j = 0; j < playerCols.Length; j++)
                {
                    var playerCol = playerCols[j];
                    if (playerCol == null)
                        continue;

                    Physics.IgnoreCollision(ballCol, playerCol, true);
                }
            }
        }
    }
}
