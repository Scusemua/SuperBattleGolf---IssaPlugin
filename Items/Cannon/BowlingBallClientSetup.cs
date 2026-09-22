using Mirror;
using UnityEngine;

namespace IssaPlugin.Items.Cannon
{
    /// <summary>
    /// Keeps remote bowling balls visual-only. The server owns collision detection
    /// and sends one explicit result to the authority that owns the impacted body.
    /// Leaving interpolated client copies solid can apply an unsynchronized local
    /// physics shove before the authoritative impact message arrives.
    /// </summary>
    public class BowlingBallClientSetup : MonoBehaviour
    {
        private void Awake()
        {
            // A listen host is also a client, but its instance is the authoritative
            // server Rigidbody and must retain full collision simulation.
            if (NetworkServer.active)
                return;

            var rigidbody = GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }

            foreach (var collider in GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }
    }
}
