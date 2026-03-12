using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class BatItem
    {
        public static readonly ItemType BatItemType = (ItemType)100;

        public static void GiveBatToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                BatItemType,
                Configuration.BaseballBatUses.Value,
                "Bat"
            );
        }
    }
}
