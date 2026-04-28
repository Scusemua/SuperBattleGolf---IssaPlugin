// ItemConfigSyncer.cs
// Runs on the host. Every SyncInterval seconds it broadcasts all BepInEx config
// entries to all clients so they use the host's authoritative values rather than
// their own local defaults.
//
// This covers every item in one shot — no per-item sync messages needed.
// The listen-server host skips applying the message it receives from itself.

using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class ItemConfigSyncer : MonoBehaviour
    {
        private const float SyncInterval = 5f;

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
        /// Immediately broadcast the host's config to all connected clients.
        /// Safe to call at any time on the host; no-op on clients.
        /// </summary>
        internal static void Broadcast()
        {
            if (!NetworkServer.active || !NetworkClient.active)
                return;

            var cfg = IssaPluginPlugin.Instance.Config;
            var keys = new List<string>(cfg.Count);
            var values = new List<string>(cfg.Count);

            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in cfg)
            {
                // Skip keybinding entries — clients control their own key mappings.
                if (kv.Value.SettingType == typeof(Key))
                    continue;

                keys.Add($"{kv.Key.Section}::{kv.Key.Key}");
                values.Add(kv.Value.GetSerializedValue() ?? string.Empty);
            }

            NetworkServer.SendToAll(
                new ItemConfigSyncMessage { Keys = keys.ToArray(), Values = values.ToArray() }
            );

            IssaPluginPlugin.Log.LogDebug(
                $"[ItemConfigSyncer] Broadcast {keys.Count} config entries to all clients."
            );
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

            var cfg = IssaPluginPlugin.Instance.Config;
            var keys = new List<string>(cfg.Count);
            var values = new List<string>(cfg.Count);

            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in cfg)
            {
                if (kv.Value.SettingType == typeof(Key))
                    continue;
                keys.Add($"{kv.Key.Section}::{kv.Key.Key}");
                values.Add(kv.Value.GetSerializedValue() ?? string.Empty);
            }

            conn.Send(
                new ItemConfigSyncMessage { Keys = keys.ToArray(), Values = values.ToArray() }
            );

            IssaPluginPlugin.Log.LogDebug(
                $"[ItemConfigSyncer] Sent {keys.Count} config entries to joining client conn={conn.connectionId}."
            );
        }

        /// <summary>
        /// Called on each client when an ItemConfigSyncMessage arrives from the host.
        /// Applies each entry to the local BepInEx ConfigFile via SetSerializedValue.
        /// </summary>
        internal static void HandleConfigSync(ItemConfigSyncMessage msg)
        {
            // The listen-server host is also a client; don't overwrite its own config.
            if (NetworkServer.active)
                return;

            var cfg = IssaPluginPlugin.Instance.Config;
            int count = msg.Keys?.Length ?? 0;
            int applied = 0;

            for (int i = 0; i < count; i++)
            {
                var parts = msg.Keys[i].Split(new[] { "::" }, 2, StringSplitOptions.None);
                if (parts.Length != 2)
                    continue;

                var def = new ConfigDefinition(parts[0], parts[1]);
                if (!cfg.ContainsKey(def))
                    continue;

                cfg[def].SetSerializedValue(msg.Values[i]);
                applied++;
            }

            IssaPluginPlugin.Log.LogDebug(
                $"[ItemConfigSyncer] Applied {applied}/{count} config entries from host."
            );
        }
    }
}
