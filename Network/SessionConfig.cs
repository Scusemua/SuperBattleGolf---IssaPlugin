// SessionConfig.cs
// In-memory copy of the host's synced settings for a remote client.
//
// The client's BepInEx ConfigFile is never written by sync. Gameplay reads
// ConfigEntry<T>.Value, and the getter patches below return the overlay value
// when one exists. BoxedValue calls Value, and Save() reads BoxedValue, so those
// reads are marked and keep returning the stored file value. Diagnostics, UI,
// and key bindings are not put in the overlay, so those reads stay on the local file.
//
// The dictionary is dropped on disconnect and again when this process starts a
// server. A crash or hard shutdown never wrote the host values, so the next
// launch loads the client's own cfg.

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Network;
using Mirror;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    internal static class SessionConfig
    {
        // Null means no session. An empty dictionary is still a session: every
        // synced read misses and falls through to the local file.
        private static Dictionary<ConfigDefinition, object> _values;

        // BoxedValue's getter calls Value. Save() and ModConfig read BoxedValue,
        // so while this is above zero the Value postfix must return the stored
        // file value. Otherwise a hotkey edit mid-match would save the host snapshot.
        private static int _rawReadDepth;

        internal static bool IsActive => _values != null;

        /// <summary>
        /// This process is a connected client and not the listen-server host.
        /// </summary>
        internal static bool IsRemoteClient => NetworkClient.active && !NetworkServer.active;

        /// <summary>
        /// True for entries the host should broadcast and a client should apply.
        /// Key bindings and the Diagnostics and UI sections stay on each machine.
        /// </summary>
        internal static bool ShouldSync(ConfigEntryBase entry)
        {
            if (entry == null)
                return false;
            if (entry.SettingType == typeof(Key))
                return false;

            string section = entry.Definition.Section;
            return section != "Diagnostics" && section != "UI";
        }

        /// <summary>
        /// Replaces the overlay with a parsed snapshot. Unknown keys, local-only
        /// entries, and values that fail to parse are skipped.
        /// </summary>
        internal static int Replace(ItemConfigSyncMessage msg)
        {
            var cfg = IssaPluginPlugin.Instance.Config;
            int keyCount = msg.Keys?.Length ?? 0;
            int valueCount = msg.Values?.Length ?? 0;
            int count = Math.Min(keyCount, valueCount);
            var next = new Dictionary<ConfigDefinition, object>(count);

            for (int i = 0; i < count; i++)
            {
                var parts = msg.Keys[i].Split(new[] { "::" }, 2, StringSplitOptions.None);
                if (parts.Length != 2)
                    continue;

                var definition = new ConfigDefinition(parts[0], parts[1]);
                if (!cfg.ContainsKey(definition))
                    continue;

                ConfigEntryBase entry = cfg[definition];
                if (!ShouldSync(entry))
                    continue;

                object parsed;
                try
                {
                    parsed = TomlTypeConverter.ConvertToValue(msg.Values[i] ?? string.Empty, entry.SettingType);
                }
                catch (Exception ex)
                {
                    IssaPluginPlugin.Log.LogWarning(
                        $"[SessionConfig] Skipped {definition.Section}/{definition.Key}: {ex.Message}"
                    );
                    continue;
                }

                next[definition] = parsed;
            }

            _values = next;
            return next.Count;
        }

        /// <summary>
        /// Writes one synced value into the overlay, creating the session if a
        /// vote or spawn-weights message arrives before the first full snapshot.
        /// </summary>
        internal static void Set(ConfigEntryBase entry, object value)
        {
            if (entry == null)
                return;

            _values ??= new Dictionary<ConfigDefinition, object>();
            _values[entry.Definition] = value;
        }

        internal static bool TryGet<T>(ConfigDefinition definition, out T value)
        {
            value = default;
            if (_values == null || !_values.TryGetValue(definition, out object boxed))
                return false;

            if (boxed is T typed)
            {
                value = typed;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Drops the overlay and the client copy of the host's pool weights.
        /// GetPoolWeight prefers those arrays over config, so leaving them set
        /// would keep the previous match's weights after the overlay is gone.
        /// </summary>
        internal static void Clear()
        {
            _values = null;

            var items = ItemRegistry.AllItems;
            for (int i = 0; i < items.Count; i++)
                items[i].ResetServerWeights();
        }

        internal static void Install(Harmony harmony)
        {
            InstallGetter<bool>(harmony);
            InstallGetter<int>(harmony);
            InstallGetter<float>(harmony);
            InstallGetter<string>(harmony);
        }

        private static void InstallGetter<T>(Harmony harmony)
        {
            var entryType = typeof(ConfigEntry<T>);
            MethodInfo valueGetter = AccessTools.PropertyGetter(entryType, nameof(ConfigEntry<T>.Value));
            MethodInfo valuePostfix = AccessTools.Method(typeof(GetterPatch<T>), nameof(GetterPatch<T>.Postfix));
            MethodInfo boxedGetter = AccessTools.PropertyGetter(entryType, nameof(ConfigEntry<T>.BoxedValue));
            MethodInfo enterRaw = AccessTools.Method(typeof(SessionConfig), nameof(EnterRawRead));
            MethodInfo exitRaw = AccessTools.Method(typeof(SessionConfig), nameof(ExitRawRead));
            if (valueGetter == null || valuePostfix == null || boxedGetter == null || enterRaw == null || exitRaw == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[SessionConfig] Could not patch ConfigEntry<{typeof(T).Name}>."
                );
                return;
            }

            harmony.Patch(valueGetter, postfix: new HarmonyMethod(valuePostfix));
            harmony.Patch(
                boxedGetter,
                prefix: new HarmonyMethod(enterRaw),
                postfix: new HarmonyMethod(exitRaw)
            );
        }

        private static void EnterRawRead() => _rawReadDepth++;

        private static void ExitRawRead()
        {
            if (_rawReadDepth > 0)
                _rawReadDepth--;
        }

        private static class GetterPatch<T>
        {
            public static void Postfix(ConfigEntry<T> __instance, ref T __result)
            {
                // Inactive session: one null check, including every other mod's entries.
                // Raw reads (BoxedValue, Save, ModConfig) and the listen host keep the file.
                if (_rawReadDepth > 0 || !IsActive || !IsRemoteClient)
                    return;

                if (!TryGet(__instance.Definition, out T value))
                    return;

                // Section and key are not unique across plugins. Only our file is overlaid.
                ConfigFile config = IssaPluginPlugin.Instance?.Config;
                if (config == null || __instance.ConfigFile != config)
                    return;

                __result = value;
            }
        }
    }
}
