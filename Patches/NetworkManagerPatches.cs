using System.Collections;
using HarmonyLib;
using IssaPlugin;
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

            NetworkClient.RegisterHandler<BomberDamagedMessage>(
                BomberNetworkBridge.HandleBomberDamaged
            );
            Writer<BomberDamagedMessage>.write =
                BomberDamagedMessageSerialization.WriteBomberDamagedMessage;
            Reader<BomberDamagedMessage>.read =
                BomberDamagedMessageSerialization.ReadBomberDamagedMessage;

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

            NetworkClient.RegisterHandler<AC130DamagedMessage>(
                AC130MessageHandlers.HandleAC130Damaged
            );
            Writer<AC130DamagedMessage>.write =
                AC130DamagedMessageSerialization.WriteAC130DamagedMessage;
            Reader<AC130DamagedMessage>.read =
                AC130DamagedMessageSerialization.ReadAC130DamagedMessage;

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
                    ?.ClientBeginAC130(msg.GunshipNetId, msg.OrbitCenter);
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

            // ── Nuke Messages ─────────────────────────────────────────────────

            // Client → Server
            Writer<NukeFireMessage>.write = NukeFireMessageSerialization.WriteNukeFireMessage;
            Reader<NukeFireMessage>.read = NukeFireMessageSerialization.ReadNukeFireMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<NukeFireMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<NukeNetworkBridge>()?.ServerHandleFire();
                    }
                );

            // Server → All Clients
            Writer<NukeExplosionMessage>.write =
                NukeExplosionMessageSerialization.WriteNukeExplosionMessage;
            Reader<NukeExplosionMessage>.read =
                NukeExplosionMessageSerialization.ReadNukeExplosionMessage;
            NetworkClient.RegisterHandler<NukeExplosionMessage>(
                NukeMessageHandlers.HandleNukeExplosion
            );

            // ── BlackHoleGrenade Messages ─────────────────────────────────────

            // Client → Server
            Writer<BlackHoleGrenadeThrowMessage>.write =
                BlackHoleGrenadeThrowMessageSerialization.WriteBlackHoleGrenadeThrowMessage;
            Reader<BlackHoleGrenadeThrowMessage>.read =
                BlackHoleGrenadeThrowMessageSerialization.ReadBlackHoleGrenadeThrowMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<BlackHoleGrenadeThrowMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<BlackHoleGrenadeNetworkBridge>()
                            ?.ServerHandleThrow(msg.ThrowOrigin, msg.ThrowVelocity);
                    }
                );

            // Server → All Clients
            Writer<BlackHoleGrenadeLandedMessage>.write =
                BlackHoleGrenadeLandedMessageSerialization.WriteBlackHoleGrenadeLandedMessage;
            Reader<BlackHoleGrenadeLandedMessage>.read =
                BlackHoleGrenadeLandedMessageSerialization.ReadBlackHoleGrenadeLandedMessage;
            NetworkClient.RegisterHandler<BlackHoleGrenadeLandedMessage>(
                BlackHoleGrenadeMessageHandlers.HandleBlackHoleGrenadeLanded
            );

            Writer<BlackHoleGrenadeSpitMessage>.write =
                BlackHoleGrenadeSpitMessageSerialization.WriteBlackHoleGrenadeSpitMessage;
            Reader<BlackHoleGrenadeSpitMessage>.read =
                BlackHoleGrenadeSpitMessageSerialization.ReadBlackHoleGrenadeSpitMessage;
            NetworkClient.RegisterHandler<BlackHoleGrenadeSpitMessage>(
                BlackHoleGrenadeMessageHandlers.HandleBlackHoleGrenadeSpit
            );

            // ── PlaceableWall Messages ────────────────────────────────────────

            // Client → Server
            Writer<PlaceWallMessage>.write = PlaceWallMessageSerialization.WritePlaceWallMessage;
            Reader<PlaceWallMessage>.read = PlaceWallMessageSerialization.ReadPlaceWallMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<PlaceWallMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<PlaceableWallNetworkBridge>()
                            ?.ServerHandlePlacement(
                                msg.RayOrigin,
                                msg.RayDirection,
                                msg.ExtraYawDegrees
                            );
                    }
                );

            // Server → All Clients
            Writer<WallDestroyedMessage>.write =
                WallDestroyedMessageSerialization.WriteWallDestroyedMessage;
            Reader<WallDestroyedMessage>.read =
                WallDestroyedMessageSerialization.ReadWallDestroyedMessage;
            NetworkClient.RegisterHandler<WallDestroyedMessage>(
                PlaceableWallMessageHandlers.HandleWallDestroyed
            );

            // ── Spawn-weight sync (server → all clients) ─────────────────────
            Writer<SpawnWeightsMessage>.write =
                SpawnWeightsMessageSerialization.WriteSpawnWeightsMessage;
            Reader<SpawnWeightsMessage>.read =
                SpawnWeightsMessageSerialization.ReadSpawnWeightsMessage;
            NetworkClient.RegisterHandler<SpawnWeightsMessage>(
                SpawnWeightsSyncer.HandleSpawnWeights
            );

            // ── Vote system ───────────────────────────────────────────────────
            Writer<VoteStartMessage>.write = VoteMessageSerialization.WriteVoteStartMessage;
            Reader<VoteStartMessage>.read = VoteMessageSerialization.ReadVoteStartMessage;
            Writer<VoteSubmitMessage>.write = VoteMessageSerialization.WriteVoteSubmitMessage;
            Reader<VoteSubmitMessage>.read = VoteMessageSerialization.ReadVoteSubmitMessage;
            Writer<VoteResultsMessage>.write = VoteMessageSerialization.WriteVoteResultsMessage;
            Reader<VoteResultsMessage>.read = VoteMessageSerialization.ReadVoteResultsMessage;

            // Server receives votes from every client (including the host's local client)
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<VoteSubmitMessage>(
                    (conn, msg) => VoteManager.Instance?.HandleVoteSubmit(conn, msg)
                );

            // All clients receive vote lifecycle messages from the server
            NetworkClient.RegisterHandler<VoteStartMessage>(VoteManager.HandleVoteStart);
            NetworkClient.RegisterHandler<VoteResultsMessage>(VoteManager.HandleVoteResults);

            // ══════════════════════════════════════════════════════════════════════════
            //  BEAR MESSAGES
            // ══════════════════════════════════════════════════════════════════════════

            // ── Client → Server ───────────────────────────────────────────────────────

            // Player activates Bear item
            Writer<BearSummonMessage>.write = BearSummonMessageSerialization.WriteBearSummonMessage;
            Reader<BearSummonMessage>.read = BearSummonMessageSerialization.ReadBearSummonMessage;
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<BearSummonMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<BearNetworkBridge>()?.ServerSummonBears();
                    }
                );
            }

            // Player is locked onto a bear and wants the next rocket to home toward it
            Writer<BearPrepareHomingMessage>.write =
                BearPrepareHomingMessageSerialization.WriteBearPrepareHomingMessage;
            Reader<BearPrepareHomingMessage>.read =
                BearPrepareHomingMessageSerialization.ReadBearPrepareHomingMessage;
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<BearPrepareHomingMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<BearNetworkBridge>()?.ServerPrepareBearRocket();
                    }
                );
            }

            // Local client detected a bear in the swing hitbox during the hit window
            Writer<BearSwingHitMessage>.write =
                BearSwingHitMessageSerialization.WriteBearSwingHitMessage;
            Reader<BearSwingHitMessage>.read =
                BearSwingHitMessageSerialization.ReadBearSwingHitMessage;
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<BearSwingHitMessage>(
                    (conn, msg) =>
                    {
                        var playerInfo = conn.identity?.GetComponent<PlayerInfo>();
                        if (playerInfo == null)
                            return;

                        if (
                            !NetworkServer.spawned.TryGetValue(msg.BearNetId, out var bearNi)
                            || bearNi == null
                        )
                            return;

                        var receiver = bearNi.GetComponent<BearHitReceiver>();
                        if (receiver == null)
                            return;

                        // Generous range check — the bear may have moved a little
                        // between the client's detection and the server receiving the message.
                        float dist = Vector3.Distance(
                            playerInfo.transform.position,
                            bearNi.transform.position
                        );
                        if (dist > Configuration.BearMeleeHitRange.Value * 4f)
                            return;

                        // Record this (player, bear) pair so OnFinishedSwinging doesn't
                        // double-hit the same bear for the same swing.
                        if (
                            !GolfClubBearHitPatch._swingHitPairs.Add(
                                (playerInfo, receiver.gameObject)
                            )
                        )
                            return; // duplicate message for the same bear this swing

                        bool isBat =
                            playerInfo.Inventory?.GetEffectivelyEquippedItem(true)
                            == ItemRegistry.BaseballBatItemType;

                        float damage = isBat
                            ? Configuration.BearDamageBaseballBat.Value
                            : Configuration.BearDamageGolfClub.Value;

                        float knockbackForce = isBat
                            ? Configuration.BearBatKnockbackForce.Value
                            : Configuration.BearMeleeKnockbackForce.Value;

                        Vector3 knockDir = (
                            bearNi.transform.position - playerInfo.transform.position
                        ).normalized;
                        knockDir = (knockDir + Vector3.up * 0.5f).normalized;

                        BearExplosionAttackerContext.CurrentAttacker = playerInfo;
                        receiver.DealDamage(damage);
                        BearExplosionAttackerContext.CurrentAttacker = null;
                        receiver.Behaviour?.ApplyMeleeKnockback(knockDir, knockbackForce);

                        NetworkServer.SendToAll(
                            new BearHitVfxMessage
                            {
                                HitPoint = bearNi.transform.position + Vector3.up * 1f,
                                AttackerOrigin = playerInfo.transform.position,
                            }
                        );

                        IssaPluginPlugin.Log.LogInfo(
                            $"[Bear] Hit by swing (client-reported) "
                                + $"from {playerInfo.PlayerId.PlayerName} for {damage} damage."
                        );
                    }
                );
            }

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
            Writer<BearHitVfxMessage>.write = BearHitVfxMessageSerialization.WriteBearHitVfxMessage;
            Reader<BearHitVfxMessage>.read = BearHitVfxMessageSerialization.ReadBearHitVfxMessage;
            NetworkClient.RegisterHandler<BearHitVfxMessage>(msg =>
                BloodSplatterHelper.SpawnBloodSplatter(msg.HitPoint, msg.AttackerOrigin)
            );

            // Bear HP changed — update world-space health bar on all clients
            Writer<BearHPUpdateMessage>.write =
                BearHPUpdateMessageSerialization.WriteBearHPUpdateMessage;
            Reader<BearHPUpdateMessage>.read =
                BearHPUpdateMessageSerialization.ReadBearHPUpdateMessage;
            NetworkClient.RegisterHandler<BearHPUpdateMessage>(
                IssaPlugin.Overlays.BearHealthBarOverlay.HandleBearHPUpdate
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

            Writer<HarrierRequestMessage>.write =
                HarrierRequestMessageSerialization.WriteHarrierRequestMessage;
            Reader<HarrierRequestMessage>.read =
                HarrierRequestMessageSerialization.ReadHarrierRequestMessage;
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<HarrierRequestMessage>(
                    (conn, msg) =>
                    {
                        conn.identity?.GetComponent<HarrierNetworkBridge>()?.ServerHandleRequest();
                    }
                );
            }

            Writer<HarrierPrepareHomingMessage>.write =
                HarrierPrepareHomingMessageSerialization.WriteHarrierPrepareHomingMessage;
            Reader<HarrierPrepareHomingMessage>.read =
                HarrierPrepareHomingMessageSerialization.ReadHarrierPrepareHomingMessage;
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<HarrierPrepareHomingMessage>(
                    (conn, msg) =>
                    {
                        var bridge = conn.identity?.GetComponent<HarrierNetworkBridge>();
                        if (bridge != null)
                        {
                            bridge.PendingHarrierHoming = true;
                        }
                    }
                );
            }

            Writer<HarrierBeginClientMessage>.write =
                HarrierBeginClientMessageSerialization.WriteHarrierBeginClientMessage;
            Reader<HarrierBeginClientMessage>.read =
                HarrierBeginClientMessageSerialization.ReadHarrierBeginClientMessage;
            NetworkClient.RegisterHandler<HarrierBeginClientMessage>(msg =>
            {
                HarrierNetworkBridge.ClientHandleBegin(msg);
            });

            Writer<HarrierEndClientMessage>.write =
                HarrierEndClientMessageSerialization.WriteHarrierEndClientMessage;
            Reader<HarrierEndClientMessage>.read =
                HarrierEndClientMessageSerialization.ReadHarrierEndClientMessage;
            NetworkClient.RegisterHandler<HarrierEndClientMessage>(msg =>
            {
                HarrierNetworkBridge.ClientHandleEnd(msg);
            });

            Writer<HarrierShotDownMessage>.write =
                HarrierShotDownMessageSerialization.WriteHarrierShotDownMessage;
            Reader<HarrierShotDownMessage>.read =
                HarrierShotDownMessageSerialization.ReadHarrierShotDownMessage;
            NetworkClient.RegisterHandler<HarrierShotDownMessage>(
                HarrierNetworkBridge.ClientHandleShotDown
            );

            Writer<HarrierDamagedMessage>.write =
                HarrierDamagedMessageSerialization.WriteHarrierDamagedMessage;
            Reader<HarrierDamagedMessage>.read =
                HarrierDamagedMessageSerialization.ReadHarrierDamagedMessage;
            NetworkClient.RegisterHandler<HarrierDamagedMessage>(
                HarrierNetworkBridge.ClientHandleDamaged
            );

            // ── Hit notifications (Harrier / Bear → owning client only) ──────────
            Writer<HitNotificationMessage>.write =
                HitNotificationMessageSerialization.WriteHitNotificationMessage;
            Reader<HitNotificationMessage>.read =
                HitNotificationMessageSerialization.ReadHitNotificationMessage;
            NetworkClient.RegisterHandler<HitNotificationMessage>(msg =>
                IssaPlugin.Overlays.HitNotificationOverlay.Instance?.AddNotification(msg.Message)
            );

            // ---- ItemWarning Messages ----
            Writer<ItemWarningMessage>.write =
                ItemWarningMessageSerialization.WriteItemWarningMessage;
            Reader<ItemWarningMessage>.read =
                ItemWarningMessageSerialization.ReadItemWarningMessage;
            NetworkClient.RegisterHandler<ItemWarningMessage>(
                IssaPlugin.Overlays.ItemWarningOverlay.HandleWarningMessage
            );

            // ── Position Swap Messages ──────────────────────────────────────────

            // Client → Server
            Writer<PositionSwapRequestMessage>.write =
                PositionSwapRequestMessageSerialization.WritePositionSwapRequestMessage;
            Reader<PositionSwapRequestMessage>.read =
                PositionSwapRequestMessageSerialization.ReadPositionSwapRequestMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<PositionSwapRequestMessage>(
                    (conn, msg) =>
                        conn
                            .identity?.GetComponent<PositionSwapNetworkBridge>()
                            ?.ServerHandleRequest(msg.TargetNetId)
                );

            // Server → All clients: warning orbs
            Writer<PositionSwapWarningMessage>.write =
                PositionSwapWarningMessageSerialization.WritePositionSwapWarningMessage;
            Reader<PositionSwapWarningMessage>.read =
                PositionSwapWarningMessageSerialization.ReadPositionSwapWarningMessage;
            NetworkClient.RegisterHandler<PositionSwapWarningMessage>(
                PositionSwapNetworkBridge.HandleWarning
            );

            // Server → Specific client: teleport your own character
            Writer<PositionSwapTeleportMessage>.write =
                PositionSwapTeleportMessageSerialization.WritePositionSwapTeleportMessage;
            Reader<PositionSwapTeleportMessage>.read =
                PositionSwapTeleportMessageSerialization.ReadPositionSwapTeleportMessage;
            NetworkClient.RegisterHandler<PositionSwapTeleportMessage>(
                PositionSwapNetworkBridge.HandleTeleport
            );

            // Server → All clients: swap executed
            Writer<PositionSwapExecuteMessage>.write =
                PositionSwapExecuteMessageSerialization.WritePositionSwapExecuteMessage;
            Reader<PositionSwapExecuteMessage>.read =
                PositionSwapExecuteMessageSerialization.ReadPositionSwapExecuteMessage;
            NetworkClient.RegisterHandler<PositionSwapExecuteMessage>(
                PositionSwapNetworkBridge.HandleExecute
            );

            // Server → All clients: swap cancelled (UI logic filtered client-side)
            Writer<PositionSwapCancelledMessage>.write =
                PositionSwapCancelledMessageSerialization.WritePositionSwapCancelledMessage;
            Reader<PositionSwapCancelledMessage>.read =
                PositionSwapCancelledMessageSerialization.ReadPositionSwapCancelledMessage;
            NetworkClient.RegisterHandler<PositionSwapCancelledMessage>(
                PositionSwapNetworkBridge.HandleCancelled
            );

            // ── Hotkey item-giving (Client → Server) ─────────────────────────────
            Writer<GiveItemRequestMessage>.write =
                GiveItemRequestMessageSerialization.WriteGiveItemRequestMessage;
            Reader<GiveItemRequestMessage>.read =
                GiveItemRequestMessageSerialization.ReadGiveItemRequestMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<GiveItemRequestMessage>(
                    (conn, msg) =>
                    {
                        if (!Configuration.AllowHotkeyItemGiving.Value)
                        {
                            IssaPluginPlugin.Log.LogInfo(
                                "[GiveItem] Rejected hotkey request: AllowHotkeyItemGiving is disabled."
                            );
                            return;
                        }

                        var inventory = conn.identity?.GetComponent<PlayerInventory>();
                        if (inventory == null)
                            return;

                        var def = ItemRegistry.GetDefinition(msg.ItemType);
                        int uses = msg.Uses > 0 ? msg.Uses : (def?.MaxUses ?? 1);
                        bool added = ItemRegistry.DirectAddCustomItem(
                            inventory,
                            msg.ItemType,
                            uses
                        );
                        if (!added)
                            IssaPluginPlugin.Log.LogWarning(
                                "[GiveItem] Failed to add item (inventory full?)."
                            );
                    }
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
            RegisterPrefab(AssetLoader.NukeBombPrefab);
            RegisterPrefab(AssetLoader.BlackHoleGrenadePrefab);
            RegisterPrefab(AssetLoader.WallPrefab);
            RegisterPrefab(AssetLoader.HarrierPrefab);
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
                mayday.OrbitCenter =
                    gunship.GetComponent<AC130FlyBehaviour>()?.orbitCenter ?? Vector3.zero;
            }
        }

        internal static void HandleAC130Damaged(AC130DamagedMessage msg)
        {
            if (AssetLoader.MaydaySmokeTrailPrefab == null)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.GunshipNetId, out var ni) || ni == null)
                return;

            IssaPluginPlugin.Log.LogInfo("[AC130] Spawning damage smoke trail.");
            var smoke = Object.Instantiate(
                AssetLoader.MaydaySmokeTrailPrefab,
                ni.transform.position,
                Quaternion.identity
            );
            smoke.transform.SetParent(ni.transform, worldPositionStays: true);
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

    /// Nuke NetworkMessage handlers — kept in a separate (non-patch) class so
    /// the Harmony analyser does not misidentify the 'msg' parameters as patch parameters.
    static class NukeMessageHandlers
    {
        internal static void HandleNukeExplosion(NukeExplosionMessage msg)
        {
            // Explosion VFX — reuse the nuke fire prefab already in the bundle.
            if (AssetLoader.NukeExplosionVfxPrefab != null)
            {
                var vfx = Object.Instantiate(
                    AssetLoader.NukeExplosionVfxPrefab,
                    msg.Position,
                    Quaternion.identity
                );
                vfx.transform.localScale = Vector3.one * msg.NukeExplosionVfxScale;
                Object.Destroy(vfx, Configuration.NukeExplosionVfxDuration.Value);
            }
            else
            {
                // Fallback to the pooled rocket explosion if the prefab isn't loaded.
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    msg.Position,
                    Quaternion.identity,
                    Vector3.one * msg.NukeExplosionVfxScale
                );
            }

            // Impact sound.
            if (AssetLoader.NukeExplosionClip != null)
            {
                var go = new GameObject("Nuke_Sound");
                var src = go.AddComponent<AudioSource>();
                src.clip = AssetLoader.NukeExplosionClip;
                src.spatialBlend = 0f;
                src.volume = 1f;
                src.Play();
                Object.Destroy(go, AssetLoader.NukeExplosionClip.length + 0.1f);
            }

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.Position
            );

            // Sky blast — apply the impulse and register the knockout on the local
            // player.  Each client runs this for themselves so TryKnockOut can call
            // CmdInformKnockedOut back to the server (a [Command] must originate from
            // the owning client).
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            if (Configuration.NukeExcludeThrower.Value && localInfo == msg.ThrowerInfo)
                return;

            var movement = localInfo.Movement;
            var rb = localInfo.GetComponentInParent<Rigidbody>();
            if (movement == null || rb == null)
                return;

            Vector3 toPlayer = movement.transform.position - msg.Position;
            Vector3 horizontal = new Vector3(toPlayer.x, 0f, toPlayer.z).normalized;
            Vector3 blastDir = Vector3
                .Lerp(horizontal, Vector3.up, msg.SkyBlastVerticalBias)
                .normalized;
            Vector3 velocityChange = blastDir * msg.SkyBlastForce;

            rb.AddForce(velocityChange, ForceMode.VelocityChange);

            bool _;
            movement.TryKnockOut(
                msg.ThrowerInfo,
                KnockoutType.Rocket,
                false, // isLegSweep
                movement.transform.InverseTransformPoint(msg.Position), // localOrigin
                toPlayer.magnitude, // distance
                velocityChange, // used for unground check
                false, // ignores electromagnetic shield
                msg.ItemUseId,
                false, // fromSpecialState
                true, // canFallbackToUnground
                out _
            );

            // Give players a speed boost.
            var playerMovement = GameManager.LocalPlayerMovement;
            if (playerMovement != null)
            {
                movement.StartCoroutine(ApplyCoffeeMovementSpeed(movement));
            }
        }

        internal static IEnumerator ApplyCoffeeMovementSpeed(PlayerMovement movement)
        {
            yield return new WaitForSeconds(3);
            movement.InformDrankCoffee();
            movement.InformDrankCoffee();
            movement.InformDrankCoffee();
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

    /// PlaceableWall NetworkMessage handlers — kept in a separate (non-patch) class so
    /// the Harmony analyser does not misidentify the 'msg' parameters as patch parameters.
    static class PlaceableWallMessageHandlers
    {
        internal static void HandleWallDestroyed(WallDestroyedMessage msg)
        {
            PlaceableWallNetworkBridge.HandleWallDestroyed(msg);
        }
    }

    /// BlackHoleGrenade NetworkMessage handlers — kept in a separate (non-patch) class so
    /// the Harmony analyser does not misidentify the 'msg' parameters as patch parameters.
    static class BlackHoleGrenadeMessageHandlers
    {
        internal static void HandleBlackHoleGrenadeLanded(BlackHoleGrenadeLandedMessage msg)
        {
            BlackHoleGrenadeNetworkBridge.HandleBlackHoleGrenadeLanded(msg);
        }

        internal static void HandleBlackHoleGrenadeSpit(BlackHoleGrenadeSpitMessage msg)
        {
            BlackHoleGrenadeNetworkBridge.HandleBlackHoleGrenadeSpit(msg);
        }
    }
}
