using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // Server → All Clients: broadcast world position so each client plays the explosion VFX.
    public struct ExplosiveGolfBallsExplodeMessage : NetworkMessage
    {
        public Vector3 WorldPosition;
    }

    public static class ExplosiveGolfBallsExplodeMessageSerialization
    {
        public static void WriteExplosiveGolfBallsExplodeMessage(
            NetworkWriter writer,
            ExplosiveGolfBallsExplodeMessage msg
        )
        {
            writer.WriteVector3(msg.WorldPosition);
        }

        public static ExplosiveGolfBallsExplodeMessage ReadExplosiveGolfBallsExplodeMessage(
            NetworkReader reader
        )
        {
            return new ExplosiveGolfBallsExplodeMessage { WorldPosition = reader.ReadVector3() };
        }
    }
}
