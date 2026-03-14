using Mirror;
using UnityEngine;
using UnityEngine.Localization;

namespace IssaPlugin.Items
{
    /// Networked ground pickup for custom items.
    ///
    /// Root carries NetworkIdentity, NetworkTransform, Rigidbody, SphereCollider (trigger),
    /// Entity, and this component.
    ///
    /// PHYSICS: The visual model is instantiated as a child on the SERVER by
    /// ServerDropCustomItemPatch. Its own Rigidbody is removed there so its colliders
    /// become part of the parent's compound Rigidbody, giving the item real terrain
    /// collision. Clients instantiate the model locally in OnStartClient with colliders
    /// disabled — they only need visuals; NetworkTransform drives their position.
    ///
    /// IL WEAVING: BepInEx plugin DLLs are not processed by Mirror's IL weaver,
    /// so [SyncVar] and [Command] decorators have no effect here. Instead:
    ///   - ItemType and RemainingUses are serialised via manual Serialize/Deserialize overrides.
    ///   - Pickup uses a NetworkMessage (DroppedItemPickupMessage) registered in NetworkManagerPatches.
    public class DroppedCustomItem : NetworkBehaviour, IInteractable
    {
        // Not [SyncVar] — synced via overridden SerializeSyncVars / DeserializeSyncVars.
        public ItemType ItemType;
        public int RemainingUses;

        public Entity AsEntity { get; private set; }
        public bool IsInteractionEnabled => true;

        // Localization key format matches RegisterCustomItemNames ("ITEM_100" etc.).
        public LocalizedString InteractString =>
            LocalizationManager.GetLocalizedString(StringTable.Data, "ITEM_" + (int)ItemType);

        private void Awake()
        {
            AsEntity = GetComponent<Entity>();
        }

        // Suppress Mirror's "NetworkBehaviour not weaved" warning — serialization is manual.
        public override bool Weaved() => true;

        /// Sends ItemType and RemainingUses to joining clients in the spawn message.
        /// Values never change after spawn so we only handle the forceAll path.
        protected override void SerializeSyncVars(NetworkWriter writer, bool forceAll)
        {
            base.SerializeSyncVars(writer, forceAll);
            if (forceAll)
            {
                writer.WriteInt((int)ItemType);
                writer.WriteInt(RemainingUses);
            }
            else
            {
                // Nothing is ever dirty after initial spawn.
                writer.WriteVarULong(0UL);
            }
        }

        protected override void DeserializeSyncVars(NetworkReader reader, bool initialState)
        {
            base.DeserializeSyncVars(reader, initialState);
            if (initialState)
            {
                ItemType = (ItemType)reader.ReadInt();
                RemainingUses = reader.ReadInt();
            }
            else
            {
                reader.ReadVarULong(); // discard the always-zero dirty bits
            }
        }

        /// On the HOST the model was already added server-side for physics; just ensure
        /// the layer is set. On PURE CLIENTS add a visual-only copy with colliders disabled
        /// and make the Rigidbody kinematic so it doesn't fight the NetworkTransform.
        public override void OnStartClient()
        {
            gameObject.layer = GameManager.LayerSettings.ItemsLayer;

            if (isServer)
                return; // model present from server-side spawn; RB stays non-kinematic

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            var prefab = GetModelPrefabForType(ItemType);
            if (prefab == null)
                return;

            var model = Instantiate(prefab);
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            // Colliders disabled — physics is server-authoritative; clients only need visuals.
            foreach (var col in model.GetComponentsInChildren<Collider>())
                col.enabled = false;

            model.SetActive(true);
        }

        /// Called by PlayerInteractableTargeter on the local client.
        /// Sends a NetworkMessage to the server instead of using [Command] (which
        /// requires IL weaving that BepInEx plugins don't get).
        public void LocalPlayerInteract()
        {
            var ni = GetComponent<NetworkIdentity>();
            NetworkClient.Send(new DroppedItemPickupMessage { DroppedItemNetId = ni.netId });
        }

        /// Called by the server handler registered in NetworkManagerPatches.
        public void ServerPickup(PlayerInventory player)
        {
            if (!player.HasSpaceForItem(out _))
                return;

            if (!player.ServerTryAddItem(ItemType, RemainingUses))
                return;

            NetworkServer.Destroy(gameObject);
        }

        public static GameObject GetModelPrefabForType(ItemType type)
        {
            if (type == BatItem.BatItemType)
                return AssetLoader.BatModelPrefab;
            if (type == StealthBomberItem.BomberItemType)
                return AssetLoader.BomberTabletPrefab;
            if (type == PredatorMissileItem.MissileItemType)
                return AssetLoader.MissileTabletPrefab;
            if (type == AC130Item.AC130ItemType)
                return AssetLoader.Ac130TabletPrefab;
            if (type == FreezeItem.FreezeItemType)
                return AssetLoader.FreezeModelPrefab;
            if (type == LowGravityItem.LowGravityItemType)
                return AssetLoader.LowGravityModelPrefab;
            if (type == SniperRifleItem.SniperRifleItemType)
                return AssetLoader.SniperRiflePrefab;
            if (type == UFOItem.UFOItemType)
                return AssetLoader.UFOModelPrefab;
            return null;
        }
    }
}
