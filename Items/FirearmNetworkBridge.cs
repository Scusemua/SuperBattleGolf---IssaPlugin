using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public abstract class FirearmNetworkBridge : NetworkBridgeBase
    {
        public abstract void ClientNotifyFireStop();
    }
}
