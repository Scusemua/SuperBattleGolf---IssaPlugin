**This is a diagnostic build, not a normal release.**

It writes performance and network diagnostics to `BepInEx/LogOutput.log`, and it runs
slightly slower than a normal build **on purpose** — the profiling instrumentation
itself has a cost. Do not judge this build's framerate as representative.

### If you are helping debug the FPS / lag reports

1. Install this build and play as you normally would.
2. Play until you notice the problem.
3. Send the whole `BepInEx/LogOutput.log` file — please **attach** it rather than
   pasting it, as it will be large.

The log will contain lines tagged `[PerfDiag]`, `[NetDiag]`, and `[ModCpu]`. Those are
what identify where the time is going.

### Turning the diagnostics off

Set these to `false` under `[Diagnostics]` in
`BepInEx/config/com.scusemua.issaplugin.cfg`:

- `PerfDiagnosticsEnabled`
- `ModCpuProfilingEnabled` (requires a game restart)
- `NetworkDiagnosticsEnabled`

### New cosmetic toggles

These are all enabled by default and are purely visual. Turning them off may help on
lower-end machines, and it is useful to know whether they make a difference:

- `Donut/OverlayEnabled` — the Donut piloting overlay
- `Diagnostics/BomberOverlayEnabled` — stealth bomber / predator missile overlay
- `Diagnostics/PlayerBoxOverlayEnabled` — player target boxes and name labels
- `Diagnostics/CustomVfxEnabled` — all custom particle and trail effects

### For regular play

Use the latest **normal** release instead of this one.
