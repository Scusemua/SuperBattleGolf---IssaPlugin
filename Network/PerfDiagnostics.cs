// PerfDiagnostics.cs
//
// Opt-in performance diagnostics for investigating the reported FPS drops.
// Everything here is gated behind Global.PerfDiagnosticsEnabled and defaults off.
//
// This is deliberately broad rather than targeted: several narrower theories have
// already been ruled out by measurement, so the point is to capture enough at once
// that the next log answers "which subsystem" without another round trip.
//
// What it reports every interval:
//
//   FRAME    frame time min/avg/max + a slow-frame count, so a stutter shows up as
//            a max/avg gap rather than being averaged away
//   CPU      main thread and render thread time from Unity's own profiler counters
//   GPU      draw calls, setpass calls, triangles, batches — separates "we are
//            drawing too much" from "we are computing too much"
//   MEMORY   GC allocated per frame and total, plus collection count. Allocation
//            churn is the usual cause of periodic hitching in Unity
//   OBJECTS  Mirror spawned-object census grouped by prefab/type name, with a
//            delta since the previous report. This is what identifies a leak: a
//            type whose count only ever climbs
//   PHYSICS  active rigidbody and collider counts, plus a contact estimate
//   MOD      per-item session flags, so the report says what the mod was doing
//
// ProfilerRecorder works in release builds — it reads the same counters the Unity
// Profiler window shows, without needing a development build. That matters here
// because the target is a shipped Steam game.
//
// Cost when disabled: one bool check per interval tick. No recorders are created,
// no counters are read, nothing is allocated.

using System.Collections;
using System.Collections.Generic;
using System.Text;
using Mirror;
using Unity.Profiling;
using UnityEngine;

namespace IssaPlugin.Network
{
    public class PerfDiagnostics : MonoBehaviour
    {
        // ── Frame timing ──────────────────────────────────────────────────────
        private float _worstFrameMs;
        private float _bestFrameMs = float.MaxValue;
        private double _frameMsSum;
        private int _frameCount;
        private int _slowFrameCount;

        /// A frame slower than this counts as a stutter. 20ms ~= below 50 FPS.
        private const float SlowFrameThresholdMs = 20f;

        /// Minimum seconds between the (expensive) physics scene scans.
        private const float PhysicsScanInterval = 120f;
        private float _nextPhysicsScanTime;

        // ── Profiler counters ─────────────────────────────────────────────────
        // Created on enable, disposed on disable. Names come from Unity's built-in
        // profiler stats and are stable across 2021+.
        private ProfilerRecorder _mainThreadTime;
        private ProfilerRecorder _renderThreadTime;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _setPassCalls;
        private ProfilerRecorder _triangles;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _gcAlloc;
        private ProfilerRecorder _gcCount;
        private ProfilerRecorder _usedHeap;
        private bool _recordersActive;

        // ── Spawned-object census ─────────────────────────────────────────────
        // Previous census, so each report can show the change rather than just the
        // level. A leak is a type whose delta is persistently positive.
        private static readonly Dictionary<string, int> PreviousCensus = new();
        private static readonly Dictionary<string, int> CurrentCensus = new();
        private static readonly List<KeyValuePair<string, int>> CensusScratch = new();

        private void OnEnable() => ApplyRecorderState();

        private void OnDisable() => DisposeRecorders();

        private void Update()
        {
            // _recordersActive mirrors the config flag but is a plain bool field, so the
            // disabled case costs one field read per frame rather than a property call
            // into BepInEx. It is refreshed on the interval tick in Start().
            if (!_recordersActive)
                return;

            // Time.unscaledDeltaTime rather than deltaTime so a pause or slow-motion
            // effect does not distort the frame-time picture.
            float ms = Time.unscaledDeltaTime * 1000f;
            _frameMsSum += ms;
            _frameCount++;
            if (ms > _worstFrameMs)
                _worstFrameMs = ms;
            if (ms < _bestFrameMs)
                _bestFrameMs = ms;
            if (ms > SlowFrameThresholdMs)
                _slowFrameCount++;
        }

        private IEnumerator Start()
        {
            while (true)
            {
                float interval = Mathf.Max(5f, ModConfig.Global.PerfDiagnosticsInterval.Value);
                yield return new WaitForSeconds(interval);

                ApplyRecorderState();

                if (!ModConfig.Global.PerfDiagnosticsEnabled.Value)
                    continue;

                // Skip the first report after enabling: the recorders were only just
                // started, so no frames have been sampled and every counter reads zero.
                // Reporting it produces a misleading "0 fps" line.
                if (_frameCount == 0)
                {
                    ResetWindow();
                    continue;
                }

                LogReport();
                ResetWindow();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Profiler recorders
        // ══════════════════════════════════════════════════════════════════════

        private void ApplyRecorderState()
        {
            bool wanted = ModConfig.Global.PerfDiagnosticsEnabled.Value;

            if (wanted && !_recordersActive)
            {
                _mainThreadTime = ProfilerRecorder.StartNew(
                    ProfilerCategory.Internal,
                    "Main Thread",
                    15
                );
                _renderThreadTime = ProfilerRecorder.StartNew(
                    ProfilerCategory.Render,
                    "CPU Main Thread Frame Time"
                );
                _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                _setPassCalls = ProfilerRecorder.StartNew(
                    ProfilerCategory.Render,
                    "SetPass Calls Count"
                );
                _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                // Counter names differ between Unity versions and are resolved by the
                // native profiler, so a wrong name binds nothing and reports n/a
                // forever. The first two attempts here reported gcAlloc=False on
                // 6000.3, so try the known aliases and keep whichever binds.
                _gcAlloc = FirstValid(
                    ProfilerCategory.Memory,
                    "GC Allocated In Frame",
                    "GC Allocated In Frame Count",
                    "Allocated In Frame"
                );
                _gcCount = FirstValid(
                    ProfilerCategory.Memory,
                    "GC Allocation In Frame Count",
                    "GC Allocation In Frame",
                    "Allocation In Frame Count"
                );
                _usedHeap = FirstValid(
                    ProfilerCategory.Memory,
                    "GC Used Memory",
                    "GC Reserved Memory",
                    "System Used Memory"
                );

                _recordersActive = true;

                // Counter names are resolved by the native profiler, so a typo or a
                // counter that does not exist on this platform fails silently and
                // reports n/a forever. Report which ones actually bound so an "n/a"
                // in the data can be told apart from a genuinely zero value.
                IssaPluginPlugin.Log.LogInfo(
                    "[PerfDiag] Performance diagnostics enabled. Reports every "
                        + $"{ModConfig.Global.PerfDiagnosticsInterval.Value:F0}s. "
                        + "Disable Diagnostics/PerfDiagnosticsEnabled for normal play."
                        + "\n  recorders: "
                        + $"mainThread={_mainThreadTime.Valid} "
                        + $"renderThread={_renderThreadTime.Valid} "
                        + $"drawCalls={_drawCalls.Valid} "
                        + $"setPass={_setPassCalls.Valid} "
                        + $"triangles={_triangles.Valid} "
                        + $"batches={_batches.Valid} "
                        + $"gcAlloc={_gcAlloc.Valid} "
                        + $"gcCount={_gcCount.Valid} "
                        + $"usedHeap={_usedHeap.Valid}"
                );
            }
            else if (!wanted && _recordersActive)
            {
                DisposeRecorders();
            }
        }

        private void DisposeRecorders()
        {
            if (!_recordersActive)
                return;

            _mainThreadTime.Dispose();
            _renderThreadTime.Dispose();
            _drawCalls.Dispose();
            _setPassCalls.Dispose();
            _triangles.Dispose();
            _batches.Dispose();
            _gcAlloc.Dispose();
            _gcCount.Dispose();
            _usedHeap.Dispose();

            _recordersActive = false;
        }

        /// Starts the first counter name in <paramref name="names"/> that actually
        /// binds. Unity renames profiler counters between versions and an unknown name
        /// fails silently, so trying the known aliases is more robust than picking one.
        private static ProfilerRecorder FirstValid(ProfilerCategory category, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var r = ProfilerRecorder.StartNew(category, names[i]);
                if (r.Valid)
                    return r;
                r.Dispose();
            }
            return default;
        }

        /// Recorder values are only meaningful once the recorder has collected a
        /// sample; a freshly started or unsupported counter reports 0.
        private static long Last(ProfilerRecorder r) => r.Valid ? r.LastValue : -1;

        private static string Ms(long nanoseconds) =>
            nanoseconds < 0 ? "n/a" : (nanoseconds * 1e-6).ToString("F2") + "ms";

        // ══════════════════════════════════════════════════════════════════════
        //  Report
        // ══════════════════════════════════════════════════════════════════════

        private void LogReport()
        {
            var sb = new StringBuilder(1024);

            AppendFrameSection(sb);
            AppendCpuGpuSection(sb);
            AppendMemorySection(sb);

            // Mod CPU attribution, expressed against the measured main thread time so
            // the report answers "how much of the frame is the mod" directly.
            long mainNs = Last(_mainThreadTime);
            ModCpuProfiler.AppendReport(
                sb,
                _frameCount,
                mainNs > 0 ? mainNs * 1e-6 : -1d,
                ModConfig.Global.PerfDiagnosticsTopObjects.Value
            );
            ModCpuProfiler.Reset();

            AppendObjectSection(sb);
            AppendPhysicsSection(sb);
            AppendModStateSection(sb);

            IssaPluginPlugin.Log.LogInfo(sb.ToString());
        }

        private void AppendFrameSection(StringBuilder sb)
        {
            float avg = _frameCount > 0 ? (float)(_frameMsSum / _frameCount) : 0f;
            float best = _bestFrameMs == float.MaxValue ? 0f : _bestFrameMs;

            // Version stamped on every report: logs arrive from players over days and
            // across builds, and a report is not interpretable without knowing which
            // build produced it.
            sb.Append("[PerfDiag] v").Append(PluginInfo.PLUGIN_VERSION).Append(" FRAME ");
            sb.Append("avg ").Append(avg.ToString("F1")).Append("ms (");
            sb.Append(avg > 0f ? (1000f / avg).ToString("F0") : "0").Append(" fps)");
            sb.Append(" | best ").Append(best.ToString("F1")).Append("ms");
            sb.Append(" | worst ").Append(_worstFrameMs.ToString("F1")).Append("ms");
            sb.Append(" | frames ").Append(_frameCount);
            sb.Append(" | slow(>")
                .Append(SlowFrameThresholdMs.ToString("F0"))
                .Append("ms) ")
                .Append(_slowFrameCount);

            // A large worst/avg ratio means stutter rather than a uniformly low
            // framerate — a different problem with different causes.
            if (avg > 0f)
                sb.Append(" | worst/avg ").Append((_worstFrameMs / avg).ToString("F1")).Append('x');
        }

        private void AppendCpuGpuSection(StringBuilder sb)
        {
            sb.Append('\n').Append("  CPU   ");
            sb.Append("mainThread ").Append(Ms(Last(_mainThreadTime)));
            sb.Append(" | renderThread ").Append(Ms(Last(_renderThreadTime)));

            sb.Append('\n').Append("  GPU   ");
            sb.Append("drawCalls ").Append(Last(_drawCalls));
            sb.Append(" | setPass ").Append(Last(_setPassCalls));
            sb.Append(" | batches ").Append(Last(_batches));
            sb.Append(" | tris ").Append(Last(_triangles));
        }

        private void AppendMemorySection(StringBuilder sb)
        {
            long allocPerFrame = Last(_gcAlloc);
            long heap = Last(_usedHeap);

            sb.Append('\n').Append("  MEM   ");
            sb.Append("gcAllocPerFrame ")
                .Append(allocPerFrame < 0 ? "n/a" : (allocPerFrame / 1024f).ToString("F1") + " KB");
            sb.Append(" | allocCountPerFrame ").Append(Last(_gcCount));
            sb.Append(" | usedHeap ")
                .Append(heap < 0 ? "n/a" : (heap / 1048576f).ToString("F1") + " MB");
            sb.Append(" | monoHeap ")
                .Append((System.GC.GetTotalMemory(false) / 1048576f).ToString("F1"))
                .Append(" MB");
        }

        /// Census of Mirror-spawned objects grouped by name, with the change since
        /// the previous report. This is the leak detector: an object type whose
        /// count climbs every interval and never falls is accumulating.
        private void AppendObjectSection(StringBuilder sb)
        {
            var spawned = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;

            CurrentCensus.Clear();
            foreach (var kv in spawned)
            {
                var identity = kv.Value;
                if (identity == null)
                    continue;

                // Strip Unity's "(Clone)" suffix so instances group under one name.
                string name = identity.gameObject.name;
                int cloneIdx = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
                if (cloneIdx > 0)
                    name = name.Substring(0, cloneIdx);

                CurrentCensus.TryGetValue(name, out int count);
                CurrentCensus[name] = count + 1;
            }

            sb.Append('\n').Append("  OBJ   ");
            sb.Append("spawnedTotal ").Append(spawned.Count);
            sb.Append(" | distinctTypes ").Append(CurrentCensus.Count);

            CensusScratch.Clear();
            foreach (var kv in CurrentCensus)
                CensusScratch.Add(kv);
            CensusScratch.Sort((a, b) => b.Value.CompareTo(a.Value));

            int top = Mathf.Clamp(ModConfig.Global.PerfDiagnosticsTopObjects.Value, 1, 64);
            int shown = Mathf.Min(top, CensusScratch.Count);

            for (int i = 0; i < shown; i++)
            {
                var entry = CensusScratch[i];
                PreviousCensus.TryGetValue(entry.Key, out int before);
                int delta = entry.Value - before;

                sb.Append('\n').Append("      ").Append(entry.Key).Append(": ").Append(entry.Value);
                if (delta != 0)
                    sb.Append(delta > 0 ? " (+" : " (").Append(delta).Append(')');
            }

            if (CensusScratch.Count > shown)
                sb.Append('\n')
                    .Append("      … ")
                    .Append(CensusScratch.Count - shown)
                    .Append(" more types");

            // Report types that vanished entirely, so a despawn burst is visible too.
            foreach (var kv in PreviousCensus)
            {
                if (!CurrentCensus.ContainsKey(kv.Key))
                    sb.Append('\n')
                        .Append("      ")
                        .Append(kv.Key)
                        .Append(": 0 (-")
                        .Append(kv.Value)
                        .Append(')');
            }

            PreviousCensus.Clear();
            foreach (var kv in CurrentCensus)
                PreviousCensus[kv.Key] = kv.Value;
        }

        /// Physics load. Contact count is not directly exposed, so this reports the
        /// inputs to it: how many bodies are awake and how many colliders exist.
        ///
        /// This is the one section that is itself expensive — two full scene scans that
        /// each allocate an array — so it runs on its own longer interval rather than
        /// every report. A diagnostic that perturbs the thing it measures is worse than
        /// no diagnostic, and physics object counts change slowly enough that sampling
        /// them less often loses nothing.
        private void AppendPhysicsSection(StringBuilder sb)
        {
            if (Time.unscaledTime < _nextPhysicsScanTime)
            {
                sb.Append('\n')
                    .Append("  PHYS  (skipped this report — scans every ")
                    .Append(PhysicsScanInterval.ToString("F0"))
                    .Append("s)");
                return;
            }

            _nextPhysicsScanTime = Time.unscaledTime + PhysicsScanInterval;

            var bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            int awake = 0;
            int kinematic = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].isKinematic)
                    kinematic++;
                else if (!bodies[i].IsSleeping())
                    awake++;
            }

            var colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            int triggers = 0;
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i].isTrigger)
                    triggers++;

            sb.Append('\n').Append("  PHYS  ");
            sb.Append("rigidbodies ").Append(bodies.Length);
            sb.Append(" (awake ")
                .Append(awake)
                .Append(", kinematic ")
                .Append(kinematic)
                .Append(')');
            sb.Append(" | colliders ").Append(colliders.Length);
            sb.Append(" (trigger ").Append(triggers).Append(')');
            sb.Append(" | fixedDelta ")
                .Append((Time.fixedDeltaTime * 1000f).ToString("F1"))
                .Append("ms");
        }

        /// Which mod systems were active during this window. Without this the numbers
        /// above have no context — a draw-call spike means something different during
        /// an AC130 session than while standing still.
        private void AppendModStateSection(StringBuilder sb)
        {
            sb.Append('\n').Append("  MOD   ");
            sb.Append("role ")
                .Append(
                    NetworkServer.active ? (NetworkClient.active ? "host" : "server") : "client"
                );
            sb.Append(" | conns ")
                .Append(NetworkServer.active ? NetworkServer.connections.Count : 1);

            if (NetworkClient.active && !NetworkServer.active)
            {
                sb.Append(" | rtt ").Append((NetworkTime.rtt * 1000.0).ToString("F0")).Append("ms");
                sb.Append(" | buf x").Append(NetworkClient.bufferTimeMultiplier.ToString("F1"));
            }

            sb.Append(" | timeScale ").Append(Time.timeScale.ToString("F2"));
            sb.Append(" | vSync ").Append(QualitySettings.vSyncCount);
            sb.Append(" | targetFps ").Append(Application.targetFrameRate);
            sb.Append(" | screen ").Append(Screen.width).Append('x').Append(Screen.height);
        }

        private void ResetWindow()
        {
            _worstFrameMs = 0f;
            _bestFrameMs = float.MaxValue;
            _frameMsSum = 0d;
            _frameCount = 0;
            _slowFrameCount = 0;
        }
    }
}
