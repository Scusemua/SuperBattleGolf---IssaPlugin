using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public abstract class NetworkBridgeBase : NetworkBehaviour, IHoleCleanable
    {
        public abstract void ServerHoleCleanup();

        public abstract void ClientHoleCleanup();
    }
}
