using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    /// Server side: no per-player state needed — explosion is driven entirely by
    ///              ExplosiveGolfBallsBehaviour on the ball.
    /// Client side: static handler plays the explosion VFX when the server broadcasts
    ///              ExplosiveGolfBallsExplodeMessage.
    /// </summary>
    public class ExplosiveGolfBallsNetworkBridge : NetworkBridgeBase
    {
        public static void HandleExplosion(ExplosiveGolfBallsExplodeMessage msg)
        {
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                msg.WorldPosition,
                Quaternion.identity,
                Vector3.one * ModConfig.ExplosiveGolfBalls.ExplosionScale.Value
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.WorldPosition
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[ExplosiveGolfBalls] Client: explosion VFX at {msg.WorldPosition}"
            );
        }

        public override void ServerHoleCleanup() { }

        public override void ClientHoleCleanup() { }
    }
}
