# Performance

## Reference hardware

| | |
|---|---|
| CPU | AMD Ryzen 5 2600, 6 cores / 12 threads |
| GPU | NVIDIA GeForce RTX 4060 (discovered when the benchmark ran with rendering on; wasn't known at planning time -- see PLAN.md's environment table, which only had CPU info) |
| RAM | ~16 GiB |
| OS | Windows 10 Enterprise x64 |
| Renderer | Godot Forward+ / D3D12 |
| Target | 30 FPS floor (33.3ms/frame), 60 FPS reach, per the brief |

## Benchmark methodology

`Scripts/Scheduling/BenchmarkRunner.cs`, triggered by setting the `CNC_BENCHMARK`
environment variable before launching the project. Runs entirely synthetically
(no LinuxCNC/bridge needed, so it's reproducible without the WSL2 setup):
every frame, for 1800 frames, it:
- Sweeps `LatheRadiusProfile` across the whole stock length with an
  oscillating depth (0-20mm), guaranteeing real mesh-dirtying work every
  single frame ("heavy cutting").
- Forces a sustained `Continuous`-type chip formation into `ChipSpawner`
  every frame ("many chips" -- this keeps the chip pool at or near its cap of
  250 the whole run, not just briefly).
- Records each frame's `delta`, then reports percentiles and scheduler stats.

Two runs: `--headless` (CPU-only, no rendering -- isolates the cost of the
material/chip/scheduler logic itself) and normal (with rendering, on this
machine's actual GPU).

## Results (2026-11-13)

| | headless (logic only) | rendered |
|---|---|---|
| avg frame time | 6.99 ms | 13.46 ms |
| p50 | 6.90 ms | 13.33 ms |
| p95 | 6.94 ms | 13.33 ms |
| p99 | 6.94 ms | 13.64 ms |
| implied avg FPS | 143 | 74.3 |
| implied p95 FPS | 144 | 75.0 |
| High-priority (mesh) scheduler time used | ~0.00 ms | ~0.00 ms |
| Normal-priority (chips) scheduler time used | ~0.02 ms | ~0.03 ms |
| Low-priority drops | 0 | 0 |

**Both comfortably clear the 30 FPS floor and the 60 FPS reach target**, with
large headroom in the logic layer itself (material sweep + chip tick + mesh
chunk rebuild together cost well under 1ms of the scheduler's measured budget
per frame in both runs -- the scheduler's High/Normal budgets of 4ms/2ms were
never close to exhausted, hence zero adaptive shrinking and zero drops).

**Caveat, stated plainly**: the rendered run's frame times cluster suspiciously
exactly around 13.333ms = 1000/75, which strongly suggests the display's
vsync (apparently a 75Hz refresh rate monitor) capped the result rather than
this being the actual achievable ceiling with rendering on. The *true*
rendered-pipeline ceiling on this GPU was not measured (would need vsync
disabled via project display settings for the benchmark run specifically).
Given the headless logic-only ceiling is ~143 FPS and the vsync-capped
rendered run still hit the display's cap with room to spare (min observed
frame time 0.603ms), it's a safe inference that the true ceiling is
comfortably above 75 FPS on this hardware for this workload, but "comfortably
above 75" was not directly measured -- recorded here as a known gap rather
than overstating the result.

## What this does NOT measure

- LinuxCNC/bridge latency and jitter under this same load (the benchmark is
  synthetic and bridge-free by design, for reproducibility). The real
  bridge's state stream was separately load-tested in Phase 0/1 at 100 Hz for
  8 seconds with zero packet loss (see PLAN.md) -- a different, narrower test
  than this one.
- Behavior as the chip pool genuinely fills past its cap under organic
  (non-forced) cutting, or across a full real multi-minute program run.
- GPU-side cost breakdown (draw calls, fill rate) -- only wall-clock frame
  time was measured.
- True uncapped rendered frame time (see the vsync caveat above).

## Scheduler design note

The job scheduler (`Scripts/Scheduling/JobScheduler.cs`) coordinates CPU time
budget *within* the main thread (time-slicing work across frames so nothing
spikes frame time) -- it does not dispatch work onto Godot's `WorkerThreadPool`
for true background-thread execution. This was a deliberate choice, not an
oversight: the measured cost of mesh regeneration and chip simulation (under
1ms combined, even under this benchmark's deliberately heavy synthetic load)
does not justify the added complexity and thread-safety surface of moving
mesh generation to a worker thread with double-buffered commit. If a future,
much heavier scene (many more chips, a finer mesh resolution, a mill's
heightmap model) pushes High/Normal time usage close to their budgets, moving
chunk mesh generation (the `SurfaceTool` build, not the final
`MeshInstance3D.Mesh =` assignment) to a background thread is the natural next
step, and the scheduler's priority/budget accounting is already structured to
make that swap straightforward to measure before/after.

## GDExtension (C++)

Not built. Per the brief's own instruction ("base this decision on profiling
data, not assumptions") and the measurements above, C# comfortably handles
every hot path measured so far with large headroom -- there is currently no
profiling evidence that a GDExtension module would move any user-visible
number. Revisit if a future phase's profiling shows otherwise.
