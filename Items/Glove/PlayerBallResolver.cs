using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Resolves a player's <see cref="GolfBall"/> from their NetworkIdentity netId.
    /// Shared by Glove / Evil Glove (and mirrors ShapeShifter's private FindBall).
    /// </summary>
    public static class PlayerBallResolver
    {
        /// <summary>
        /// Returns the OwnBall for the player whose NetworkIdentity has
        /// <paramref name="playerNetId"/>, or null if missing.
        /// </summary>
        public static GolfBall TryGetOwnBall(uint playerNetId)
        {
            if (playerNetId == 0)
                return null;

            // Prefer client map so pure clients resolve remotes; fall back to server.
            if (
                NetworkClient.active
                && NetworkClient.spawned.TryGetValue(playerNetId, out var clientId)
                && clientId != null
            )
            {
                var ball = clientId.GetComponent<PlayerInfo>()?.AsGolfer?.OwnBall
                    ?? clientId.GetComponent<PlayerInventory>()?.PlayerInfo?.AsGolfer?.OwnBall;
                if (ball != null)
                    return ball;
            }

            if (
                NetworkServer.active
                && NetworkServer.spawned.TryGetValue(playerNetId, out var serverId)
                && serverId != null
            )
            {
                return serverId.GetComponent<PlayerInfo>()?.AsGolfer?.OwnBall
                    ?? serverId.GetComponent<PlayerInventory>()?.PlayerInfo?.AsGolfer?.OwnBall;
            }

            return null;
        }

        /// <summary>
        /// NetworkIdentity netId for <paramref name="info"/>'s player object, or 0.
        /// </summary>
        public static uint GetPlayerNetId(PlayerInfo info)
        {
            if (info == null)
                return 0;
            var identity = info.GetComponent<NetworkIdentity>();
            return identity != null ? identity.netId : 0;
        }

        public static bool IsGloveLike(ItemType type) =>
            type == ItemRegistry.GloveItemType || type == ItemRegistry.EvilGloveItemType;

        public static bool IsEvilGlove(ItemType type) => type == ItemRegistry.EvilGloveItemType;
    }
}
