// ModCpuProfiler.cs
//
// Attributes main-thread CPU time to the mod's own subsystems.
//
// PerfDiagnostics can already say "the main thread is at 14.6ms of a 16.7ms budget",
// which identifies the bottleneck as CPU script execution rather than GPU or network.
// What it cannot say is how much of that time is the mod versus the base game, or
// which part of the mod. Without that, optimisation is guesswork — which is exactly
// what several earlier rounds of this investigation turned out to be.
//
// This closes that gap by timing three buckets:
//
//   PATCHES  every Harmony patch method the mod installs, timed automatically by
//            wrapping them with a profiling prefix/postfix. Covers the ~20 patches
//            that sit on per-frame base-game methods (OnBUpdate, FixedUpdate,
//            ProcessMovementInput, ModifyContactsInternal, ...).
//   OVERLAYS the 24 OnGUI overlays, which each run at least twice per frame.
//   BRIDGES  the NetworkBridge Update/LateUpdate methods, which run per player
//            per frame — so their cost scales with lobby size.
//
// Reported as both absolute ms/frame and as a percentage of the measured main
// thread time, so the output answers "is the mod the problem" directly.
//
// Cost when disabled: the timing wrappers are only installed when the config flag
// is on at startup, so a disabled profiler adds no instructions to any hot path.
// Stopwatch.GetTimestamp is a raw QPC read (a few ns) and there is no allocation
// or logging on the timed paths.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace IssaPlugin.Network
{
    /// <summary>
    /// Accumulates elapsed ticks per named bucket. Written from the timed paths,
    /// read and reset once per report interval.
    /// </summary>
    internal static class ModCpuProfiler
    {
        private sealed class Bucket
        {
            public long Ticks;
            public int Calls;
        }

        private static readonly Dictionary<string, Bucket> Buckets = new();
        private static readonly List<KeyValuePair<string, Bucket>> SortScratch = new();

        /// Guards Buckets.
        ///
        /// Not every timed method runs on the main thread: Unity invokes
        /// Physics.ContactModifyEvent callbacks on a worker thread, and the mod patches
        /// PhysicsManager.ModifyContactsInternal. Mutating a plain Dictionary from two
        /// threads can corrupt its internal state or throw, so a diagnostic must not
        /// take that risk. Contention is negligible — the lock is held for a dictionary
        /// lookup and two integer adds.
        private static readonly object Gate = new();

        /// The thread Unity's main loop runs on, captured at startup. Used only to
        /// report whether off-main-thread samples were seen.
        private static int _mainThreadId;
        private static bool _sawOffThreadSamples;

        internal static void CaptureMainThread() =>
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        /// Set once at startup. When false every Add call returns immediately, and
        /// the Harmony timing wrappers are never installed in the first place.
        internal static bool Enabled;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        internal static void Add(string bucket, long ticks)
        {
            if (!Enabled)
                return;

            bool offThread =
                _mainThreadId != 0
                && System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThreadId;

            lock (Gate)
            {
                if (offThread)
                    _sawOffThreadSamples = true;

                if (!Buckets.TryGetValue(bucket, out var b))
                {
                    b = new Bucket();
                    Buckets[bucket] = b;
                }
                b.Ticks += ticks;
                b.Calls++;
            }
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                foreach (var kv in Buckets)
                {
                    kv.Value.Ticks = 0;
                    kv.Value.Calls = 0;
                }
                _sawOffThreadSamples = false;
            }
        }

        /// <summary>
        /// Appends the per-bucket breakdown. <paramref name="frames"/> is the frame
        /// count for the window so cost can be expressed per frame, which is the only
        /// form comparable against the 16.7ms budget. <paramref name="mainThreadMs"/>
        /// is the measured main thread time, or negative when unavailable.
        /// </summary>
        internal static void AppendReport(
            StringBuilder sb,
            int frames,
            double mainThreadMs,
            int topN
        )
        {
            if (frames <= 0)
                return;

            double totalMs = 0d;
            bool offThread;

            // Snapshot under the lock: worker threads may be adding samples while this
            // report is being built.
            lock (Gate)
            {
                offThread = _sawOffThreadSamples;
                SortScratch.Clear();
                foreach (var kv in Buckets)
                {
                    if (kv.Value.Calls == 0)
                        continue;
                    SortScratch.Add(
                        new KeyValuePair<string, Bucket>(
                            kv.Key,
                            new Bucket { Ticks = kv.Value.Ticks, Calls = kv.Value.Calls }
                        )
                    );
                    totalMs += kv.Value.Ticks * TicksToMs;
                }
            }

            double totalPerFrame = totalMs / frames;

            sb.Append('\n').Append("  MODCPU ");
            sb.Append("total ").Append(totalPerFrame.ToString("F2")).Append("ms/frame");

            // The headline number: what share of the main thread the mod accounts for.
            if (mainThreadMs > 0d)
                sb.Append(" (")
                    .Append((totalPerFrame / mainThreadMs * 100d).ToString("F0"))
                    .Append("% of mainThread ")
                    .Append(mainThreadMs.ToString("F2"))
                    .Append("ms)");

            // Off-main-thread work (physics contact callbacks) is real cost but does not
            // come out of the main thread budget, so the percentage above would be
            // misleading without saying so.
            if (offThread)
                sb.Append(" [includes off-main-thread samples]");

            if (SortScratch.Count == 0)
            {
                sb.Append(" | no samples");
                return;
            }

            SortScratch.Sort((a, b) => b.Value.Ticks.CompareTo(a.Value.Ticks));

            int shown = Mathf.Min(Mathf.Clamp(topN, 1, 64), SortScratch.Count);
            for (int i = 0; i < shown; i++)
            {
                var entry = SortScratch[i];
                double msPerFrame = entry.Value.Ticks * TicksToMs / frames;
                double callsPerFrame = (double)entry.Value.Calls / frames;

                sb.Append('\n').Append("      ").Append(entry.Key).Append(": ");
                sb.Append(msPerFrame.ToString("F3")).Append("ms/frame");
                sb.Append(" over ").Append(callsPerFrame.ToString("F1")).Append(" calls/frame");

                if (totalPerFrame > 0d)
                    sb.Append(" (")
                        .Append((msPerFrame / totalPerFrame * 100d).ToString("F0"))
                        .Append("%)");
            }

            if (SortScratch.Count > shown)
                sb.Append('\n')
                    .Append("      … ")
                    .Append(SortScratch.Count - shown)
                    .Append(" more buckets");
        }
    }

    /// <summary>
    /// Wraps every Harmony patch method the mod installs with timing, so patch cost is
    /// attributed without hand-editing each patch class.
    ///
    /// Harmony lets a patch be patched: for each of our own patch methods we add a
    /// prefix that records a start timestamp and a finalizer that accumulates the
    /// delta. The bucket name is the declaring type, which maps directly back to a file.
    /// </summary>
    internal static class ModCpuProfilerInstaller
    {
        internal static void Install(Harmony harmony, Assembly assembly)
        {
            if (!ModCpuProfiler.Enabled)
                return;

            var prefix = new HarmonyMethod(
                typeof(ModCpuProfilerInstaller).GetMethod(
                    nameof(TimingPrefix),
                    BindingFlags.NonPublic | BindingFlags.Static
                )
            );
            var finalizer = new HarmonyMethod(
                typeof(ModCpuProfilerInstaller).GetMethod(
                    nameof(TimingFinalizer),
                    BindingFlags.NonPublic | BindingFlags.Static
                )
            );

            int wrapped = 0;
            int skipped = 0;

            // GetTypes throws if any type fails to load — for example one referencing a
            // game type that moved in an update. Take the types that did load rather
            // than letting a diagnostic abort plugin startup.
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = Array.FindAll(e.Types, t => t != null);
                IssaPluginPlugin.Log.LogWarning(
                    $"[ModCpu] {e.Types.Length - types.Length} type(s) failed to load; "
                        + "instrumenting the rest."
                );
            }

            foreach (var type in types)
            {
                // Never instrument the profiling machinery itself: wrapping it would
                // recurse, and timing the diagnostics would inflate the very number
                // they exist to measure.
                if (
                    type == typeof(ModCpuProfilerInstaller)
                    || type == typeof(ModCpuProfiler)
                    || type == typeof(PerfDiagnostics)
                    || type == typeof(NetworkTrafficDiagnostics)
                )
                    continue;

                foreach (
                    var method in type.GetMethods(
                        BindingFlags.NonPublic
                            | BindingFlags.Public
                            | BindingFlags.Static
                            | BindingFlags.Instance
                    )
                )
                {
                    if (method.DeclaringType != type || method.IsAbstract)
                        continue;

                    // Two families are worth timing:
                    //   * our Harmony patch bodies, which run inside base-game methods
                    //   * our own Unity per-frame callbacks (overlays draw in OnGUI,
                    //     bridges tick in Update/LateUpdate/FixedUpdate)
                    string n = method.Name;
                    bool isPatchBody =
                        method.IsStatic && (n == "Prefix" || n == "Postfix" || n == "Finalizer");
                    bool isUnityLoop =
                        !method.IsStatic
                        && (
                            n == "OnGUI" || n == "Update" || n == "LateUpdate" || n == "FixedUpdate"
                        );

                    if (!isPatchBody && !isUnityLoop)
                        continue;

                    // Generic definitions cannot be patched.
                    if (method.ContainsGenericParameters)
                        continue;

                    try
                    {
                        harmony.Patch(method, prefix: prefix, finalizer: finalizer);
                        wrapped++;
                    }
                    catch (Exception e)
                    {
                        // A method we cannot wrap is not fatal — it just goes unmeasured.
                        skipped++;
                        IssaPluginPlugin.Log.LogDebug(
                            $"[ModCpu] Could not instrument {type.Name}.{n}: {e.Message}"
                        );
                    }
                }
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[ModCpu] Instrumented {wrapped} patch methods ({skipped} skipped). "
                    + "Timing overhead is included in the reported numbers."
            );
        }

        // Timestamps live in an explicit per-thread stack rather than Harmony's __state.
        //
        // __state cannot be used here: several of the mod's own patches already declare
        // their own __state of a different type (FreezeMovementPatch passes a Vector3
        // from its Prefix to its Postfix). Wrapping those methods with a second patch
        // that also declares __state gives Harmony two conflicting definitions for the
        // same wrapped method, which at best fails to compile the patch and at worst
        // clobbers the value the original patch depends on — silently breaking gameplay
        // rather than merely mismeasuring it.
        //
        // A stack also handles nesting correctly: a timed method can call another timed
        // method, and each Dispose must pop its own start time.
        [ThreadStatic]
        private static Stack<long> _starts;

        private static void TimingPrefix()
        {
            (_starts ??= new Stack<long>()).Push(Stopwatch.GetTimestamp());
        }

        /// Finalizer rather than a plain postfix: a finalizer runs even when the wrapped
        /// method throws. With a postfix, an exception would skip the pop and leave a
        /// stale timestamp on the stack, so every subsequent measurement on this thread
        /// would be attributed to the wrong method.
        ///
        /// Returning null rethrows the original exception unchanged, so this observes
        /// without altering behaviour.
        private static Exception TimingFinalizer(MethodBase __originalMethod, Exception __exception)
        {
            RecordElapsed(__originalMethod);
            return __exception;
        }

        private static void RecordElapsed(MethodBase __originalMethod)
        {
            // Defensive: if a prefix was skipped for any reason the stack can be empty.
            if (_starts == null || _starts.Count == 0)
                return;

            long delta = Stopwatch.GetTimestamp() - _starts.Pop();

            string type = __originalMethod.DeclaringType?.Name ?? "unknown";

            // Prefix the bucket so the report groups by kind: a Unity callback on one
            // of our own components is a different cost centre from a patch body
            // running inside a base-game method, even when both live in the same class.
            string kind = __originalMethod.IsStatic ? "patch:" : "loop:";

            ModCpuProfiler.Add(kind + type + "." + __originalMethod.Name, delta);
        }
    }

    /// <summary>
    /// Times a scope and books it to a named bucket. Used for the hand-instrumented
    /// buckets (overlays, bridges) where a Harmony wrapper does not apply.
    ///
    /// Struct + IDisposable so a using-block compiles to no allocation.
    /// </summary>
    internal readonly struct ModCpuScope : IDisposable
    {
        private readonly string _bucket;
        private readonly long _start;

        internal ModCpuScope(string bucket)
        {
            _bucket = bucket;
            _start = ModCpuProfiler.Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        public void Dispose()
        {
            if (_start == 0L)
                return;
            ModCpuProfiler.Add(_bucket, Stopwatch.GetTimestamp() - _start);
        }
    }
}
