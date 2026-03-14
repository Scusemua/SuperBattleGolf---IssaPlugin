using Mirror;
using UnityEngine;
using UnityEngine.Localization;

namespace IssaPlugin.Items
{
    /// Networked ground pickup for custom items.
    ///
    /// The root GameObject carries NetworkIdentity, NetworkTransform, Rigidbody,
    /// SphereCollider, Entity, and this component.  A visual child is instantiated
    /// on every client in OnStartClient() based on the synced ItemType.
    ///
    /// PlayerInteractableTargeter discovers this automatically via the generic
    /// GetComponentsInParent<IInteractable>() search — no extra patching needed.
    public class DroppedCustomItem : NetworkBehaviour, IInteractable
    {
        [SyncVar]
        public ItemType ItemType;

        [SyncVar]
        public int RemainingUses;

        public Entity AsEntity { get; private set; }
        public bool IsInteractionEnabled => true;

        public LocalizedString InteractString =>
            LocalizationManager.GetLocalizedString(StringTable.Data, "ITEM_" + ItemType.ToString());

        private void Awake()
        {
            AsEntity = GetComponent<Entity>();
        }

        /// Spawn the correct visual model as an inactive child, then activate it.
        /// SyncVars are already populated when OnStartClient fires.
        public override void OnStartClient()
        {
            // Mirror doesn't sync GameObject.layer, so the client instance starts on
            // the prefab's default layer.  Set it to ItemsLayer so the
            // PotentiallyInteractableMask physics query can find the trigger collider.
            gameObject.layer = GameManager.LayerSettings.ItemsLayer;

            var prefab = GetModelPrefab();
            if (prefab == null)
                return;

            var model = Instantiate(prefab);
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            model.SetActive(true);
        }

        public void LocalPlayerInteract()
        {
            CmdPickUp(GameManager.LocalPlayerInventory);
        }

        [Command(requiresAuthority = false)]
        private void CmdPickUp(PlayerInventory player, NetworkConnectionToClient sender = null)
        {
            if (!player.HasSpaceForItem(out _))
                return;

            if (!player.ServerTryAddItem(ItemType, RemainingUses))
                return;

            NetworkServer.Destroy(gameObject);
        }

        private GameObject GetModelPrefab()
        {
            if (ItemType == BatItem.BatItemType)
                return AssetLoader.BatModelPrefab;
            if (ItemType == StealthBomberItem.BomberItemType)
                return AssetLoader.BomberTabletPrefab;
            if (ItemType == PredatorMissileItem.MissileItemType)
                return AssetLoader.MissileTabletPrefab;
            if (ItemType == AC130Item.AC130ItemType)
                return AssetLoader.Ac130TabletPrefab;
            if (ItemType == FreezeItem.FreezeItemType)
                return AssetLoader.FreezeModelPrefab;
            if (ItemType == LowGravityItem.LowGravityItemType)
                return AssetLoader.LowGravityModelPrefab;
            if (ItemType == SniperRifleItem.SniperRifleItemType)
                return AssetLoader.SniperRiflePrefab;
            return null;
        }
    }
}
