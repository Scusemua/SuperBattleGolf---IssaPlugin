using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class BatItem
    {
        public static void GiveBatToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                ItemRegistry.BaseballBatItemType,
                (int)Configuration.BaseballBatUses.Value,
                "Bat"
            );
        }
    }
}
