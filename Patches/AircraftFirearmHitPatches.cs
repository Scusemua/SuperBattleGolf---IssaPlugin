using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Enables firearms (elephant gun, sniper rifle, AK-47, dueling pistol) to
    /// shoot down large aircraft (Harrier, AC-130, Stealth Bomber) in all
    /// multiplayer configurations.
    ///
    /// Why UserCode patches instead of static method patches:
    ///   PlayElephantGunMissForAllClients is a static wrapper that plays VFX
    ///   locally and then sends a [Command(requiresAuthority = false)] to the
    ///   server. Patching the static method fires on the CALLING machine, so
    ///   non-host clients would see NetworkServer.active = false and skip the
    ///   aircraft check. Instead we patch the server-side UserCode handler
    ///   (UserCode_CmdPlay...ForAllClients) which runs on the server for ALL
    ///   clients: the host calls it directly (isServer && isClient short-circuit)
    ///   and non-host clients trigger it when their Command arrives on the server.
    ///
    /// AK-47 rate-limit handling:
    ///   The AK-47 rate-limits its VFX call so one Command is sent per 0.1 s rather
    ///   than per bullet. For bullets where the VFX Command is skipped, the UserCode
    ///   patch never fires on the server. AK47Item.DoShoot calls TryHitAircraftAlongRay
    ///   directly for those bullets only. On the server (host) this calls OnHit
    ///   directly; on a non-host client it sends an AircraftFirearmHitMessage so the
    ///   server still processes every single bullet.
    /// </summary>
    // ── Elephant gun / sniper rifle — server-side Command handler patch ──────────

    [HarmonyPatch]
    static class ElephantGunAircraftHitPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(VfxManager),
                "UserCode_CmdPlayElephantGunMissForAllClients__PlayerInventory__Vector3__NetworkConnectionToClient"
            );

        static void Postfix(PlayerInventory shootingPlayer, Vector3 direction)
        {
            if (shootingPlayer == null)
                return;

            AircraftFirearmHitHelper.HitAircraftDirectly(
                shootingPlayer.GetElephantGunBarrelEndPosition(),
                direction
            );
        }
    }

    // ── Dueling pistol — server-side Command handler patch ───────────────────────

    [HarmonyPatch]
    static class DuelingPistolAircraftHitPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(VfxManager),
                "UserCode_CmdPlayDuelingPistolMissForAllClients__PlayerInventory__Vector3__NetworkConnectionToClient"
            );

        static void Postfix(PlayerInventory shootingPlayer, Vector3 direction)
        {
            if (shootingPlayer == null)
                return;

            AircraftFirearmHitHelper.HitAircraftDirectly(
                shootingPlayer.GetDuelingPistolBarrelEndPosition(),
                direction
            );
        }
    }

    // ── Mirror message: non-host client → server for rate-limited AK-47 bullets ──

    public struct AircraftFirearmHitMessage : NetworkMessage
    {
        /// <summary>netId of the aircraft's NetworkIdentity.</summary>
        public uint AircraftNetId;

        /// <summary>World-space raycast hit point (used for LastHitWorldPos).</summary>
        public Vector3 HitPoint;
    }

    public static class AircraftFirearmHitMessageSerialization
    {
        public static void WriteAircraftFirearmHitMessage(
            NetworkWriter writer,
            AircraftFirearmHitMessage msg
        )
        {
            writer.WriteUInt(msg.AircraftNetId);
            writer.WriteVector3(msg.HitPoint);
        }

        public static AircraftFirearmHitMessage ReadAircraftFirearmHitMessage(
            NetworkReader reader
        ) =>
            new AircraftFirearmHitMessage
            {
                AircraftNetId = reader.ReadUInt(),
                HitPoint = reader.ReadVector3(),
            };
    }

    // ── Shared helper ────────────────────────────────────────────────────────────

    public static class AircraftFirearmHitHelper
    {
        private const float MaxRange = 2000f;

        /// <summary>
        /// Performs an all-layers raycast server-side and calls OnHit on any aircraft
        /// hit receiver found. Called by the UserCode patches for every non-rate-limited
        /// missed shot. Must only be called on the server; aircraft hit-receiver
        /// components (AC130HitReceiver, HarrierHitReceiver, BomberProxyBehaviour) are
        /// added server-side only and do not exist on client-side copies of the objects.
        /// </summary>
        public static void HitAircraftDirectly(Vector3 origin, Vector3 direction)
        {
            if (direction == Vector3.zero)
                return;

            var hits = Physics.RaycastAll(
                origin,
                direction,
                MaxRange,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            ProcessHitsOnServer(hits);
        }

        /// <summary>
        /// Called by AK47Item for rate-limited bullets where the VFX Command is
        /// skipped and the server-side UserCode patch will not fire.
        /// Branches on server vs. client:
        ///   • Server (host): raycast and call OnHit directly.
        ///   • Non-host client: raycast and send AircraftFirearmHitMessage to the server.
        /// </summary>
        public static void TryHitAircraftAlongRay(Vector3 origin, Vector3 direction)
        {
            if (direction == Vector3.zero)
                return;

            var hits = Physics.RaycastAll(
                origin,
                direction,
                MaxRange,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            if (NetworkServer.active)
                ProcessHitsOnServer(hits);
            else
                SendHitMessagesToServer(hits);
        }

        private static void ProcessHitsOnServer(RaycastHit[] hits)
        {
            bool hitAC130 = false,
                hitHarrier = false,
                hitBomber = false;

            foreach (var hit in hits)
            {
                if (!hitAC130)
                {
                    var ac130 = hit.collider.GetComponentInParent<AC130HitReceiver>();
                    if (ac130 != null && ModConfig.AC130.FirearmsCanHit.Value)
                    {
                        hitAC130 = true;
                        IssaPluginPlugin.Log.LogInfo(
                            "[AC130] Firearm hit — notifying AC130HitReceiver."
                        );
                        ac130.OnHit?.Invoke();
                    }
                }

                if (!hitHarrier)
                {
                    var harrier = hit.collider.GetComponentInParent<HarrierHitReceiver>();
                    if (harrier != null && ModConfig.Harrier.FirearmsCanHit.Value)
                    {
                        hitHarrier = true;
                        harrier.LastHitWorldPos = hit.point;
                        IssaPluginPlugin.Log.LogInfo(
                            "[Harrier] Firearm hit — notifying HarrierHitReceiver."
                        );
                        harrier.OnHit?.Invoke();
                    }
                }

                if (!hitBomber)
                {
                    var bomber = hit.collider.GetComponentInParent<BomberProxyBehaviour>();
                    if (bomber != null && ModConfig.StealthBomber.FirearmsCanHit.Value)
                    {
                        hitBomber = true;
                        bomber.LastHitWorldPos = hit.point;
                        IssaPluginPlugin.Log.LogInfo(
                            "[Bomber] Firearm hit — notifying BomberProxyBehaviour."
                        );
                        bomber.OnHit?.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// Client-side: find NetworkIdentities along the ray and send one message per
        /// unique aircraft to the server. The server resolves which component to hit.
        /// Non-aircraft networked objects are discarded silently by ServerHandleHit.
        /// </summary>
        private static void SendHitMessagesToServer(RaycastHit[] hits)
        {
            var alreadySent = new HashSet<uint>();
            foreach (var hit in hits)
            {
                var ni = hit.collider.GetComponentInParent<NetworkIdentity>();
                if (ni == null || !alreadySent.Add(ni.netId))
                    continue;

                NetworkClient.Send(
                    new AircraftFirearmHitMessage { AircraftNetId = ni.netId, HitPoint = hit.point }
                );
            }
        }

        /// <summary>
        /// Server handler for AircraftFirearmHitMessage sent by non-host clients for
        /// rate-limited AK-47 bullets. Looks up the aircraft by netId and calls OnHit.
        /// Silently discards messages for non-aircraft netIds.
        /// </summary>
        internal static void ServerHandleHit(
            NetworkConnectionToClient conn,
            AircraftFirearmHitMessage msg
        )
        {
            if (
                !NetworkServer.spawned.TryGetValue(msg.AircraftNetId, out var identity)
                || identity == null
            )
                return;

            var go = identity.gameObject;

            var ac130 = go.GetComponentInChildren<AC130HitReceiver>();
            if (ac130 != null && ModConfig.AC130.FirearmsCanHit.Value)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[AC130] Firearm hit (client msg) — notifying AC130HitReceiver."
                );
                ac130.OnHit?.Invoke();
                return;
            }

            var harrier = go.GetComponentInChildren<HarrierHitReceiver>();
            if (harrier != null && ModConfig.Harrier.FirearmsCanHit.Value)
            {
                harrier.LastHitWorldPos = msg.HitPoint;
                IssaPluginPlugin.Log.LogInfo(
                    "[Harrier] Firearm hit (client msg) — notifying HarrierHitReceiver."
                );
                harrier.OnHit?.Invoke();
                return;
            }

            var bomber = go.GetComponentInChildren<BomberProxyBehaviour>();
            if (bomber != null && ModConfig.StealthBomber.FirearmsCanHit.Value)
            {
                bomber.LastHitWorldPos = msg.HitPoint;
                IssaPluginPlugin.Log.LogInfo(
                    "[Bomber] Firearm hit (client msg) — notifying BomberProxyBehaviour."
                );
                bomber.OnHit?.Invoke();
            }
            // Not an aircraft — silently discard.
        }
    }
}
