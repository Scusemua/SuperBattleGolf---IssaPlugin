// NetworkTrafficDiagnostics.cs
//
// Opt-in diagnostics for investigating lag reports. When
// Global.NetworkDiagnosticsEnabled is true, this logs a periodic summary of:
//
//   * outbound and inbound bandwidth, split by message type
//   * round-trip time and jitter
//   * Mirror's snapshot interpolation buffer, which inflates under jitter and is
//     what makes network trouble *feel* like lag rather than just packet loss
//   * spawned NetworkIdentity count, to catch spawn/despawn leaks
//
// Design notes
// ------------
// This exists to debug a performance problem, so it must not become one itself:
//
//   * Counting happens in Mirror's own NetworkDiagnostics events, which fire only
//     when a subscriber is attached. Disabled means no subscription and therefore
//     no per-message cost at all.
//   * Per-message work is a dictionary lookup and two integer adds. No allocation,
//     no string formatting, no logging on the hot path.
//   * Formatting and logging happen once per interval (default 30s).
//
// Logged at Info level: BepInEx's default log filter excludes Debug, so Debug
// lines would not reach a player's LogOutput.log and would be useless in a bug
// report.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Network
{
    public class NetworkTrafficDiagnostics : MonoBehaviour
    {
        private sealed class Counter
        {
            public long Bytes;
            public int Sends;
        }

        // Message type name -> totals accumulated since the last summary.
        private static readonly Dictionary<string, Counter> Outbound = new();
        private static readonly Dictionary<string, Counter> Inbound = new();

        // Scratch list reused across summaries so formatting allocates as little as
        // possible. Only touched inside LogSummary, which runs once per interval.
        private static readonly List<KeyValuePair<string, Counter>> SortScratch = new();

        private static bool _subscribed;
        private static float _windowStart;

        private void Awake()
        {
            // Track the toggle at runtime so a player can turn diagnostics on
            // mid-session (or the host can push it via config sync) without a restart.
            ApplySubscriptionState();
        }

        private void OnDestroy() => Unsubscribe();

        private IEnumerator Start()
        {
            while (true)
            {
                float interval = Mathf.Max(5f, ModConfig.Global.NetworkDiagnosticsInterval.Value);
                yield return new WaitForSeconds(interval);

                ApplySubscriptionState();

                if (!ModConfig.Global.NetworkDiagnosticsEnabled.Value)
                    continue;
                if (!NetworkClient.active && !NetworkServer.active)
                    continue;

                LogSummary();
            }
        }

        /// <summary>
        /// Subscribes to or unsubscribes from Mirror's diagnostics events to match
        /// the current config value. While unsubscribed Mirror skips the event
        /// entirely, so disabled diagnostics cost nothing per message.
        /// </summary>
        private static void ApplySubscriptionState()
        {
            bool wanted = ModConfig.Global.NetworkDiagnosticsEnabled.Value;

            if (wanted && !_subscribed)
            {
                NetworkDiagnostics.OutMessageEvent += OnOutMessage;
                NetworkDiagnostics.InMessageEvent += OnInMessage;
                _subscribed = true;
                ResetWindow();

                IssaPluginPlugin.Log.LogInfo(
                    "[NetDiag] Network diagnostics enabled. Summaries follow every "
                        + $"{ModConfig.Global.NetworkDiagnosticsInterval.Value:F0}s. "
                        + "Disable Diagnostics/NetworkDiagnosticsEnabled for normal play."
                );
            }
            else if (!wanted && _subscribed)
            {
                Unsubscribe();
            }
        }

        private static void Unsubscribe()
        {
            if (!_subscribed)
                return;

            NetworkDiagnostics.OutMessageEvent -= OnOutMessage;
            NetworkDiagnostics.InMessageEvent -= OnInMessage;
            _subscribed = false;
        }

        // NetworkDiagnostics reports `bytes` for one copy of the message and `count`
        // for how many connections it went to, so total wire cost is bytes * count.
        private static void OnOutMessage(NetworkDiagnostics.MessageInfo info) =>
            Accumulate(
                Outbound,
                info.message.GetType().Name,
                (long)info.bytes * info.count,
                info.count
            );

        private static void OnInMessage(NetworkDiagnostics.MessageInfo info) =>
            Accumulate(Inbound, info.message.GetType().Name, info.bytes, 1);

        private static void Accumulate(
            Dictionary<string, Counter> into,
            string typeName,
            long bytes,
            int sends
        )
        {
            if (!into.TryGetValue(typeName, out var counter))
            {
                counter = new Counter();
                into[typeName] = counter;
            }

            counter.Bytes += bytes;
            counter.Sends += sends;
        }

        private static void ResetWindow()
        {
            Outbound.Clear();
            Inbound.Clear();
            _windowStart = Time.realtimeSinceStartup;
        }

        private static void LogSummary()
        {
            float elapsed = Mathf.Max(0.001f, Time.realtimeSinceStartup - _windowStart);
            string role = NetworkServer.active
                ? (NetworkClient.active ? "host" : "server")
                : "client";

            var sb = new StringBuilder(512);
            sb.Append("[NetDiag] ").Append(role);
            sb.Append(" | window ").Append(elapsed.ToString("F0")).Append("s");
            sb.Append(" | conns ")
                .Append(NetworkServer.active ? NetworkServer.connections.Count : 1);
            sb.Append(" | spawned ")
                .Append(
                    NetworkServer.active ? NetworkServer.spawned.Count : NetworkClient.spawned.Count
                );

            // RTT and jitter are only meaningful on a real remote connection; on the
            // host they measure the loopback and read as ~0.
            if (NetworkClient.active && !NetworkServer.active)
            {
                sb.Append(" | rtt ").Append((NetworkTime.rtt * 1000.0).ToString("F0")).Append("ms");
                sb.Append(" ±")
                    .Append((NetworkTime.rttVariance * 1000.0).ToString("F0"))
                    .Append("ms");

                // bufferTimeMultiplier grows when Mirror's dynamic adjustment detects
                // jitter, directly increasing interpolation delay. A value climbing
                // above the default 2.0 is the clearest signal that a player's
                // "everything feels delayed" complaint is jitter-driven.
                sb.Append(" | buf x")
                    .Append(NetworkClient.bufferTimeMultiplier.ToString("F1"))
                    .Append(" (")
                    .Append(NetworkClient.snapshots.Count)
                    .Append(" snaps)");
            }

            AppendDirection(sb, "OUT", Outbound, elapsed);
            AppendDirection(sb, "IN", Inbound, elapsed);

            IssaPluginPlugin.Log.LogInfo(sb.ToString());
            ResetWindow();
        }

        private static void AppendDirection(
            StringBuilder sb,
            string label,
            Dictionary<string, Counter> counters,
            float elapsed
        )
        {
            long totalBytes = 0;
            foreach (var kv in counters)
                totalBytes += kv.Value.Bytes;

            sb.Append('\n').Append("  ").Append(label).Append(' ');
            sb.Append((totalBytes / elapsed / 1024f).ToString("F1")).Append(" KB/s total");

            if (counters.Count == 0)
                return;

            SortScratch.Clear();
            foreach (var kv in counters)
                SortScratch.Add(kv);
            SortScratch.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));

            int top = Mathf.Clamp(ModConfig.Global.NetworkDiagnosticsTopMessages.Value, 1, 32);
            int shown = Mathf.Min(top, SortScratch.Count);

            for (int i = 0; i < shown; i++)
            {
                var entry = SortScratch[i];
                sb.Append('\n').Append("    ").Append(entry.Key).Append(": ");
                sb.Append((entry.Value.Bytes / elapsed / 1024f).ToString("F2")).Append(" KB/s");
                sb.Append(" over ").Append(entry.Value.Sends).Append(" sends");
                sb.Append(" (").Append(PercentOf(entry.Value.Bytes, totalBytes)).Append("%)");
            }

            if (SortScratch.Count > shown)
                sb.Append('\n').Append("    … ").Append(SortScratch.Count - shown).Append(" more");
        }

        private static string PercentOf(long part, long total) =>
            total <= 0 ? "0" : (100.0 * part / total).ToString("F0");
    }
}
