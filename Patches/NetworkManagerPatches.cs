using System.Data.SqlTypes;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Registers all custom networked prefabs and NetworkMessage handlers with Mirror
    /// immediately after the client transport connects.
    ///
    /// Hook point: BNetworkManager.OnStartClient() — fires on every client
    /// (including the listen-server host) before any server spawn messages arrive.
    ///
    /// WHY NetworkMessage INSTEAD OF [ClientRpc]:
    /// Mirror's [ClientRpc] attribute only works after the IL weaver has rewritten the
    /// method body to call SendRpcInternal and registered the dispatch delegate in a
    /// static constructor via RemoteProcedureCalls.RegisterRpc.  BepInEx plugin DLLs
    /// are NOT processed by Mirror's IL weaver, so [ClientRpc] decorated methods just
    /// execute locally — remote clients never receive anything.
    /// NetworkServer.SendToAll<T> / NetworkClient.RegisterHandler<T> bypass that
    /// pipeline entirely and work without IL weaving.
    [HarmonyPatch]
    static class NetworkManagerRegisterPrefabsPatch
    {
        private static bool _registered;

        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BNetworkManager), "OnStartClient");

        static void Postfix()
        {
            if (_registered)
                return;

            IssaPluginPlugin.Log.LogInfo(
                "[NetworkManager] Registering custom prefabs and message handlers."
            );

            // ── Prefab registration ──────────────────────────────────────────
            RegisterPrefabs();

            // ── NetworkMessage handlers ──────────────────────────────────────
            RegisterNetworkMessages();

            _registered = true;
            IssaPluginPlugin.Log.LogInfo(
                "[NetworkManager] Custom prefabs and message handlers registered."
            );
        }

        private static void RegisterNetworkMessages()
        {
            // -------------------------------
            // ---- FreezeEffect Messages ----
            NetworkClient.RegisterHandler<FreezeBeginMessage>(
                FreezeNetworkBridge.HandleFreezeBegin
            );
            Writer<FreezeBeginMessage>.write =
                FreezeBeginMessageSerialization.WriteFreezeBeginMessage;
            Reader<FreezeBeginMessage>.read =
                FreezeBeginMessageSerialization.ReadFreezeBeginMessage;

            NetworkClient.RegisterHandler<FreezeEndMessage>(FreezeNetworkBridge.HandleFreezeEnd);
            Writer<FreezeEndMessage>.write = FreezeEndMessageSerialization.WriteFreezeEndMessage;
            Reader<FreezeEndMessage>.read = FreezeEndMessageSerialization.ReadFreezeEndMessage;

            // -----------------------------
            // ---- LowGravity Messages ----
            NetworkClient.RegisterHandler<LowGravityBeginMessage>(
                LowGravityNetworkBridge.HandleLowGravityBegin
            );
            Writer<LowGravityBeginMessage>.write =
                LowGravityBeginMessageSerialization.WriteLowGravityBeginMessage;
            Reader<LowGravityBeginMessage>.read =
                LowGravityBeginMessageSerialization.ReadLowGravityBeginMessage;

            NetworkClient.RegisterHandler<LowGravityEndMessage>(
                LowGravityNetworkBridge.HandleLowGravityEnd
            );
            Writer<LowGravityEndMessage>.write =
                LowGravityEndMessageSerialization.WriteLowGravityEndMessage;
            Reader<LowGravityEndMessage>.read =
                LowGravityEndMessageSerialization.ReadLowGravityEndMessage;

            // --------------------------------
            // ---- StealthBomber Messages ----
            NetworkClient.RegisterHandler<BomberVisualSpawnMessage>(
                BomberNetworkBridge.HandleBomberVisualSpawn
            );
            Writer<BomberVisualSpawnMessage>.write =
                BomberVisualSpawnMessageSerialization.WriteBomberVisualSpawnMessage;
            Reader<BomberVisualSpawnMessage>.read =
                BomberVisualSpawnMessageSerialization.ReadBomberVisualSpawnMessage;

            NetworkClient.RegisterHandler<BomberShotDownMessage>(
                BomberNetworkBridge.HandleBomberShotDown
            );
            Writer<BomberShotDownMessage>.write =
                BomberShotDownMessageSerialization.WriteBomberShotDownMessage;
            Reader<BomberShotDownMessage>.read =
                BomberShotDownMessageSerialization.ReadBomberShotDownMessage;

            // ------------------------
            // ---- AC130 Messages ----
            NetworkClient.RegisterHandler<AC130SoundMessage>(AC130MessageHandlers.HandleAC130Sound);
            Writer<AC130SoundMessage>.write = AC130SoundMessageSerialization.WriteAC130SoundMessage;
            Reader<AC130SoundMessage>.read = AC130SoundMessageSerialization.ReadAC130SoundMessage;

            NetworkClient.RegisterHandler<AC130MaydayVfxMessage>(
                AC130MessageHandlers.HandleAC130MaydayVfx
            );
            Writer<AC130MaydayVfxMessage>.write =
                AC130MaydayVfxMessageSerialization.WriteAC130MaydayVfxMessage;
            Reader<AC130MaydayVfxMessage>.read =
                AC130MaydayVfxMessageSerialization.ReadAC130MaydayVfxMessage;

            NetworkClient.RegisterHandler<AC130MaydayImpactMessage>(
                AC130MessageHandlers.HandleAC130MaydayImpact
            );
            Writer<AC130MaydayImpactMessage>.write =
                AC130MaydayImpactMessageSerialization.WriteAC130MaydayImpactMessage;
            Reader<AC130MaydayImpactMessage>.read =
                AC130MaydayImpactMessageSerialization.ReadAC130MaydayImpactMessage;

            // --------------------------------
            // ---- DroppedItem Messages ----
            Writer<DroppedItemPickupMessage>.write =
                DroppedItemPickupMessageSerialization.WriteDroppedItemPickupMessage;
            Reader<DroppedItemPickupMessage>.read =
                DroppedItemPickupMessageSerialization.ReadDroppedItemPickupMessage;

            // Client → Server: register on the server only.
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DroppedItemPickupMessage>(
                    DroppedItemMessageHandlers.HandleDroppedItemPickup
                );

            // ── New Command replacements (client→server) ─────────────────────

            Writer<FreezeActivateMessage>.write =
                FreezeActivateMessageSerialization.WriteFreezeActivateMessage;
            Reader<FreezeActivateMessage>.read =
                FreezeActivateMessageSerialization.ReadFreezeActivateMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<FreezeActivateMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<FreezeNetworkBridge>()?.ServerActivateFreeze();
                    }
                );

            Writer<LowGravityActivateMessage>.write =
                LowGravityActivateMessageSerialization.WriteLowGravityActivateMessage;
            Reader<LowGravityActivateMessage>.read =
                LowGravityActivateMessageSerialization.ReadLowGravityActivateMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<LowGravityActivateMessage>(
                    (conn, msg) =>
                    {
                        var bridge = conn.identity?.GetComponent<LowGravityNetworkBridge>();
                        IssaPluginPlugin.Log.LogInfo(
                            $"[LowGravity] Server received LowGravityActivateMessage. identity={(conn.identity != null ? "OK" : "NULL")}, bridge={(bridge != null ? "OK" : "NULL")}"
                        );
                        bridge?.ServerActivateLowGravity();
                    }
                );

            Writer<BomberRunMessage>.write = BomberRunMessageSerialization.WriteBomberRunMessage;
            Reader<BomberRunMessage>.read = BomberRunMessageSerialization.ReadBomberRunMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<BomberRunMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<BomberNetworkBridge>()
                            ?.ServerRequestBombingRun(
                                msg.Center,
                                msg.Forward,
                                msg.Length,
                                msg.EquippedIndex
                            );
                    }
                );

            Writer<BomberPrepareHomingMessage>.write =
                BomberPrepareHomingMessageSerialization.WriteBomberPrepareHomingMessage;
            Reader<BomberPrepareHomingMessage>.read =
                BomberPrepareHomingMessageSerialization.ReadBomberPrepareHomingMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<BomberPrepareHomingMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<BomberNetworkBridge>()
                            ?.ServerPrepareBomberRocket();
                    }
                );

            Writer<DonutPrepareHomingMessage>.write =
                DonutMessageSerialization.WriteDonutPrepareHomingMessage;
            Reader<DonutPrepareHomingMessage>.read =
                DonutMessageSerialization.ReadDonutPrepareHomingMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DonutPrepareHomingMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<DonutNetworkBridge>()
                            ?.ServerPrepareDonutRocket();
                    }
                );

            Writer<MissileRequestMessage>.write =
                MissileRequestMessageSerialization.WriteMissileRequestMessage;
            Reader<MissileRequestMessage>.read =
                MissileRequestMessageSerialization.ReadMissileRequestMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<MissileRequestMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<MissileNetworkBridge>()?.ServerRequestMissile();
                    }
                );

            Writer<MissileSetVelocityMessage>.write =
                MissileSetVelocityMessageSerialization.WriteMissileSetVelocityMessage;
            Reader<MissileSetVelocityMessage>.read =
                MissileSetVelocityMessageSerialization.ReadMissileSetVelocityMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<MissileSetVelocityMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<MissileNetworkBridge>()
                            ?.ServerSetMissileVelocity(msg.Velocity);
                    }
                );

            Writer<MissileDetonateMessage>.write =
                MissileDetonateMessageSerialization.WriteMissileDetonateMessage;
            Reader<MissileDetonateMessage>.read =
                MissileDetonateMessageSerialization.ReadMissileDetonateMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<MissileDetonateMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<MissileNetworkBridge>()
                            ?.ServerDetonateMissile();
                    }
                );

            Writer<AC130StartMessage>.write = AC130StartMessageSerialization.WriteAC130StartMessage;
            Reader<AC130StartMessage>.read = AC130StartMessageSerialization.ReadAC130StartMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130StartMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<AC130NetworkBridge>()?.ServerStartAC130();
                    }
                );

            Writer<AC130EndMessage>.write = AC130EndMessageSerialization.WriteAC130EndMessage;
            Reader<AC130EndMessage>.read = AC130EndMessageSerialization.ReadAC130EndMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130EndMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<AC130NetworkBridge>()?.ServerEndAC130();
                    }
                );

            Writer<AC130FireMessage>.write = AC130FireMessageSerialization.WriteAC130FireMessage;
            Reader<AC130FireMessage>.read = AC130FireMessageSerialization.ReadAC130FireMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130FireMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<AC130NetworkBridge>()
                            ?.ServerFireAC130(msg.AimDirection);
                    }
                );

            Writer<AC130TriggerMaydayMessage>.write =
                AC130TriggerMaydayMessageSerialization.WriteAC130TriggerMaydayMessage;
            Reader<AC130TriggerMaydayMessage>.read =
                AC130TriggerMaydayMessageSerialization.ReadAC130TriggerMaydayMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130TriggerMaydayMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<AC130NetworkBridge>()?.ServerTriggerMayday();
                    }
                );

            Writer<AC130PrepareHomingMessage>.write =
                AC130PrepareHomingMessageSerialization.WriteAC130PrepareHomingMessage;
            Reader<AC130PrepareHomingMessage>.read =
                AC130PrepareHomingMessageSerialization.ReadAC130PrepareHomingMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130PrepareHomingMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<AC130NetworkBridge>()
                            ?.ServerPrepareGunshipRocket();
                    }
                );

            Writer<AC130MaydayInputMessage>.write =
                AC130MaydayInputMessageSerialization.WriteAC130MaydayInputMessage;
            Reader<AC130MaydayInputMessage>.read =
                AC130MaydayInputMessageSerialization.ReadAC130MaydayInputMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130MaydayInputMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<AC130NetworkBridge>()
                            ?.ServerSetMaydayInput(msg.DiveInfluence, msg.RollInfluence);
                    }
                );

            Writer<AC130FlightInputMessage>.write =
                AC130FlightInputMessageSerialization.WriteAC130FlightInputMessage;
            Reader<AC130FlightInputMessage>.read =
                AC130FlightInputMessageSerialization.ReadAC130FlightInputMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130FlightInputMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<AC130NetworkBridge>()
                            ?.ServerSetFlightInput(msg.AltitudeOffset, msg.Boosting);
                    }
                );

            // ── New TargetRpc replacements (server→client) ───────────────────

            Writer<MissileBeginSteeringMessage>.write =
                MissileBeginSteeringMessageSerialization.WriteMissileBeginSteeringMessage;
            Reader<MissileBeginSteeringMessage>.read =
                MissileBeginSteeringMessageSerialization.ReadMissileBeginSteeringMessage;
            NetworkClient.RegisterHandler<MissileBeginSteeringMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<MissileNetworkBridge>()
                    ?.ClientBeginSteering(msg.RocketNetId);
            });

            Writer<MissileEndSteeringMessage>.write =
                MissileEndSteeringMessageSerialization.WriteMissileEndSteeringMessage;
            Reader<MissileEndSteeringMessage>.read =
                MissileEndSteeringMessageSerialization.ReadMissileEndSteeringMessage;
            NetworkClient.RegisterHandler<MissileEndSteeringMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<MissileNetworkBridge>()
                    ?.ClientEndSteering();
            });

            Writer<AC130BeginClientMessage>.write =
                AC130BeginClientMessageSerialization.WriteAC130BeginClientMessage;
            Reader<AC130BeginClientMessage>.read =
                AC130BeginClientMessageSerialization.ReadAC130BeginClientMessage;
            NetworkClient.RegisterHandler<AC130BeginClientMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<AC130NetworkBridge>()
                    ?.ClientBeginAC130(msg.GunshipNetId, msg.MapCentre);
            });

            Writer<AC130EndClientMessage>.write =
                AC130EndClientMessageSerialization.WriteAC130EndClientMessage;
            Reader<AC130EndClientMessage>.read =
                AC130EndClientMessageSerialization.ReadAC130EndClientMessage;
            NetworkClient.RegisterHandler<AC130EndClientMessage>(msg =>
            {
                NetworkClient.localPlayer?.GetComponent<AC130NetworkBridge>()?.ClientEndAC130();
            });

            Writer<AC130BeginMaydayClientMessage>.write =
                AC130BeginMaydayClientMessageSerialization.WriteAC130BeginMaydayClientMessage;
            Reader<AC130BeginMaydayClientMessage>.read =
                AC130BeginMaydayClientMessageSerialization.ReadAC130BeginMaydayClientMessage;
            NetworkClient.RegisterHandler<AC130BeginMaydayClientMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<AC130NetworkBridge>()
                    ?.ClientBeginMayday(msg.GunshipNetId);
            });

            Writer<AC130EndMaydayClientMessage>.write =
                AC130EndMaydayClientMessageSerialization.WriteAC130EndMaydayClientMessage;
            Reader<AC130EndMaydayClientMessage>.read =
                AC130EndMaydayClientMessageSerialization.ReadAC130EndMaydayClientMessage;
            NetworkClient.RegisterHandler<AC130EndMaydayClientMessage>(msg =>
            {
                NetworkClient.localPlayer?.GetComponent<AC130NetworkBridge>()?.ClientEndMayday();
            });

            Writer<AC130BusyMessage>.write = AC130BusyMessageSerialization.WriteAC130BusyMessage;
            Reader<AC130BusyMessage>.read = AC130BusyMessageSerialization.ReadAC130BusyMessage;
            NetworkClient.RegisterHandler<AC130BusyMessage>(msg =>
            {
                NetworkClient.localPlayer?.GetComponent<AC130NetworkBridge>()?.ClientAC130Busy();
            });

            // ── Donut Messages ─────────────────────────────────────────────────

            // Client → Server
            Writer<DonutStartMessage>.write = DonutMessageSerialization.WriteDonutStartMessage;
            Reader<DonutStartMessage>.read = DonutMessageSerialization.ReadDonutStartMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DonutStartMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<DonutNetworkBridge>()?.ServerStartDonut();
                    }
                );

            Writer<DonutEndMessage>.write = DonutMessageSerialization.WriteDonutEndMessage;
            Reader<DonutEndMessage>.read = DonutMessageSerialization.ReadDonutEndMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DonutEndMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<DonutNetworkBridge>()
                            ?.ServerEndDonut(msg.ShouldCrash);
                    }
                );

            Writer<DonutMoveMessage>.write = DonutMessageSerialization.WriteDonutMoveMessage;
            Reader<DonutMoveMessage>.read = DonutMessageSerialization.ReadDonutMoveMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DonutMoveMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<DonutNetworkBridge>()
                            ?.ServerMoveDonut(msg.WorldMoveDir);
                    }
                );

            Writer<DonutFireLaserMessage>.write =
                DonutMessageSerialization.WriteDonutFireLaserMessage;
            Reader<DonutFireLaserMessage>.read =
                DonutMessageSerialization.ReadDonutFireLaserMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DonutFireLaserMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<DonutNetworkBridge>()?.ServerFireLaser();
                    }
                );

            // Server → Client
            Writer<DonutBeginClientMessage>.write =
                DonutMessageSerialization.WriteDonutBeginClientMessage;
            Reader<DonutBeginClientMessage>.read =
                DonutMessageSerialization.ReadDonutBeginClientMessage;
            NetworkClient.RegisterHandler<DonutBeginClientMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<DonutNetworkBridge>()
                    ?.ClientBeginDonut(msg.DonutNetId);
            });

            Writer<DonutEndClientMessage>.write =
                DonutMessageSerialization.WriteDonutEndClientMessage;
            Reader<DonutEndClientMessage>.read =
                DonutMessageSerialization.ReadDonutEndClientMessage;
            NetworkClient.RegisterHandler<DonutEndClientMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<DonutNetworkBridge>()
                    ?.ClientEndDonut(false);
            });

            Writer<DonutShotDownMessage>.write =
                DonutMessageSerialization.WriteDonutShotDownMessage;
            Reader<DonutShotDownMessage>.read = DonutMessageSerialization.ReadDonutShotDownMessage;
            NetworkClient.RegisterHandler<DonutShotDownMessage>(msg =>
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[NetworkManager] Client received DonutShotDownMessage."
                );
                NetworkClient.localPlayer?.GetComponent<DonutNetworkBridge>()?.ClientEndDonut(true);
            });

            Writer<DonutBusyMessage>.write = DonutMessageSerialization.WriteDonutBusyMessage;
            Reader<DonutBusyMessage>.read = DonutMessageSerialization.ReadDonutBusyMessage;
            NetworkClient.RegisterHandler<DonutBusyMessage>(msg =>
            {
                NetworkClient.localPlayer?.GetComponent<DonutNetworkBridge>()?.ClientDonutBusy();
            });

            Writer<DonutLaserShootStartMessage>.write =
                DonutMessageSerialization.WriteDonutLaserShootStartMessage;
            Reader<DonutLaserShootStartMessage>.read =
                DonutMessageSerialization.ReadDonutLaserShootStartMessage;
            NetworkClient.RegisterHandler<DonutLaserShootStartMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<DonutNetworkBridge>()
                    ?.ClientDonutStartShooting(msg.StartPosition);
            });

            // ── Javelin Messages ──────────────────────────────────────────────

            // Client → Server
            Writer<JavelinFireMessage>.write =
                JavelinFireMessageSerialization.WriteJavelinFireMessage;
            Reader<JavelinFireMessage>.read =
                JavelinFireMessageSerialization.ReadJavelinFireMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<JavelinFireMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<JavelinNetworkBridge>()
                            ?.ServerHandleFire(msg.TargetPosition);
                    }
                );

            // Server → All Clients
            Writer<JavelinRocketTrailMessage>.write =
                JavelinRocketTrailMessageSerialization.WriteJavelinRocketTrailMessage;
            Reader<JavelinRocketTrailMessage>.read =
                JavelinRocketTrailMessageSerialization.ReadJavelinRocketTrailMessage;
            NetworkClient.RegisterHandler<JavelinRocketTrailMessage>(
                JavelinMessageHandlers.HandleJavelinRocketTrail
            );

            Writer<JavelinExplosionMessage>.write =
                JavelinExplosionMessageSerialization.WriteJavelinExplosionMessage;
            Reader<JavelinExplosionMessage>.read =
                JavelinExplosionMessageSerialization.ReadJavelinExplosionMessage;
            NetworkClient.RegisterHandler<JavelinExplosionMessage>(
                JavelinMessageHandlers.HandleJavelinExplosion
            );

            // Server → Client
            Writer<JavelinLaunchedMessage>.write =
                JavelinLaunchedMessageSerialization.WriteJavelinLaunchedMessage;
            Reader<JavelinLaunchedMessage>.read =
                JavelinLaunchedMessageSerialization.ReadJavelinLaunchedMessage;
            NetworkClient.RegisterHandler<JavelinLaunchedMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<JavelinNetworkBridge>()
                    ?.ClientHandleLaunched();
            });

            Writer<JavelinDetonatedMessage>.write =
                JavelinDetonatedMessageSerialization.WriteJavelinDetonatedMessage;
            Reader<JavelinDetonatedMessage>.read =
                JavelinDetonatedMessageSerialization.ReadJavelinDetonatedMessage;
            NetworkClient.RegisterHandler<JavelinDetonatedMessage>(msg =>
            {
                NetworkClient
                    .localPlayer?.GetComponent<JavelinNetworkBridge>()
                    ?.ClientHandleDetonated();
            });

            // ── StickyGrenade Messages ────────────────────────────────────────

            // Client → Server
            Writer<StickyGrenadeThrowMessage>.write =
                StickyGrenadeThrowMessageSerialization.WriteStickyGrenadeThrowMessage;
            Reader<StickyGrenadeThrowMessage>.read =
                StickyGrenadeThrowMessageSerialization.ReadStickyGrenadeThrowMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<StickyGrenadeThrowMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<StickyGrenadeNetworkBridge>()
                            ?.ServerHandleThrow(msg.ThrowOrigin, msg.ThrowVelocity);
                    }
                );

            // Server → All Clients
            Writer<StickyGrenadeStuckMessage>.write =
                StickyGrenadeStuckMessageSerialization.WriteStickyGrenadeStuckMessage;
            Reader<StickyGrenadeStuckMessage>.read =
                StickyGrenadeStuckMessageSerialization.ReadStickyGrenadeStuckMessage;
            NetworkClient.RegisterHandler<StickyGrenadeStuckMessage>(
                StickyGrenadeNetworkBridge.HandleStickyGrenadeStuck
            );

            Writer<StickyGrenadeDetonatedMessage>.write =
                StickyGrenadeDetonatedMessageSerialization.WriteStickyGrenadeDetonatedMessage;
            Reader<StickyGrenadeDetonatedMessage>.read =
                StickyGrenadeDetonatedMessageSerialization.ReadStickyGrenadeDetonatedMessage;
            NetworkClient.RegisterHandler<StickyGrenadeDetonatedMessage>(
                StickyGrenadeNetworkBridge.HandleStickyGrenadeDetonated
            );

            // ── Spawn-weight sync (server → all clients) ─────────────────────
            Writer<SpawnWeightsMessage>.write =
                SpawnWeightsMessageSerialization.WriteSpawnWeightsMessage;
            Reader<SpawnWeightsMessage>.read =
                SpawnWeightsMessageSerialization.ReadSpawnWeightsMessage;
            NetworkClient.RegisterHandler<SpawnWeightsMessage>(
                SpawnWeightsSyncer.HandleSpawnWeights
            );

            // ══════════════════════════════════════════════════════════════════════════
            //  BEAR MESSAGES
            // ══════════════════════════════════════════════════════════════════════════

            // ── Client → Server ───────────────────────────────────────────────────────

            // Player activates Bear item
            Writer<BearSummonMessage>.write = BearSummonMessageSerialization.WriteBearSummonMessage;
            Reader<BearSummonMessage>.read = BearSummonMessageSerialization.ReadBearSummonMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<BearSummonMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<BearNetworkBridge>()?.ServerSummonBears();
                    }
                );

            // Player is locked onto a bear and wants the next rocket to home toward it
            Writer<BearPrepareHomingMessage>.write =
                BearPrepareHomingMessageSerialization.WriteBearPrepareHomingMessage;
            Reader<BearPrepareHomingMessage>.read =
                BearPrepareHomingMessageSerialization.ReadBearPrepareHomingMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<BearPrepareHomingMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<BearNetworkBridge>()?.ServerPrepareBearRocket();
                    }
                );

            // ── Server → All Clients ─────────────────────────────────────────────────

            // Bear AI state changed (drives Animator on all clients)
            Writer<BearStateMessage>.write = BearStateMessageSerialization.WriteBearStateMessage;
            Reader<BearStateMessage>.read = BearStateMessageSerialization.ReadBearStateMessage;
            NetworkClient.RegisterHandler<BearStateMessage>(BearNetworkBridge.HandleBearState);

            // Bear landed a hit on a player (camera shake on all clients)
            Writer<BearAttackImpactMessage>.write =
                BearAttackImpactMessageSerialization.WriteBearAttackImpactMessage;
            Reader<BearAttackImpactMessage>.read =
                BearAttackImpactMessageSerialization.ReadBearAttackImpactMessage;
            NetworkClient.RegisterHandler<BearAttackImpactMessage>(
                BearNetworkBridge.HandleBearAttackImpact
            );

            // Entire session ended (cleanup on all clients)
            Writer<BearHuntEndedMessage>.write =
                BearHuntEndedMessageSerialization.WriteBearHuntEndedMessage;
            Reader<BearHuntEndedMessage>.read =
                BearHuntEndedMessageSerialization.ReadBearHuntEndedMessage;
            NetworkClient.RegisterHandler<BearHuntEndedMessage>(
                BearNetworkBridge.HandleBearSessionEnd
            );

            // Bear hit by gun or melee — spawn blood splatter on all clients
            Writer<BearHitVfxMessage>.write =
                BearHitVfxMessageSerialization.WriteBearHitVfxMessage;
            Reader<BearHitVfxMessage>.read =
                BearHitVfxMessageSerialization.ReadBearHitVfxMessage;
            NetworkClient.RegisterHandler<BearHitVfxMessage>(msg =>
                BloodSplatterHelper.SpawnBloodSplatter(msg.HitPoint, msg.AttackerOrigin)
            );

            // ── Server → Owning Client only (TargetRpc replacements) ─────────────────
            // These arrive via connectionToClient.Send() so only the summoning player
            // receives them. On the client side they are registered as normal
            // NetworkClient.RegisterHandler entries — Mirror routes them to the local
            // player automatically because the server addressed them to that connection.

            // Show bear HUD when session starts
            Writer<BearOverlayBeginMessage>.write =
                BearOverlayBeginMessageSerialization.WriteBearOverlayBeginMessage;
            Reader<BearOverlayBeginMessage>.read =
                BearOverlayBeginMessageSerialization.ReadBearOverlayBeginMessage;
            NetworkClient.RegisterHandler<BearOverlayBeginMessage>(
                BearNetworkBridge.HandleBearOverlayBegin
            );

            // Decrement pip count when a bear dies
            Writer<BearKilledClientMessage>.write =
                BearKilledClientMessageSerialization.WriteBearKilledClientMessage;
            Reader<BearKilledClientMessage>.read =
                BearKilledClientMessageSerialization.ReadBearKilledClientMessage;
            NetworkClient.RegisterHandler<BearKilledClientMessage>(
                BearNetworkBridge.HandleBearKilledClient
            );

            // Hide bear HUD when session ends
            Writer<BearOverlayEndMessage>.write =
                BearOverlayEndMessageSerialization.WriteBearOverlayEndMessage;
            Reader<BearOverlayEndMessage>.read =
                BearOverlayEndMessageSerialization.ReadBearOverlayEndMessage;
            NetworkClient.RegisterHandler<BearOverlayEndMessage>(
                BearNetworkBridge.HandleBearOverlayEnd
            );
        }

        public static void ResetRegistration() => _registered = false;

        private static void RegisterPrefabs()
        {
            RegisterPrefab(AssetLoader.DroppedCustomItemPrefab);
            RegisterPrefab(AssetLoader.DonutPrefab);
            RegisterPrefab(AssetLoader.DonutHandheldPrefab);
            RegisterPrefab(AssetLoader.JavelinHandheldPrefab);
            RegisterPrefab(AssetLoader.BatModelPrefab);
            RegisterPrefab(AssetLoader.BomberPrefab);
            RegisterPrefab(AssetLoader.BomberProxyPrefab);
            RegisterPrefab(AssetLoader.AC130Prefab);
            RegisterPrefab(AssetLoader.BomberTabletPrefab);
            RegisterPrefab(AssetLoader.MissileTabletPrefab);
            RegisterPrefab(AssetLoader.Ac130TabletPrefab);
            RegisterPrefab(AssetLoader.FreezeModelPrefab);
            RegisterPrefab(AssetLoader.LowGravityModelPrefab);
            RegisterPrefab(AssetLoader.SniperRiflePrefab);
            RegisterPrefab(AssetLoader.BloodSplatterPrefab);
            RegisterPrefab(AssetLoader.StickyGrenadePrefab);
            RegisterPrefab(AssetLoader.BearPrefab);
            RegisterPrefab(AssetLoader.TeddyBearPrefab);
        }

        private static void RegisterPrefab(GameObject prefab)
        {
            if (prefab == null)
                return;

            var ni = prefab.GetComponent<NetworkIdentity>();
            if (ni == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[NetworkManager] Skipping {prefab.name}: no NetworkIdentity."
                );
                return;
            }

            if (ni.assetId == 0)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[NetworkManager] Skipping {prefab.name}: assetId is 0 (not stable)."
                );
                return;
            }

            NetworkClient.RegisterPrefab(prefab);
            IssaPluginPlugin.Log.LogInfo(
                $"[NetworkManager] Registered '{prefab.name}' assetId={ni.assetId}."
            );
        }

        // Registration delegates point into AC130MessageHandlers below.
    }

    /// Resets the registration flag when the client disconnects so that all
    /// Writer/Reader delegates and NetworkServer handlers are re-registered
    /// on the next connect (fixes silent message loss on reconnect).
    [HarmonyPatch]
    static class NetworkManagerUnregisterPatch
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BNetworkManager), "OnStopClient");

        static void Postfix() => NetworkManagerRegisterPrefabsPatch.ResetRegistration();
    }

    /// AC130 NetworkMessage handlers — kept in a separate (non-patch) class so
    /// the Harmony analyser does not misidentify the 'msg' parameters as patch
    /// parameters and emit false Harmony003 warnings.
    static class AC130MessageHandlers
    {
        internal static void HandleAC130Sound(AC130SoundMessage msg)
        {
            var clip = AssetLoader.AC130AboveClip;
            if (clip == null)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Audio clip not loaded.");
                return;
            }

            var go = new GameObject("AC130_Sound");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        internal static void HandleAC130MaydayVfx(AC130MaydayVfxMessage msg)
        {
            // Skip for the owning client — TargetBeginMayday handles the cockpit path.
            // All other clients get the external smoke/fire mayday behaviour here.
            var localBridge = NetworkClient.localPlayer?.GetComponent<AC130NetworkBridge>();
            if (localBridge != null && localBridge.LocalSessionActive)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.GunshipNetId, out var ni) || ni == null)
                return;

            var gunship = ni.gameObject;
            if (gunship.GetComponent<AC130MaydayBehaviour>() == null)
            {
                var mayday = gunship.AddComponent<AC130MaydayBehaviour>();
                mayday.IsLocalPlayer = false;
                mayday.MapCentre =
                    gunship.GetComponent<AC130FlyBehaviour>()?.mapCentre ?? Vector3.zero;
            }
        }

        internal static void HandleAC130MaydayImpact(AC130MaydayImpactMessage msg)
        {
            float duration = Configuration.AC130MaydayExplosionDuration.Value;

            if (AssetLoader.MaydayExplosionVfxPrefab != null)
            {
                var vfxGo = Object.Instantiate(
                    AssetLoader.MaydayExplosionVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Object.Destroy(vfxGo, duration);
            }
            else
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    msg.ImpactPos,
                    Quaternion.identity,
                    Vector3.one * Configuration.AC130MaydayExplosionScale.Value
                );
            }

            if (AssetLoader.ImpactVfxPrefab != null)
            {
                var debrisGo = Object.Instantiate(
                    AssetLoader.ImpactVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Object.Destroy(debrisGo, duration);
            }

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.ImpactPos
            );
        }
    }

    /// Javelin NetworkMessage handlers — kept in a separate (non-patch) class so
    /// the Harmony analyser does not misidentify the 'msg' parameters as patch parameters.
    static class JavelinMessageHandlers
    {
        internal static void HandleJavelinRocketTrail(JavelinRocketTrailMessage msg)
        {
            if (AssetLoader.JavelinTrailVfxPrefab == null)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.RocketNetId, out var ni) || ni == null)
                return;

            var trail = Object.Instantiate(
                AssetLoader.JavelinTrailVfxPrefab,
                ni.transform.position,
                ni.transform.rotation
            );
            trail.transform.SetParent(ni.transform, worldPositionStays: false);
            trail.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            var detacher = ni.gameObject.AddComponent<JavelinRocketTrailDetacher>();
            detacher.TrailRoot = trail.transform;
        }

        internal static void HandleJavelinExplosion(JavelinExplosionMessage msg)
        {
            if (AssetLoader.JavelinExplosionVfxPrefab == null)
                return;

            var vfx = Object.Instantiate(
                AssetLoader.JavelinExplosionVfxPrefab,
                msg.Position,
                Quaternion.identity
            );
            Object.Destroy(vfx, Configuration.JavelinExplosionVfxDuration.Value);
        }
    }

    /// DroppedItem NetworkMessage handlers — kept in a separate (non-patch) class so
    /// the Harmony analyser does not misidentify the 'msg' parameters as patch parameters.
    static class DroppedItemMessageHandlers
    {
        internal static void HandleDroppedItemPickup(
            NetworkConnectionToClient conn,
            DroppedItemPickupMessage msg
        )
        {
            if (!NetworkServer.spawned.TryGetValue(msg.DroppedItemNetId, out var ni) || ni == null)
                return;

            var item = ni.gameObject.GetComponent<DroppedCustomItem>();
            var inventory = conn.identity?.GetComponent<PlayerInventory>();
            if (item == null || inventory == null)
                return;

            item.ServerPickup(inventory);
        }
    }
}
