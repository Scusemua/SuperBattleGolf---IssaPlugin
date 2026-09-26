// ItemConfigSyncer.cs
// Runs on the host. Every SyncInterval seconds it broadcasts the host's BepInEx
// config entries to all clients. A remote client stores that snapshot in
// SessionConfig and leaves its own cfg file untouched.
//
// This covers every item in one shot — no per-item sync messages needed.
// The listen-server host skips applying the message it receives from itself.
//
// Change-guarding
// ---------------
// The periodic broadcast only sends when the snapshot actually differs from the
// last one sent (see SnapshotEquals).  An identical payload would be a no-op on
// every client, so skipping it is behaviourally equivalent while removing ~14 KB
// per client per tick of redundant reliable traffic.
//
// Whenever anything could invalidate that assumption — a client joining, the
// server starting or stopping, or a config value changing on the host — the
// sentinel is cleared via ResetSyncState() so the next tick sends in full.
// ForceBroadcast() sends immediately for in-game edits that must propagate now.

using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin
{
    public class ItemConfigSyncer : MonoBehaviour
    {
        private const float SyncInterval = 5f;

        // Snapshot of the last payload broadcast to all clients. Null means "no
        // known client state" and forces the next Broadcast() to send.
        // Compared element-wise rather than hashed so there is no collision risk:
        // if the arrays match, the message we would send is byte-identical.
        private static string[] _lastSentKeys;
        private static string[] _lastSentValues;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSeconds(SyncInterval);
                if (NetworkServer.active)
                    Broadcast();
            }
        }

        /// <summary>
        /// Clears the change-guard sentinel so the next <see cref="Broadcast"/>
        /// sends the full config regardless of whether it changed.
        ///
        /// Call whenever the set of clients or the host's config may have changed
        /// underneath us: client join, server start/stop, or a BepInEx setting
        /// change (including a config file edited on disk mid-session).
        /// </summary>
        internal static void ResetSyncState()
        {
            _lastSentKeys = null;
            _lastSentValues = null;
        }

        /// <summary>
        /// Broadcasts the host's config to all clients, but only if it differs
        /// from the last broadcast. Safe to call at any time on the host; no-op
        /// on clients.
        /// </summary>
        internal static void Broadcast()
        {
            if (!NetworkServer.active || !NetworkClient.active)
                return;

            BuildSnapshot(out var keys, out var values);

            // Identical payload — every client already holds these values, so
            // sending again would change nothing. Skip silently: logging here
            // would run on every tick and defeat the point of the guard.
            if (SnapshotEquals(keys, values))
                return;

            // sendToReadyOnly: connections that are not ready are still loading and
            // will receive the full config via BroadcastToConnection when they become
            // ready. Sending to them here wastes bandwidth exactly when a joining
            // client can least afford it.
            NetworkServer.SendToAll(
                new ItemConfigSyncMessage { Keys = keys, Values = values },
                sendToReadyOnly: true
            );

            _lastSentKeys = keys;
            _lastSentValues = values;

            // Info rather than Debug: BepInEx's default log filter drops Debug, and
            // this line is the primary field evidence that the change guard is
            // working. In steady state it should appear once and then stop; a log
            // full of these means something is invalidating the guard every tick.
            IssaPluginPlugin.Log.LogInfo(
                $"[ItemConfigSyncer] Broadcast {keys.Length} config entries to all clients "
                    + $"({EstimatePayloadBytes(keys, values) / 1024f:F1} KB)."
            );
        }

        /// <summary>
        /// Clears the sentinel and broadcasts immediately, bypassing the change
        /// guard. Use after an in-game config edit that must reach clients now
        /// rather than on the next periodic tick.
        ///
        /// In practice the Config.SettingChanged hook in Plugin.Awake has usually
        /// cleared the sentinel already by the time this runs. That overlap is
        /// deliberate: callers that must push a change immediately should not have
        /// to depend on an event subscription elsewhere still being wired up.
        /// </summary>
        internal static void ForceBroadcast()
        {
            ResetSyncState();
            Broadcast();
        }

        /// <summary>
        /// Sends the host's full config to a single newly-joined client only.
        /// Use this instead of Broadcast() during OnServerReady so existing clients
        /// are not hit with a SendToAll that could disconnect them if their transport
        /// connection is momentarily stale.
        /// </summary>
        internal static void BroadcastToConnection(NetworkConnectionToClient conn)
        {
            if (!NetworkServer.active || !NetworkClient.active)
                return;

            BuildSnapshot(out var keys, out var values);

            conn.Send(new ItemConfigSyncMessage { Keys = keys, Values = values });

            // A client joining changes who holds what. Clearing the sentinel keeps
            // the periodic broadcast authoritative for everyone rather than letting
            // this single-connection send stand in for a full one.
            ResetSyncState();

            IssaPluginPlugin.Log.LogDebug(
                $"[ItemConfigSyncer] Sent {keys.Length} config entries to joining client conn={conn.connectionId}."
            );
        }

        /// <summary>
        /// Builds the wire payload: every synced config entry as a "Section::Key"
        /// string plus its serialized value, in ConfigFile enumeration order.
        /// Diagnostics, UI, and key bindings are omitted — see SessionConfig.ShouldSync.
        ///
        /// Single source of truth for both send paths — Broadcast() and
        /// BroadcastToConnection() must never disagree about what gets synced.
        /// GetSerializedValue reads BoxedValue, which bypasses the session overlay,
        /// so a listen host broadcasts its file even if a client snapshot is still up.
        /// </summary>
        private static void BuildSnapshot(out string[] keys, out string[] values)
        {
            var cfg = IssaPluginPlugin.Instance.Config;
            var keyList = new List<string>(cfg.Count);
            var valueList = new List<string>(cfg.Count);

            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in cfg)
            {
                if (!SessionConfig.ShouldSync(kv.Value))
                    continue;

                keyList.Add($"{kv.Key.Section}::{kv.Key.Key}");
                valueList.Add(kv.Value.GetSerializedValue() ?? string.Empty);
            }

            keys = keyList.ToArray();
            values = valueList.ToArray();
        }

        /// <summary>
        /// Approximate serialized size of a snapshot: each string costs its UTF-8
        /// bytes plus Mirror's 2-byte length prefix. Used only for log output, so an
        /// estimate that assumes single-byte characters is good enough — config keys
        /// and serialized values are ASCII in practice.
        /// </summary>
        private static int EstimatePayloadBytes(string[] keys, string[] values)
        {
            int total = 0;
            for (int i = 0; i < keys.Length; i++)
                total += keys[i].Length + 2 + values[i].Length + 2;
            return total;
        }

        /// <summary>
        /// True when the given snapshot is identical to the last one broadcast.
        /// Returns false when no previous snapshot exists, so a cleared sentinel
        /// always forces a send.
        /// </summary>
        private static bool SnapshotEquals(string[] keys, string[] values)
        {
            if (_lastSentKeys == null || _lastSentValues == null)
                return false;
            if (_lastSentKeys.Length != keys.Length || _lastSentValues.Length != values.Length)
                return false;

            for (int i = 0; i < keys.Length; i++)
            {
                if (!string.Equals(_lastSentKeys[i], keys[i], StringComparison.Ordinal))
                    return false;
                if (!string.Equals(_lastSentValues[i], values[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Called on each client when an ItemConfigSyncMessage arrives from the host.
        /// Stores the snapshot in SessionConfig. The local cfg file is not modified.
        /// </summary>
        internal static void HandleConfigSync(ItemConfigSyncMessage msg)
        {
            // The listen-server host keeps its own file. After disconnect, a late
            // packet must not refill the overlay.
            if (!SessionConfig.IsRemoteClient)
                return;

            int count = msg.Keys?.Length ?? 0;
            int applied = SessionConfig.Replace(msg);

            // Info rather than Debug so it appears in a player's log. On a client
            // this should be rare — once on join, then only when the host actually
            // changes something. Repeated lines every few seconds mean the host's
            // change guard is not holding.
            IssaPluginPlugin.Log.LogInfo(
                $"[ItemConfigSyncer] Applied {applied}/{count} config entries from host."
            );
        }
    }
}
