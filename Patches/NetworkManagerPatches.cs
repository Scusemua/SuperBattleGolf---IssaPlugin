using System;
using System.Collections.Generic;
using System.Reflection;
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
        // Prefabs only need to be registered once — Mirror warns on re-registration.
        // Writers/readers/handlers must be re-registered on every OnStartClient because
        // Mirror clears them when the client shuts down, and the local (host) connection
        // bypasses serialization entirely so a null Writer<T>.write only breaks remote
        // clients, making the bug invisible on the host side.
        private static bool _prefabsRegistered;

        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BNetworkManager), "OnStartClient");

        static void Postfix()
        {
            if (!_prefabsRegistered)
            {
                IssaPluginPlugin.Log.LogInfo("[NetworkManager] Registering custom prefabs.");
                RegisterPrefabs();
                _prefabsRegistered = true;
            }

            // Always re-register message handlers, writers, and readers.
            // Mirror clears Writer<T>/Reader<T> delegates on client shutdown, so
            // re-registering here ensures they are valid for every new session.
            IssaPluginPlugin.Log.LogInfo("[NetworkManager] Registering custom message handlers.");
            RegisterNetworkMessages();
            IssaPluginPlugin.Log.LogInfo("[NetworkManager] Custom message handlers registered.");
            ValidateMessageIds();
        }

        private static T GetBridge<T>(NetworkConnectionToClient conn)
            where T : Component
        {
            var identity = conn.identity;
            if (identity == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[GetBridge<{typeof(T).Name}>] conn.identity is NULL for conn={conn}"
                );
                return null;
            }
            var bridge = identity.GetComponent<T>();
            if (bridge == null)
                IssaPluginPlugin.Log.LogWarning(
                    $"[GetBridge<{typeof(T).Name}>] No {typeof(T).Name} component on identity netId={identity.netId}"
                );
            return bridge;
        }

        // ── Message ID collision detection ────────────────────────────────────────
        // Mirror assigns each NetworkMessage type a 16-bit ID derived from its
        // full type name via GetStableHashCode16. With 65,536 possible slots and a
        // growing message count, the birthday paradox makes collisions inevitable
        // unless caught early. Run this after every RegisterNetworkMessages() call.
        //
        // HOW TO FIX a reported collision:
        //   Rename one of the two conflicting structs so its hash changes.
        //   Any rename works — there is no "good" name; just pick one that differs.

        private static void ValidateMessageIds()
        {
            // Use Mirror's own NetworkMessageId<T> to get the actual runtime ID for
            // each type — this is the same value Mirror uses for dispatch, so it
            // catches collisions regardless of how Mirror's hash function is implemented.
            var networkMessageInterface = typeof(NetworkMessage);
            var pluginAssembly = typeof(NetworkManagerRegisterPrefabsPatch).Assembly;

            // Map: id → (type, assemblyLabel). Built from all external assemblies first
            // so any collision is reported as "plugin type vs external type".
            var seenIds = new Dictionary<ushort, (Type Type, string Label)>();
            var collisions = new List<string>();

            // Scan every assembly currently loaded in the AppDomain except our own.
            // Using the AppDomain (rather than hard-coded anchors) automatically covers
            // Mirror, GameAssembly, SharedAssembly, and any other game-side DLLs without
            // needing a typeof() anchor for each one.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == pluginAssembly)
                    continue;

                string label = assembly.GetName().Name;
                foreach (var t in SafeGetTypes(assembly))
                {
                    if (!IsNetworkMessageType(t, networkMessageInterface))
                        continue;

                    ushort id = GetMessageIdForType(t);
                    if (id == 0)
                        continue; // ID lookup failed — already warned inside GetMessageIdForType
                    seenIds.TryAdd(id, (t, label));
                }
            }

            // Now scan the plugin assembly and check every ID against all external IDs.
            var pluginMessageTypes = new List<Type>();
            foreach (var t in SafeGetTypes(pluginAssembly))
            {
                if (IsNetworkMessageType(t, networkMessageInterface))
                    pluginMessageTypes.Add(t);
            }

            foreach (var msgType in pluginMessageTypes)
            {
                ushort id = GetMessageIdForType(msgType);
                if (id == 0)
                    continue; // ID lookup failed — already warned inside GetMessageIdForType

                if (seenIds.TryGetValue(id, out var existing))
                    collisions.Add(
                        $"  id={id}: [{existing.Label}] {existing.Type.FullName}  vs  [Plugin] {msgType.FullName}"
                    );
                else
                    seenIds[id] = (msgType, "Plugin");
            }

            if (collisions.Count > 0)
            {
                IssaPluginPlugin.Log.LogError(
                    "╔══════════════════════════════════════════════════════════╗"
                );
                IssaPluginPlugin.Log.LogError(
                    "║        MIRROR MESSAGE ID COLLISION DETECTED              ║"
                );
                IssaPluginPlugin.Log.LogError(
                    "╚══════════════════════════════════════════════════════════╝"
                );
                IssaPluginPlugin.Log.LogError(
                    $"  {collisions.Count} collision(s) found. Multiplayer message dispatch is broken."
                );
                IssaPluginPlugin.Log.LogError(
                    "  FIX: Rename one struct from each pair below so its hash changes."
                );
                IssaPluginPlugin.Log.LogError(
                    "  ──────────────────────────────────────────────────────────"
                );
                foreach (var c in collisions)
                    IssaPluginPlugin.Log.LogError(c);
                IssaPluginPlugin.Log.LogError(
                    "╚══════════════════════════════════════════════════════════╝"
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[MessageCollision] All {pluginMessageTypes.Count} plugin message IDs are unique "
                        + "(no collisions found across all loaded assemblies)."
                );
            }
        }

        /// Returns true if <paramref name="t"/> is a concrete struct implementing
        /// <paramref name="networkMessageInterface"/>.
        /// Returns false (never throws) — <see cref="Type.IsAssignableFrom"/> can throw
        /// <see cref="TypeLoadException"/> for partially-loaded types from assemblies that
        /// had missing dependencies.
        private static bool IsNetworkMessageType(Type t, Type networkMessageInterface)
        {
            try
            {
                return t.IsValueType && networkMessageInterface.IsAssignableFrom(t);
            }
            catch
            {
                return false;
            }
        }

        /// Returns all loadable types from <paramref name="assembly"/>.
        /// Never throws: if some types fail to load due to missing dependencies the rest
        /// are returned; if the assembly itself is unreadable an empty array is returned.
        /// Both cases are common in BepInEx environments where not all transitive
        /// dependencies are present.
        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Partial load — return whichever types did resolve.
                var loadable = new List<Type>(ex.Types.Length);
                foreach (var t in ex.Types)
                    if (t != null)
                        loadable.Add(t);
                return loadable.ToArray();
            }
            catch (Exception ex)
            {
                // SecurityException, BadImageFormatException, etc. — skip the assembly.
                IssaPluginPlugin.Log.LogWarning(
                    $"[MessageCollision] Skipping assembly '{assembly.GetName().Name}' — GetTypes() threw: {ex.Message}"
                );
                return [];
            }
        }

        // Set to true after the first time we fail to find NetworkMessageId<T>.Id via
        // reflection, so we emit at most one warning about a potential Mirror API change
        // rather than spamming one line per message type.
        private static bool _networkMessageIdApiWarned;

        /// Returns the Mirror-assigned 16-bit dispatch ID for <paramref name="msgType"/>
        /// by reflecting into <c>NetworkMessageId&lt;T&gt;.Id</c>.
        /// Returns 0 and logs a warning if the ID cannot be obtained (Mirror version mismatch).
        private static ushort GetMessageIdForType(Type msgType)
        {
            try
            {
                var generic = typeof(NetworkMessageId<>).MakeGenericType(msgType);

                // Mirror stores the ID as a public static readonly ushort field named "Id".
                var field = generic.GetField("Id", BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                    return (ushort)field.GetValue(null);

                // Some Mirror versions expose it as a property instead.
                var prop = generic.GetProperty("Id", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                    return (ushort)prop.GetValue(null);

                // Neither field nor property found — Mirror's internal API may have changed.
                if (!_networkMessageIdApiWarned)
                {
                    _networkMessageIdApiWarned = true;
                    IssaPluginPlugin.Log.LogWarning(
                        "[MessageCollision] NetworkMessageId<T> has no 'Id' field or property. "
                            + "Mirror's API may have changed — message ID collision detection is unreliable."
                    );
                }
            }
            catch (Exception ex)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[MessageCollision] Could not get ID for {msgType.FullName}: {ex.Message}"
                );
            }
            return 0;
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
            NetworkClient.RegisterHandler<AC130SoundMessage>(AC130ClientHandlers.HandleAC130Sound);
            Writer<AC130SoundMessage>.write = AC130SoundMessageSerialization.WriteAC130SoundMessage;
            Reader<AC130SoundMessage>.read = AC130SoundMessageSerialization.ReadAC130SoundMessage;

            NetworkClient.RegisterHandler<AC130MaydayVfxMessage>(
                AC130ClientHandlers.HandleAC130MaydayVfx
            );
            Writer<AC130MaydayVfxMessage>.write =
                AC130MaydayVfxMessageSerialization.WriteAC130MaydayVfxMessage;
            Reader<AC130MaydayVfxMessage>.read =
                AC130MaydayVfxMessageSerialization.ReadAC130MaydayVfxMessage;

            NetworkClient.RegisterHandler<AC130DamagedMessage>(
                AC130ClientHandlers.HandleAC130Damaged
            );
            Writer<AC130DamagedMessage>.write =
                AC130DamagedMessageSerialization.WriteAC130DamagedMessage;
            Reader<AC130DamagedMessage>.read =
                AC130DamagedMessageSerialization.ReadAC130DamagedMessage;

            NetworkClient.RegisterHandler<AC130MaydayImpactMessage>(
                AC130ClientHandlers.HandleAC130MaydayImpact
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
                    DroppedCustomItem.ServerHandlePickupMessage
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
                        GetBridge<FreezeNetworkBridge>(conn)?.ServerActivateFreeze();
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
                        var bridge = GetBridge<LowGravityNetworkBridge>(conn);
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
                        GetBridge<BomberNetworkBridge>(conn)
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
                        GetBridge<BomberNetworkBridge>(conn)?.ServerPrepareBomberRocket();
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
                        GetBridge<DonutNetworkBridge>(conn)?.ServerPrepareDonutRocket();
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
                        GetBridge<MissileNetworkBridge>(conn)?.ServerRequestMissile();
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
                        GetBridge<MissileNetworkBridge>(conn)
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
                        GetBridge<MissileNetworkBridge>(conn)?.ServerDetonateMissile();
                    }
                );

            Writer<AC130StartMessage>.write = AC130StartMessageSerialization.WriteAC130StartMessage;
            Reader<AC130StartMessage>.read = AC130StartMessageSerialization.ReadAC130StartMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130StartMessage>(
                    (conn, msg) =>
                    {
                        GetBridge<AC130NetworkBridge>(conn)?.ServerStartAC130();
                    }
                );

            Writer<AC130EndMessage>.write = AC130EndMessageSerialization.WriteAC130EndMessage;
            Reader<AC130EndMessage>.read = AC130EndMessageSerialization.ReadAC130EndMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130EndMessage>(
                    (conn, msg) =>
                    {
                        GetBridge<AC130NetworkBridge>(conn)?.ServerEndAC130();
                    }
                );

            Writer<AC130FireMessage>.write = AC130FireMessageSerialization.WriteAC130FireMessage;
            Reader<AC130FireMessage>.read = AC130FireMessageSerialization.ReadAC130FireMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<AC130FireMessage>(
                    (conn, msg) =>
                    {
                        GetBridge<AC130NetworkBridge>(conn)
                            ?.ServerFireAC130(msg.AimDirection, msg.IsHeavy);
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
                        GetBridge<AC130NetworkBridge>(conn)?.ServerTriggerMayday();
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
                        GetBridge<AC130NetworkBridge>(conn)?.ServerPrepareGunshipRocket();
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
                        GetBridge<AC130NetworkBridge>(conn)
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
                        GetBridge<AC130NetworkBridge>(conn)
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
                        GetBridge<DonutNetworkBridge>(conn)?.ServerStartDonut();
                    }
                );

            Writer<DonutEndMessage>.write = DonutMessageSerialization.WriteDonutEndMessage;
            Reader<DonutEndMessage>.read = DonutMessageSerialization.ReadDonutEndMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DonutEndMessage>(
                    (conn, msg) =>
                    {
                        GetBridge<DonutNetworkBridge>(conn)?.ServerEndDonut(msg.ShouldCrash);
                    }
                );

            Writer<DonutMoveMessage>.write = DonutMessageSerialization.WriteDonutMoveMessage;
            Reader<DonutMoveMessage>.read = DonutMessageSerialization.ReadDonutMoveMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DonutMoveMessage>(
                    (conn, msg) =>
                    {
                        GetBridge<DonutNetworkBridge>(conn)?.ServerMoveDonut(msg.WorldMoveDir);
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
                        GetBridge<DonutNetworkBridge>(conn)?.ServerFireLaser();
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

            // ── Super Donut Messages ──────────────────────────────────────────

            // Client → Server
            Writer<SuperDonutStartMessage>.write =
                SuperDonutMessageSerialization.WriteSuperDonutStartMessage;
            Reader<SuperDonutStartMessage>.read =
                SuperDonutMessageSerialization.ReadSuperDonutStartMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<SuperDonutStartMessage>(
                    (conn, msg) =>
                    {
                        GetBridge<SuperDonutNetworkBridge>(conn)?.ServerFireBarrage();
                    }
                );

            // Server → All Clients
            Writer<SuperDonutFiredMessage>.write =
                SuperDonutMessageSerialization.WriteSuperDonutFiredMessage;
            Reader<SuperDonutFiredMessage>.read =
                SuperDonutMessageSerialization.ReadSuperDonutFiredMessage;
            NetworkClient.RegisterHandler<SuperDonutFiredMessage>(msg =>
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[SuperDonut] Barrage fired — {msg.TargetCount} laser(s) incoming."
                );
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
                        GetBridge<JavelinNetworkBridge>(conn)?.ServerHandleFire(msg.TargetPosition);
                    }
                );

            // Server → All Clients
            Writer<JavelinRocketTrailMessage>.write =
                JavelinRocketTrailMessageSerialization.WriteJavelinRocketTrailMessage;
            Reader<JavelinRocketTrailMessage>.read =
                JavelinRocketTrailMessageSerialization.ReadJavelinRocketTrailMessage;
            NetworkClient.RegisterHandler<JavelinRocketTrailMessage>(
                JavelinNetworkBridge.HandleRocketTrailVfx
            );

            Writer<JavelinExplosionMessage>.write =
                JavelinExplosionMessageSerialization.WriteJavelinExplosionMessage;
            Reader<JavelinExplosionMessage>.read =
                JavelinExplosionMessageSerialization.ReadJavelinExplosionMessage;
            NetworkClient.RegisterHandler<JavelinExplosionMessage>(
                JavelinNetworkBridge.HandleExplosionVfx
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
                        GetBridge<StickyGrenadeNetworkBridge>(conn)
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
                        GetBridge<NukeNetworkBridge>(conn)?.ServerHandleFire();
                    }
                );

            // Server → All Clients
            Writer<NukeExplosionMessage>.write =
                NukeExplosionMessageSerialization.WriteNukeExplosionMessage;
            Reader<NukeExplosionMessage>.read =
                NukeExplosionMessageSerialization.ReadNukeExplosionMessage;
            NetworkClient.RegisterHandler<NukeExplosionMessage>(
                NukeNetworkBridge.HandleNukeExplosion
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
                        GetBridge<BlackHoleGrenadeNetworkBridge>(conn)
                            ?.ServerHandleThrow(msg.ThrowOrigin, msg.ThrowVelocity);
                    }
                );

            // Server → All Clients
            Writer<BlackHoleGrenadeLandedMessage>.write =
                BlackHoleGrenadeLandedMessageSerialization.WriteBlackHoleGrenadeLandedMessage;
            Reader<BlackHoleGrenadeLandedMessage>.read =
                BlackHoleGrenadeLandedMessageSerialization.ReadBlackHoleGrenadeLandedMessage;
            NetworkClient.RegisterHandler<BlackHoleGrenadeLandedMessage>(
                BlackHoleGrenadeNetworkBridge.HandleBlackHoleGrenadeLanded
            );

            Writer<BlackHoleGrenadeSpitMessage>.write =
                BlackHoleGrenadeSpitMessageSerialization.WriteBlackHoleGrenadeSpitMessage;
            Reader<BlackHoleGrenadeSpitMessage>.read =
                BlackHoleGrenadeSpitMessageSerialization.ReadBlackHoleGrenadeSpitMessage;
            NetworkClient.RegisterHandler<BlackHoleGrenadeSpitMessage>(
                BlackHoleGrenadeNetworkBridge.HandleBlackHoleGrenadeSpit
            );

            // ── PlaceableWall Messages ────────────────────────────────────────

            // Client → Server
            Writer<PlaceWallMessage>.write = PlaceWallMessageSerialization.WritePlaceWallMessage;
            Reader<PlaceWallMessage>.read = PlaceWallMessageSerialization.ReadPlaceWallMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<PlaceWallMessage>(
                    (conn, msg) =>
                    {
                        GetBridge<PlaceableWallNetworkBridge>(conn)
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
                PlaceableWallNetworkBridge.HandleWallDestroyed
            );

            // ── Spawn-weight sync (server → all clients) ─────────────────────
            Writer<SpawnWeightsMessage>.write =
                SpawnWeightsMessageSerialization.WriteSpawnWeightsMessage;
            Reader<SpawnWeightsMessage>.read =
                SpawnWeightsMessageSerialization.ReadSpawnWeightsMessage;
            NetworkClient.RegisterHandler<SpawnWeightsMessage>(
                SpawnWeightsSyncer.HandleSpawnWeights
            );

            // ── Full config sync (server → all clients) ───────────────────────
            Writer<ItemConfigSyncMessage>.write = ItemConfigSyncSerialization.Write;
            Reader<ItemConfigSyncMessage>.read = ItemConfigSyncSerialization.Read;
            NetworkClient.RegisterHandler<ItemConfigSyncMessage>(ItemConfigSyncer.HandleConfigSync);

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
            Writer<BearActivateMessage>.write =
                BearActivateMessageSerialization.WriteBearActivateMessage;
            Reader<BearActivateMessage>.read =
                BearActivateMessageSerialization.ReadBearActivateMessage;
            IssaPluginPlugin.Log.LogInfo(
                $"[Bear] Registering BearActivateMessage handler — NetworkServer.active={NetworkServer.active}"
            );
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<BearActivateMessage>(
                    (conn, msg) =>
                    {
                        IssaPluginPlugin.Log.LogInfo(
                            $"[Bear] BearActivateMessage handler invoked — conn={conn}, identity={conn?.identity?.netId}"
                        );
                        GetBridge<BearNetworkBridge>(conn)?.ServerSummonBears();
                    }
                );
                IssaPluginPlugin.Log.LogInfo(
                    "[Bear] BearActivateMessage server handler registered."
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Bear] Skipped BearActivateMessage server handler — NetworkServer not active."
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
                        GetBridge<BearNetworkBridge>(conn)?.ServerPrepareBearRocket();
                    }
                );
            }

            // Local client detected a bear in the swing hitbox during the hit window
            Writer<BearSwingHitMessage>.write =
                BearSwingHitMessageSerialization.WriteBearSwingHitMessage;
            Reader<BearSwingHitMessage>.read =
                BearSwingHitMessageSerialization.ReadBearSwingHitMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<BearSwingHitMessage>(
                    BearNetworkBridge.ServerHandleSwingHit
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
                        GetBridge<HarrierNetworkBridge>(conn)?.ServerHandleRequest();
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
                        var bridge = GetBridge<HarrierNetworkBridge>(conn);
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
            NetworkClient.RegisterHandler<HarrierBeginClientMessage>(
                HarrierNetworkBridge.ClientHandleBegin
            );

            Writer<HarrierEndClientMessage>.write =
                HarrierEndClientMessageSerialization.WriteHarrierEndClientMessage;
            Reader<HarrierEndClientMessage>.read =
                HarrierEndClientMessageSerialization.ReadHarrierEndClientMessage;
            NetworkClient.RegisterHandler<HarrierEndClientMessage>(
                HarrierNetworkBridge.ClientHandleEnd
            );

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
                        GetBridge<PositionSwapNetworkBridge>(conn)
                            ?.ServerHandleRequest(msg.TargetNetId, msg.EquippedSlotIndex)
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

            // ── Poison Jar ────────────────────────────────────────────────────────
            Writer<PoisonJarThrowMessage>.write =
                PoisonJarMessageSerialization.WritePoisonJarThrowMessage;
            Reader<PoisonJarThrowMessage>.read =
                PoisonJarMessageSerialization.ReadPoisonJarThrowMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<PoisonJarThrowMessage>(
                    (conn, msg) =>
                        GetBridge<PoisonJarNetworkBridge>(conn)
                            ?.ServerHandleThrow(msg.ThrowOrigin, msg.ThrowVelocity)
                );

            Writer<PoisonJarLandedMessage>.write =
                PoisonJarMessageSerialization.WritePoisonJarLandedMessage;
            Reader<PoisonJarLandedMessage>.read =
                PoisonJarMessageSerialization.ReadPoisonJarLandedMessage;
            NetworkClient.RegisterHandler<PoisonJarLandedMessage>(
                PoisonJarNetworkBridge.HandleLanded
            );

            // ── Drone Swarm ────────────────────────────────────────────────────────
            Writer<DroneSwarmSummonMessage>.write =
                DroneSwarmSummonMessageSerialization.WriteDroneSwarmSummonMessage;
            Reader<DroneSwarmSummonMessage>.read =
                DroneSwarmSummonMessageSerialization.ReadDroneSwarmSummonMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<DroneSwarmSummonMessage>(
                    (conn, msg) => GetBridge<DroneSwarmNetworkBridge>(conn)?.ServerSummonDrones()
                );

            Writer<DroneExplodedMessage>.write =
                DroneExplodedMessageSerialization.WriteDroneExplodedMessage;
            Reader<DroneExplodedMessage>.read =
                DroneExplodedMessageSerialization.ReadDroneExplodedMessage;
            NetworkClient.RegisterHandler<DroneExplodedMessage>(
                DroneSwarmNetworkBridge.HandleDroneExploded
            );

            Writer<DroneSwarmSessionEndedMessage>.write =
                DroneSwarmSessionEndedMessageSerialization.WriteDroneSwarmSessionEndedMessage;
            Reader<DroneSwarmSessionEndedMessage>.read =
                DroneSwarmSessionEndedMessageSerialization.ReadDroneSwarmSessionEndedMessage;
            NetworkClient.RegisterHandler<DroneSwarmSessionEndedMessage>(
                DroneSwarmNetworkBridge.HandleSessionEnded
            );

            Writer<DroneSwarmOverlayBeginMessage>.write =
                DroneSwarmOverlayBeginMessageSerialization.WriteDroneSwarmOverlayBeginMessage;
            Reader<DroneSwarmOverlayBeginMessage>.read =
                DroneSwarmOverlayBeginMessageSerialization.ReadDroneSwarmOverlayBeginMessage;
            NetworkClient.RegisterHandler<DroneSwarmOverlayBeginMessage>(
                DroneSwarmNetworkBridge.HandleOverlayBegin
            );

            Writer<DroneDiedClientMessage>.write =
                DroneDiedClientMessageSerialization.WriteDroneDiedClientMessage;
            Reader<DroneDiedClientMessage>.read =
                DroneDiedClientMessageSerialization.ReadDroneDiedClientMessage;
            NetworkClient.RegisterHandler<DroneDiedClientMessage>(
                DroneSwarmNetworkBridge.HandleDroneDied
            );

            Writer<DroneSwarmOverlayEndMessage>.write =
                DroneSwarmOverlayEndMessageSerialization.WriteDroneSwarmOverlayEndMessage;
            Reader<DroneSwarmOverlayEndMessage>.read =
                DroneSwarmOverlayEndMessageSerialization.ReadDroneSwarmOverlayEndMessage;
            NetworkClient.RegisterHandler<DroneSwarmOverlayEndMessage>(
                DroneSwarmNetworkBridge.HandleOverlayEnd
            );

            // ── Red Bull Trail ────────────────────────────────────────────────────
            Writer<RedBullActivateMessage>.write = RedBullMessageSerialization.WriteActivate;
            Reader<RedBullActivateMessage>.read = RedBullMessageSerialization.ReadActivate;
            Writer<RedBullTrailBeginMessage>.write = RedBullMessageSerialization.WriteTrailBegin;
            Reader<RedBullTrailBeginMessage>.read = RedBullMessageSerialization.ReadTrailBegin;
            Writer<RedBullTrailEndMessage>.write = RedBullMessageSerialization.WriteTrailEnd;
            Reader<RedBullTrailEndMessage>.read = RedBullMessageSerialization.ReadTrailEnd;
            NetworkClient.RegisterHandler<RedBullTrailBeginMessage>(
                RedBullNetworkBridge.HandleTrailBegin
            );
            NetworkClient.RegisterHandler<RedBullTrailEndMessage>(
                RedBullNetworkBridge.HandleTrailEnd
            );
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<RedBullActivateMessage>(
                    (conn, msg) => GetBridge<RedBullNetworkBridge>(conn)?.ServerActivate()
                );

            // ── Jetpack ───────────────────────────────────────────────────────────
            Writer<JetpackThrustStartMessage>.write = JetpackMessageSerialization.WriteThrustStart;
            Reader<JetpackThrustStartMessage>.read = JetpackMessageSerialization.ReadThrustStart;
            Writer<JetpackThrustStopMessage>.write = JetpackMessageSerialization.WriteThrustStop;
            Reader<JetpackThrustStopMessage>.read = JetpackMessageSerialization.ReadThrustStop;
            Writer<JetpackThrustBeginMessage>.write = JetpackMessageSerialization.WriteThrustBegin;
            Reader<JetpackThrustBeginMessage>.read = JetpackMessageSerialization.ReadThrustBegin;
            Writer<JetpackThrustEndMessage>.write = JetpackMessageSerialization.WriteThrustEnd;
            Reader<JetpackThrustEndMessage>.read = JetpackMessageSerialization.ReadThrustEnd;
            Writer<JetpackEquipBeginMessage>.write = JetpackMessageSerialization.WriteEquipBegin;
            Reader<JetpackEquipBeginMessage>.read = JetpackMessageSerialization.ReadEquipBegin;
            Writer<JetpackEquipEndMessage>.write = JetpackMessageSerialization.WriteEquipEnd;
            Reader<JetpackEquipEndMessage>.read = JetpackMessageSerialization.ReadEquipEnd;
            NetworkClient.RegisterHandler<JetpackThrustBeginMessage>(
                JetpackNetworkBridge.HandleThrustBegin
            );
            NetworkClient.RegisterHandler<JetpackThrustEndMessage>(
                JetpackNetworkBridge.HandleThrustEnd
            );
            NetworkClient.RegisterHandler<JetpackEquipBeginMessage>(
                JetpackNetworkBridge.HandleEquipBegin
            );
            NetworkClient.RegisterHandler<JetpackEquipEndMessage>(
                JetpackNetworkBridge.HandleEquipEnd
            );
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<JetpackThrustStartMessage>(
                    (conn, msg) => GetBridge<JetpackNetworkBridge>(conn)?.ServerHandleThrustStart()
                );
                NetworkServer.RegisterHandler<JetpackThrustStopMessage>(
                    (conn, msg) => GetBridge<JetpackNetworkBridge>(conn)?.ServerHandleThrustStop()
                );
            }

            // ── Coffee Dispenser Red Bull visual swap ─────────────────────────────
            Writer<CoffeeDispenserRedBullMessage>.write =
                RedBullMessageSerialization.WriteCoffeeDispenserRedBull;
            Reader<CoffeeDispenserRedBullMessage>.read =
                RedBullMessageSerialization.ReadCoffeeDispenserRedBull;
            NetworkClient.RegisterHandler<CoffeeDispenserRedBullMessage>(
                CoffeeDispenserClientHandlers.HandleVisualSwap
            );

            // ── Spinach ───────────────────────────────────────────────────────────
            Writer<SpinachActivateMessage>.write = SpinachMessageSerialization.WriteActivate;
            Reader<SpinachActivateMessage>.read = SpinachMessageSerialization.ReadActivate;
            Writer<SpinachTrailBeginMessage>.write = SpinachMessageSerialization.WriteTrailBegin;
            Reader<SpinachTrailBeginMessage>.read = SpinachMessageSerialization.ReadTrailBegin;
            Writer<SpinachTrailEndMessage>.write = SpinachMessageSerialization.WriteTrailEnd;
            Reader<SpinachTrailEndMessage>.read = SpinachMessageSerialization.ReadTrailEnd;
            NetworkClient.RegisterHandler<SpinachTrailBeginMessage>(
                SpinachNetworkBridge.HandleVfxBegin
            );
            NetworkClient.RegisterHandler<SpinachTrailEndMessage>(
                SpinachNetworkBridge.HandleVfxEnd
            );
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<SpinachActivateMessage>(
                    (conn, msg) => GetBridge<SpinachNetworkBridge>(conn)?.ServerActivate()
                );

            // ── Flamethrower ──────────────────────────────────────────────────────
            Writer<FlamethrowerFireStartMessage>.write =
                FlamethrowerMessageSerialization.WriteFireStart;
            Reader<FlamethrowerFireStartMessage>.read =
                FlamethrowerMessageSerialization.ReadFireStart;

            Writer<FlamethrowerFireStopMessage>.write =
                FlamethrowerMessageSerialization.WriteFireStop;
            Reader<FlamethrowerFireStopMessage>.read =
                FlamethrowerMessageSerialization.ReadFireStop;

            Writer<FlamethrowerBurnRequestMessage>.write =
                FlamethrowerMessageSerialization.WriteBurnRequest;
            Reader<FlamethrowerBurnRequestMessage>.read =
                FlamethrowerMessageSerialization.ReadBurnRequest;

            Writer<FlamethrowerFireBeginMessage>.write =
                FlamethrowerMessageSerialization.WriteFireBegin;
            Reader<FlamethrowerFireBeginMessage>.read =
                FlamethrowerMessageSerialization.ReadFireBegin;

            Writer<FlamethrowerFireEndMessage>.write =
                FlamethrowerMessageSerialization.WriteFireEnd;
            Reader<FlamethrowerFireEndMessage>.read = FlamethrowerMessageSerialization.ReadFireEnd;

            Writer<FlamethrowerBurnBeginMessage>.write =
                FlamethrowerMessageSerialization.WriteBurnBegin;
            Reader<FlamethrowerBurnBeginMessage>.read =
                FlamethrowerMessageSerialization.ReadBurnBegin;

            Writer<FlamethrowerBurnEndMessage>.write =
                FlamethrowerMessageSerialization.WriteBurnEnd;
            Reader<FlamethrowerBurnEndMessage>.read = FlamethrowerMessageSerialization.ReadBurnEnd;

            NetworkClient.RegisterHandler<FlamethrowerFireBeginMessage>(
                FlamethrowerNetworkBridge.HandleFireBegin
            );
            NetworkClient.RegisterHandler<FlamethrowerFireEndMessage>(
                FlamethrowerNetworkBridge.HandleFireEnd
            );
            NetworkClient.RegisterHandler<FlamethrowerBurnBeginMessage>(
                FlamethrowerNetworkBridge.HandleBurnBegin
            );
            NetworkClient.RegisterHandler<FlamethrowerBurnEndMessage>(
                FlamethrowerNetworkBridge.HandleBurnEnd
            );

            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<FlamethrowerFireStartMessage>(
                    (conn, msg) =>
                        GetBridge<FlamethrowerNetworkBridge>(conn)?.ServerHandleFireStart()
                );
                NetworkServer.RegisterHandler<FlamethrowerFireStopMessage>(
                    (conn, msg) =>
                        GetBridge<FlamethrowerNetworkBridge>(conn)?.ServerHandleFireStop()
                );
                NetworkServer.RegisterHandler<FlamethrowerBurnRequestMessage>(
                    (conn, msg) =>
                        GetBridge<FlamethrowerNetworkBridge>(conn)
                            ?.ServerHandleBurnRequest(conn, msg.VictimNetId)
                );
            }

            // ── Hotkey item-giving (Client → Server) ─────────────────────────────
            Writer<GiveItemRequestMessage>.write =
                GiveItemRequestMessageSerialization.WriteGiveItemRequestMessage;
            Reader<GiveItemRequestMessage>.read =
                GiveItemRequestMessageSerialization.ReadGiveItemRequestMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<GiveItemRequestMessage>(
                    ItemRegistry.ServerHandleGiveItemRequest
                );

            // ── Gravity Gun ─────────────────────────────────────────────────
            Writer<GravityGunLockOnMessage>.write =
                GravityGunLockOnMessageSerialization.WriteGravityGunLockOnMessage;
            Reader<GravityGunLockOnMessage>.read =
                GravityGunLockOnMessageSerialization.ReadGravityGunLockOnMessage;

            Writer<GravityGunAimTickMessage>.write =
                GravityGunAimTickMessageSerialization.WriteGravityGunAimTickMessage;
            Reader<GravityGunAimTickMessage>.read =
                GravityGunAimTickMessageSerialization.ReadGravityGunAimTickMessage;

            Writer<GravityGunReleaseMessage>.write =
                GravityGunReleaseMessageSerialization.WriteGravityGunReleaseMessage;
            Reader<GravityGunReleaseMessage>.read =
                GravityGunReleaseMessageSerialization.ReadGravityGunReleaseMessage;

            Writer<GravityGunConnectedMessage>.write =
                GravityGunConnectedMessageSerialization.WriteGravityGunConnectedMessage;
            Reader<GravityGunConnectedMessage>.read =
                GravityGunConnectedMessageSerialization.ReadGravityGunConnectedMessage;

            Writer<GravityGunTetherTickMessage>.write =
                GravityGunTetherTickMessageSerialization.WriteGravityGunTetherTickMessage;
            Reader<GravityGunTetherTickMessage>.read =
                GravityGunTetherTickMessageSerialization.ReadGravityGunTetherTickMessage;

            Writer<GravityGunDisconnectedMessage>.write =
                GravityGunDisconnectedMessageSerialization.WriteGravityGunDisconnectedMessage;
            Reader<GravityGunDisconnectedMessage>.read =
                GravityGunDisconnectedMessageSerialization.ReadGravityGunDisconnectedMessage;

            Writer<GravityGunBusyMessage>.write =
                GravityGunBusyMessageSerialization.WriteGravityGunBusyMessage;
            Reader<GravityGunBusyMessage>.read =
                GravityGunBusyMessageSerialization.ReadGravityGunBusyMessage;

            NetworkClient.RegisterHandler<GravityGunConnectedMessage>(
                GravityGunNetworkBridge.HandleGravityGunConnected
            );
            NetworkClient.RegisterHandler<GravityGunTetherTickMessage>(
                GravityGunNetworkBridge.HandleGravityGunTetherTick
            );
            NetworkClient.RegisterHandler<GravityGunDisconnectedMessage>(
                GravityGunNetworkBridge.HandleGravityGunDisconnected
            );
            NetworkClient.RegisterHandler<GravityGunBusyMessage>(
                GravityGunNetworkBridge.HandleGravityGunBusy
            );

            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<GravityGunLockOnMessage>(
                    (conn, msg) =>
                        GetBridge<GravityGunNetworkBridge>(conn)?.ServerHandleLockOn(conn, msg)
                );
                NetworkServer.RegisterHandler<GravityGunAimTickMessage>(
                    (conn, msg) =>
                        GetBridge<GravityGunNetworkBridge>(conn)?.ServerHandleAimTick(conn, msg)
                );
                NetworkServer.RegisterHandler<GravityGunReleaseMessage>(
                    (conn, msg) =>
                        GetBridge<GravityGunNetworkBridge>(conn)?.ServerHandleRelease(conn)
                );
            }

            // ── Rocket Tether (lock-on item specific) ────────────────────────────
            Writer<RocketTetherLockOnMessage>.write =
                RocketTetherLockOnMessageSerialization.WriteRocketTetherLockOnMessage;
            Reader<RocketTetherLockOnMessage>.read =
                RocketTetherLockOnMessageSerialization.ReadRocketTetherLockOnMessage;

            Writer<RocketTetherBusyMessage>.write =
                RocketTetherBusyMessageSerialization.WriteRocketTetherBusyMessage;
            Reader<RocketTetherBusyMessage>.read =
                RocketTetherBusyMessageSerialization.ReadRocketTetherBusyMessage;

            NetworkClient.RegisterHandler<RocketTetherBusyMessage>(
                RocketTetherNetworkBridge.HandleRocketTetherBusy
            );

            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<RocketTetherLockOnMessage>(
                    (conn, msg) =>
                        GetBridge<RocketTetherNetworkBridge>(conn)?.ServerHandleLockOn(conn, msg)
                );
            }

            // ── Shared Rocket Victim Messages (Rocket Tether + Rocket Tether Grenade) ──
            // Registered once here; RocketTetherClientLogic handles both items.
            Writer<RocketVictimConnectedMessage>.write =
                RocketVictimConnectedMessageSerialization.WriteRocketVictimConnectedMessage;
            Reader<RocketVictimConnectedMessage>.read =
                RocketVictimConnectedMessageSerialization.ReadRocketVictimConnectedMessage;

            Writer<RocketVictimDisconnectedMessage>.write =
                RocketVictimDisconnectedMessageSerialization.WriteRocketVictimDisconnectedMessage;
            Reader<RocketVictimDisconnectedMessage>.read =
                RocketVictimDisconnectedMessageSerialization.ReadRocketVictimDisconnectedMessage;

            // Server → Client handlers: registered without NetworkServer.active guard
            // so all clients (including non-hosts) receive them.
            NetworkClient.RegisterHandler<RocketVictimConnectedMessage>(
                RocketTetherClientLogic.HandleVictimConnected
            );
            NetworkClient.RegisterHandler<RocketVictimDisconnectedMessage>(
                RocketTetherClientLogic.HandleVictimDisconnected
            );

            // ── Rocket Tether Grenade Messages ────────────────────────────────────
            Writer<RocketTetherGrenadeThrowMessage>.write =
                RocketTetherGrenadeMessageSerialization.WriteRocketTetherGrenadeThrowMessage;
            Reader<RocketTetherGrenadeThrowMessage>.read =
                RocketTetherGrenadeMessageSerialization.ReadRocketTetherGrenadeThrowMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<RocketTetherGrenadeThrowMessage>(
                    (conn, msg) =>
                        GetBridge<RocketTetherGrenadeNetworkBridge>(conn)
                            ?.ServerHandleThrow(msg.ThrowOrigin, msg.ThrowVelocity)
                );

            Writer<RocketTetherGrenadeLandedMessage>.write =
                RocketTetherGrenadeMessageSerialization.WriteRocketTetherGrenadeLandedMessage;
            Reader<RocketTetherGrenadeLandedMessage>.read =
                RocketTetherGrenadeMessageSerialization.ReadRocketTetherGrenadeLandedMessage;
            NetworkClient.RegisterHandler<RocketTetherGrenadeLandedMessage>(msg =>
                RocketTetherGrenadeNetworkBridge.ClientHandleLand(msg.Position, msg.ThrowerNetId)
            );

            // ── Teleporter Messages ───────────────────────────────────────────────

            // Client → Server: player confirmed a teleport destination.
            Writer<TeleporterUseMessage>.write =
                TeleporterUseMessageSerialization.WriteTeleporterUseMessage;
            Reader<TeleporterUseMessage>.read =
                TeleporterUseMessageSerialization.ReadTeleporterUseMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<TeleporterUseMessage>(
                    (conn, msg) =>
                        GetBridge<TeleporterNetworkBridge>(conn)
                            ?.ServerHandleUse(msg.Destination, msg.EquippedIndex)
                );

            // Server → Using Client only: warp the local CharacterController.
            Writer<TeleporterWarpMessage>.write =
                TeleporterWarpMessageSerialization.WriteTeleporterWarpMessage;
            Reader<TeleporterWarpMessage>.read =
                TeleporterWarpMessageSerialization.ReadTeleporterWarpMessage;
            NetworkClient.RegisterHandler<TeleporterWarpMessage>(
                TeleporterNetworkBridge.HandleWarp
            );

            // Server → All Clients: play the particle VFX at origin and destination.
            Writer<TeleporterVfxMessage>.write =
                TeleporterVfxMessageSerialization.WriteTeleporterVfxMessage;
            Reader<TeleporterVfxMessage>.read =
                TeleporterVfxMessageSerialization.ReadTeleporterVfxMessage;
            NetworkClient.RegisterHandler<TeleporterVfxMessage>(TeleporterNetworkBridge.HandleVfx);

            // ── Wind Storm Messages ───────────────────────────────────────────────
            Writer<WindStormActivateMessage>.write = WindStormMessageSerialization.WriteActivate;
            Reader<WindStormActivateMessage>.read = WindStormMessageSerialization.ReadActivate;
            Writer<WindStormBeginMessage>.write = WindStormMessageSerialization.WriteBegin;
            Reader<WindStormBeginMessage>.read = WindStormMessageSerialization.ReadBegin;
            Writer<WindStormEndMessage>.write = WindStormMessageSerialization.WriteEnd;
            Reader<WindStormEndMessage>.read = WindStormMessageSerialization.ReadEnd;
            NetworkClient.RegisterHandler<WindStormBeginMessage>(
                WindStormNetworkBridge.HandleBegin
            );
            NetworkClient.RegisterHandler<WindStormEndMessage>(WindStormNetworkBridge.HandleEnd);
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<WindStormActivateMessage>(
                    (conn, msg) => GetBridge<WindStormNetworkBridge>(conn)?.ServerActivate()
                );

            // ── UFO Abduction ─────────────────────────────────────────────────────

            // Client → Server
            Writer<UfoAbductionLockOnMessage>.write =
                UfoAbductionLockOnMessageSerialization.WriteUfoAbductionLockOnMessage;
            Reader<UfoAbductionLockOnMessage>.read =
                UfoAbductionLockOnMessageSerialization.ReadUfoAbductionLockOnMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<UfoAbductionLockOnMessage>(
                    (conn, msg) =>
                        GetBridge<UfoAbductionNetworkBridge>(conn)?.ServerHandleLockOn(conn, msg)
                );

            Writer<UfoAbductionDropoffSelectedMessage>.write =
                UfoAbductionDropoffSelectedMessageSerialization.WriteUfoAbductionDropoffSelectedMessage;
            Reader<UfoAbductionDropoffSelectedMessage>.read =
                UfoAbductionDropoffSelectedMessageSerialization.ReadUfoAbductionDropoffSelectedMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<UfoAbductionDropoffSelectedMessage>(
                    (conn, msg) =>
                        GetBridge<UfoAbductionNetworkBridge>(conn)
                            ?.ServerHandleDropoffSelected(conn, msg)
                );

            Writer<UfoAbductionDropoffCancelledMessage>.write =
                UfoAbductionDropoffCancelledMessageSerialization.WriteUfoAbductionDropoffCancelledMessage;
            Reader<UfoAbductionDropoffCancelledMessage>.read =
                UfoAbductionDropoffCancelledMessageSerialization.ReadUfoAbductionDropoffCancelledMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<UfoAbductionDropoffCancelledMessage>(
                    (conn, msg) =>
                        GetBridge<UfoAbductionNetworkBridge>(conn)
                            ?.ServerHandleDropoffCancelled(conn, msg)
                );

            // Server → Wielder only
            Writer<UfoAbductionBusyMessage>.write =
                UfoAbductionBusyMessageSerialization.WriteUfoAbductionBusyMessage;
            Reader<UfoAbductionBusyMessage>.read =
                UfoAbductionBusyMessageSerialization.ReadUfoAbductionBusyMessage;
            NetworkClient.RegisterHandler<UfoAbductionBusyMessage>(
                UfoAbductionNetworkBridge.HandleBusy
            );

            Writer<UfoAbductionTargetAcquiredMessage>.write =
                UfoAbductionTargetAcquiredMessageSerialization.WriteUfoAbductionTargetAcquiredMessage;
            Reader<UfoAbductionTargetAcquiredMessage>.read =
                UfoAbductionTargetAcquiredMessageSerialization.ReadUfoAbductionTargetAcquiredMessage;
            NetworkClient.RegisterHandler<UfoAbductionTargetAcquiredMessage>(
                UfoAbductionNetworkBridge.HandleTargetAcquired
            );

            // Server → Victim only
            Writer<UfoAbductionBeingTargetedMessage>.write =
                UfoAbductionBeingTargetedMessageSerialization.WriteUfoAbductionBeingTargetedMessage;
            Reader<UfoAbductionBeingTargetedMessage>.read =
                UfoAbductionBeingTargetedMessageSerialization.ReadUfoAbductionBeingTargetedMessage;
            NetworkClient.RegisterHandler<UfoAbductionBeingTargetedMessage>(
                UfoAbductionNetworkBridge.HandleBeingTargeted
            );

            // Server → All Clients
            Writer<UfoAbductionSessionAbortedMessage>.write =
                UfoAbductionSessionAbortedMessageSerialization.WriteUfoAbductionSessionAbortedMessage;
            Reader<UfoAbductionSessionAbortedMessage>.read =
                UfoAbductionSessionAbortedMessageSerialization.ReadUfoAbductionSessionAbortedMessage;
            NetworkClient.RegisterHandler<UfoAbductionSessionAbortedMessage>(
                UfoAbductionNetworkBridge.HandleSessionAborted
            );

            Writer<UfoAbductionBeginMessage>.write =
                UfoAbductionBeginMessageSerialization.WriteUfoAbductionBeginMessage;
            Reader<UfoAbductionBeginMessage>.read =
                UfoAbductionBeginMessageSerialization.ReadUfoAbductionBeginMessage;
            NetworkClient.RegisterHandler<UfoAbductionBeginMessage>(
                UfoAbductionClientLogic.HandleBegin
            );

            Writer<UfoAbductionEndMessage>.write =
                UfoAbductionEndMessageSerialization.WriteUfoAbductionEndMessage;
            Reader<UfoAbductionEndMessage>.read =
                UfoAbductionEndMessageSerialization.ReadUfoAbductionEndMessage;
            NetworkClient.RegisterHandler<UfoAbductionEndMessage>(
                UfoAbductionClientLogic.HandleEnd
            );

            // ── Moon ──────────────────────────────────────────────────────────────

            // Client → Server
            Writer<MoonUseMessage>.write = MoonUseMessageSerialization.WriteMoonUseMessage;
            Reader<MoonUseMessage>.read = MoonUseMessageSerialization.ReadMoonUseMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<MoonUseMessage>(
                    (conn, msg) => GetBridge<MoonNetworkBridge>(conn)?.ServerHandleUse(conn, msg)
                );

            // Server → All Clients
            Writer<MoonBeginMessage>.write = MoonBeginMessageSerialization.WriteMoonBeginMessage;
            Reader<MoonBeginMessage>.read = MoonBeginMessageSerialization.ReadMoonBeginMessage;
            NetworkClient.RegisterHandler<MoonBeginMessage>(MoonClientLogic.HandleMoonBegin);

            Writer<MoonEndMessage>.write = MoonEndMessageSerialization.WriteMoonEndMessage;
            Reader<MoonEndMessage>.read = MoonEndMessageSerialization.ReadMoonEndMessage;
            NetworkClient.RegisterHandler<MoonEndMessage>(MoonClientLogic.HandleMoonEnd);

            // ── Hunter Drone ──────────────────────────────────────────────────────
            Writer<HunterDroneLaunchMessage>.write =
                HunterDroneLaunchMessageSerialization.WriteHunterDroneLaunchMessage;
            Reader<HunterDroneLaunchMessage>.read =
                HunterDroneLaunchMessageSerialization.ReadHunterDroneLaunchMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<HunterDroneLaunchMessage>(
                    (conn, msg) =>
                        GetBridge<HunterDroneNetworkBridge>(conn)?.ServerLaunchDrone(msg.AimPoint)
                );

            Writer<HunterDroneShotMessage>.write =
                HunterDroneShotMessageSerialization.WriteHunterDroneShotMessage;
            Reader<HunterDroneShotMessage>.read =
                HunterDroneShotMessageSerialization.ReadHunterDroneShotMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<HunterDroneShotMessage>(
                    (conn, msg) => HunterDroneNetworkBridge.ServerHandleDroneShot(msg.DroneNetId)
                );

            Writer<HunterDroneExplodedMessage>.write =
                HunterDroneExplodedMessageSerialization.WriteHunterDroneExplodedMessage;
            Reader<HunterDroneExplodedMessage>.read =
                HunterDroneExplodedMessageSerialization.ReadHunterDroneExplodedMessage;
            NetworkClient.RegisterHandler<HunterDroneExplodedMessage>(
                HunterDroneNetworkBridge.HandleDroneExploded
            );

            // ── Shape Shifter / Super Shape Shifter ──────────────────────────────

            // Client → Server: single-target request
            Writer<ShapeShifterRequestMessage>.write =
                ShapeShifterRequestMessageSerialization.WriteShapeShifterRequestMessage;
            Reader<ShapeShifterRequestMessage>.read =
                ShapeShifterRequestMessageSerialization.ReadShapeShifterRequestMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<ShapeShifterRequestMessage>(
                    (conn, msg) =>
                        GetBridge<ShapeShifterNetworkBridge>(conn)
                            ?.ServerHandleRequest(msg.TargetNetId, msg.EquippedSlotIndex)
                );

            // Client → Server: all-targets request (SuperShapeShifter)
            Writer<SuperShapeShifterRequestMessage>.write =
                SuperShapeShifterRequestMessageSerialization.WriteSuperShapeShifterRequestMessage;
            Reader<SuperShapeShifterRequestMessage>.read =
                SuperShapeShifterRequestMessageSerialization.ReadSuperShapeShifterRequestMessage;
            if (NetworkServer.active)
                NetworkServer.RegisterHandler<SuperShapeShifterRequestMessage>(
                    (conn, msg) =>
                        GetBridge<SuperShapeShifterNetworkBridge>(conn)
                            ?.ServerHandleRequest(msg.EquippedSlotIndex)
                );

            // Server → All Clients: cube effect begins on a specific ball
            Writer<ShapeShifterBeginMessage>.write =
                ShapeShifterBeginMessageSerialization.WriteShapeShifterBeginMessage;
            Reader<ShapeShifterBeginMessage>.read =
                ShapeShifterBeginMessageSerialization.ReadShapeShifterBeginMessage;
            NetworkClient.RegisterHandler<ShapeShifterBeginMessage>(
                ShapeShifterHelper.HandleShapeShifterBegin
            );

            // Server → All Clients: cube effect ends on a specific ball
            Writer<ShapeShifterEndMessage>.write =
                ShapeShifterEndMessageSerialization.WriteShapeShifterEndMessage;
            Reader<ShapeShifterEndMessage>.read =
                ShapeShifterEndMessageSerialization.ReadShapeShifterEndMessage;
            NetworkClient.RegisterHandler<ShapeShifterEndMessage>(
                ShapeShifterHelper.HandleShapeShifterEnd
            );

            // ── Explosive Golf Balls (Server → All Clients) ──────────────────────
            Writer<ExplosiveGolfBallsExplodeMessage>.write =
                ExplosiveGolfBallsExplodeMessageSerialization.WriteExplosiveGolfBallsExplodeMessage;
            Reader<ExplosiveGolfBallsExplodeMessage>.read =
                ExplosiveGolfBallsExplodeMessageSerialization.ReadExplosiveGolfBallsExplodeMessage;
            NetworkClient.RegisterHandler<ExplosiveGolfBallsExplodeMessage>(
                ExplosiveGolfBallsNetworkBridge.HandleExplosion
            );

            // ── Scaled explosion VFX (Server → All Clients) ──────────────────────
            Writer<ScaledExplosionVfxMessage>.write =
                ScaledExplosionVfxMessageSerialization.WriteScaledExplosionVfxMessage;
            Reader<ScaledExplosionVfxMessage>.read =
                ScaledExplosionVfxMessageSerialization.ReadScaledExplosionVfxMessage;
            NetworkClient.RegisterHandler<ScaledExplosionVfxMessage>(static msg =>
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    msg.Position,
                    Quaternion.identity,
                    Vector3.one * msg.Scale
                );
                CameraModuleController.Shake(
                    GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                    msg.Position
                );
            });
        }

        public static void ResetRegistration() => _prefabsRegistered = false;

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
            RegisterPrefab(AssetLoader.PoisonJarPrefab);
            RegisterPrefab(AssetLoader.DronePrefab);
            // Null-safe: RocketTetherGrenadePrefab is null when no dedicated asset exists.
            if (AssetLoader.RocketTetherGrenadePrefab != null)
                RegisterPrefab(AssetLoader.RocketTetherGrenadePrefab);
            // Null-safe: HunterDronePrefab is null until hunter_drone.prefab is added to the bundle.
            if (AssetLoader.HunterDronePrefab != null)
                RegisterPrefab(AssetLoader.HunterDronePrefab);
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

    /// When a remote client signals ready (after OnStartClient has registered their
    /// handlers), immediately broadcast the host's current config state so they don't
    /// have to wait up to 5 seconds for the next periodic sync cycle.
    ///
    /// This fixes two related bugs:
    ///   1. Gameplay config (e.g. FuelPerUse) was only synced periodically, leaving a
    ///      window where a freshly-joined client used local defaults instead of the
    ///      host's values.
    ///   2. Spawn weights and tier config were never sent to late-joining clients at all
    ///      unless the host made a change via the UI after they joined.
    [HarmonyPatch]
    static class BNetworkManagerOnServerReadyPatch
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(BNetworkManager),
                "OnServerReady",
                new[] { typeof(NetworkConnectionToClient) }
            );

        static void Postfix(NetworkConnectionToClient connection)
        {
            // The listen-server host's own connection also fires OnServerReady.
            // Skip it: the host already has authoritative values, and all three
            // handler methods guard against NetworkServer.active anyway.
            if (connection == NetworkServer.localConnection)
                return;

            // Send only to the newly-joined client — NOT SendToAll.
            // SendToAll on every join can disconnect existing clients whose Steam
            // transport connection happens to be momentarily stale at that instant.
            // The periodic 5-second sync will update all clients within one tick.
            SpawnWeightsSyncer.SyncToConnection(connection);
            ItemConfigSyncer.BroadcastToConnection(connection);
        }
    }
}
