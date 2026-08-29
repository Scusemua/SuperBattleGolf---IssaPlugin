using System;
using System.Collections.Generic;

namespace IssaPlugin.Integrations.SpawnerUI
{
    /// <summary>
    /// Builds the list of players items can be given to.
    ///
    /// This deliberately uses only base-game APIs (<c>GameManager.LocalPlayerInventory</c>
    /// and <c>GameManager.RemotePlayers</c>) rather than the roster the ItemSpawner mod
    /// maintains via its own PlayerInventory.Awake patch. That keeps the spawner window
    /// working whether or not ItemSpawner is installed, and avoids reflecting into
    /// another mod's private state.
    /// </summary>
    internal static class SpawnerPlayerRoster
    {
        internal class Player
        {
            public PlayerInventory Inventory;
            public string Name;
            public bool IsLocal;
        }

        /// <summary>
        /// Returns the local player first, then remote players by name. Rebuilt when the
        /// window opens and when the selection is used, since players can join or leave.
        /// </summary>
        public static List<Player> Build()
        {
            var players = new List<Player>();

            PlayerInventory local = null;
            try
            {
                local = GameManager.LocalPlayerInventory;
            }
            catch (Exception ex)
            {
                IssaPluginPlugin.Log.LogWarning($"[Spawner] Could not read local player: {ex.Message}");
            }

            if (local != null)
            {
                players.Add(new Player
                {
                    Inventory = local,
                    Name = ResolveName(local) + " (You)",
                    IsLocal = true,
                });
            }

            List<PlayerInfo> remotes = null;
            try
            {
                remotes = GameManager.RemotePlayers;
            }
            catch (Exception ex)
            {
                IssaPluginPlugin.Log.LogWarning($"[Spawner] Could not read remote players: {ex.Message}");
            }

            if (remotes != null)
            {
                foreach (var info in remotes)
                {
                    if (info == null) continue;

                    PlayerInventory inventory = null;
                    try
                    {
                        inventory = info.Inventory;
                    }
                    catch
                    {
                        continue;
                    }

                    // Skip the local player if it also appears in the remote list.
                    if (inventory == null || inventory == local) continue;

                    players.Add(new Player
                    {
                        Inventory = inventory,
                        Name = ResolveName(inventory),
                        IsLocal = false,
                    });
                }
            }

            return players;
        }

        private static string ResolveName(PlayerInventory inventory)
        {
            try
            {
                var id = inventory?.PlayerInfo?.PlayerId;
                if (id != null && !string.IsNullOrEmpty(id.PlayerNameNoRichText))
                    return id.PlayerNameNoRichText;
            }
            catch
            {
                // Names come from networked state that can be briefly unavailable.
            }

            return "Unknown Player";
        }
    }
}
