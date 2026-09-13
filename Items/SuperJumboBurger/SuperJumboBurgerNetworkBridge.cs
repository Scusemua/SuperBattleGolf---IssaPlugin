using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Deliberately thin. The giant form's two pieces of state — characterScale on
    /// PlayerMovement and isInJumboBurgerGiantForm on PlayerInfo — are both base-game
    /// SyncVars that Mirror already replicates to every client, so this bridge does NOT
    /// replicate size. Re-sending it would race with the SyncVar and cause visible
    /// snapping on remote clients.
    ///
    /// What it does own:
    ///   - Broadcasting the one-shot eat/grow VFX, which is not covered by any SyncVar.
    ///   - Hole cleanup, so a player left giant at the end of a hole is reset.
    /// </summary>
    public class SuperJumboBurgerNetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Client → Server
        // ================================================================

        public void ClientRequestEffects() =>
            NetworkClient.Send(new SuperJumboBurgerActivateMessage());

        // ================================================================
        //  Server handler — registered in NetworkManagerPatches
        // ================================================================

        public void ServerActivate()
        {
            if (!isServer)
                return;

            NetworkServer.SendToAll(new SuperJumboBurgerEffectsMessage { PlayerNetId = netId });
        }

        // ================================================================
        //  Client handler — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleEffects(SuperJumboBurgerEffectsMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;

            var info = identity.GetComponent<PlayerInfo>();
            if (info == null || info.ChestBone == null)
                return;

            // Reuse the base game's own burger eat + grow VFX so the super variant reads
            // as the same family of effect on every client.
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.JumboBurgerEat,
                info.ChestBone.position,
                Quaternion.identity,
                Vector3.one
            );
            // Sized from config, which ItemConfigSyncer keeps host-authoritative on every
            // client, so the effect matches the size the player will actually reach.
            float scale = ModConfig.SuperJumboBurger?.Scale.Value ?? 1f;
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.JumboBurgerGrow,
                info.ChestBone.position,
                Quaternion.identity,
                Vector3.one * scale
            );
        }

        // ================================================================
        //  Hole cleanup
        // ================================================================

        public override void ServerHoleCleanup() { }

        /// Reset the local player's size between holes. Without this a player who ate
        /// the burger just before holing out would start the next hole still giant —
        /// the base game's own timer is cancelled by the hole change, but our scale
        /// override is driven by a coroutine that must be stopped explicitly.
        public override void ClientHoleCleanup()
        {
            // Unconditional for the local player: ForceReset is idempotent, and gating
            // on LocalSessionActive would skip cleanup in exactly the cases where that
            // flag has gone stale.
            if (isLocalPlayer)
                SuperJumboBurgerBehaviour.ForceReset();
        }

        // ================================================================
        //  Lifecycle
        // ================================================================

        public override void OnStartClient() => _wasOwned = isOwned;

        /// Cached because isLocalPlayer/isOwned can already be false by the time the
        /// object is torn down, which would skip the reset on a disconnect — the exact
        /// case where a stuck static flag would leak into the next session.
        private bool _wasOwned;

        public override void OnStopClient()
        {
            if (_wasOwned || isOwned)
                SuperJumboBurgerBehaviour.ForceReset();
        }

        private void OnDestroy()
        {
            if (_wasOwned)
                SuperJumboBurgerBehaviour.ForceReset();
        }
    }
}
