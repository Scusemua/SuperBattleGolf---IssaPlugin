using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class Firearm
    {
        // Prevents a second coroutine starting if OnUse is called while already firing
        // (e.g. if the game re-calls TryUseItem while held).
        private bool _isFiring;

        private ItemType _itemType;

        private FirearmNetworkBridge _bridge;

        private Func<PlayerInventory, bool> _continueFiringCondition;

        private Action<PlayerInventory> _playSoundFunc;

        private Action<PlayerInventory> _shootFunc;

        private Func<float> _getFireRate;

        public Firearm(
            ItemType itemType,
            FirearmNetworkBridge bridge,
            Func<PlayerInventory, bool> continueFiringCondition,
            Action<PlayerInventory> playSoundFunc,
            Action<PlayerInventory> shootFunc,
            Func<float> getFireRate
        )
        {
            _itemType = itemType;
            _playSoundFunc = playSoundFunc;
            _bridge = bridge;

            _continueFiringCondition =
                continueFiringCondition
                ?? throw new ArgumentException(
                    "'Func<PlayerInventory, bool> continueFiringCondition' must be non-null"
                );

            _shootFunc =
                shootFunc
                ?? throw new ArgumentException(
                    "'Action<PlayerInventory> shootFunc' must be non-null"
                );

            _getFireRate =
                getFireRate
                ?? throw new ArgumentException("'Func<float> getFireRate' must be non-null");
        }

        public IEnumerator FireLoop(PlayerInventory inventory)
        {
            // Guard: TryUseItem can be retried by the game's input buffer while the
            // Swing action buffer is still active. Exit immediately if the button is
            // not actually held — mirrors the same guard in JetpackItem.
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                yield break;

            if (_isFiring)
                yield break;

            _isFiring = true;
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            try
            {
                do
                {
                    DoShoot(inventory);

                    if (inventory.GetEffectivelyEquippedItem(true) != _itemType)
                        break;

                    PlaySound(inventory);

                    yield return new WaitForSeconds(_getFireRate());
                } while (ShouldContinueFiring(inventory));
            }
            finally
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);

                if (_bridge)
                    _bridge?.ClientNotifyFireStop();

                _isFiring = false;
            }
        }

        private void DoShoot(PlayerInventory inventory)
        {
            int slot = inventory.EquippedItemIndex;
            _shootFunc(inventory);
            ItemHelper.DecrementAndRemove(inventory, slot);
        }

        private void PlaySound(PlayerInventory inventory)
        {
            if (_playSoundFunc != null)
                _playSoundFunc(inventory);
        }

        private bool ShouldContinueFiring(PlayerInventory inventory)
        {
            return Mouse.current != null
                && Mouse.current.leftButton.isPressed
                && _continueFiringCondition(inventory);
        }
    }
}
