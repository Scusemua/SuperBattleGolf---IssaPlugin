using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public abstract class NetworkBridgeBase : NetworkBehaviour, IHoleCleanable
    {
        private PlayerInventory _cachedInventory;
        private bool _inventoryLookupDone;

        /// <summary>
        /// The PlayerInventory on this bridge's player object, resolved once and
        /// reused thereafter.
        ///
        /// Bridges are added to the player object at startup and live as long as it
        /// does, so the component reference is stable. Several bridges poll inventory
        /// state every frame, and on the host every bridge runs for every player —
        /// calling GetComponent there turns a fixed cost into one that scales with
        /// player count times bridge count.
        ///
        /// Returns null if the object genuinely has no PlayerInventory; the lookup is
        /// only attempted once either way.
        /// </summary>
        protected PlayerInventory CachedInventory
        {
            get
            {
                if (!_inventoryLookupDone)
                {
                    _cachedInventory = GetComponent<PlayerInventory>();
                    _inventoryLookupDone = true;
                }
                return _cachedInventory;
            }
        }

        public abstract void ServerHoleCleanup();

        public abstract void ClientHoleCleanup();
    }
}
