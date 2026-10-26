# Godot 4.7.2 (.NET/mono) — notes

- Toolchain: official mono Linux zip from GitHub releases (108MB), unzip, run. No installer, no account.
- Project scaffolded by hand: project.godot + GodotPoc.csproj (Godot.NET.Sdk/4.7.2, net8.0) + Main.tscn (one Node3D + Main.cs). Everything else built in code at runtime.
- Build: `dotnet build` (PATH needs ~/.dotnet10). First build 25s (NuGet restore), incremental 3s.
- Fix iterations: 2
  1. Compile: `PackedFloat32Array` not found — in the C# bindings `HeightMapShape3D.MapData` is a plain `float[]`. One-line fix.
  2. Runtime env: headless run crashed (SIGSEGV after hostfxr error) because `dotnet` was not on PATH for the engine process — Godot shells out to `dotnet` to locate hostfxr. Fixed by exporting PATH; no code change.
- Smoke: `godot --headless --path . -- --walk-test` → SMOKE start=(0, 2.78, 8) end=(-0.02, 0.52, -20.34) moved=28.43 onFloor=True. Player ran forward 3s at 9.5 u/s, stayed glued to terrain. PASS.
- Steps needing the user: NONE. No GUI was opened at any point.

## Round 2, Phase B — Probe 2: physics gameplay loop (GameProbe2.tscn, --probe2-test)

- New files, prior probes untouched: GameProbe2.cs (world + HUD + harness), Probe2Player.cs (physics player + shooter), Enemy.cs + Enemy.tscn (ONE PackedScene enemy definition, instantiated x3), GameProbe2.tscn. Also playable manually (no arg: WASD/Shift, Space/LMB shoots).
- Real physics throughout: player capsule (CharacterBody3D, gravity + MoveAndSlide) vs HeightMapShape3D terrain (101x101 = 10,201 cells, setup 55.26 ms incl. data fill) and both buildings — round-1 buildings had NO colliders; added StaticBody3D + BoxShape3D, box extended 2 units down so slope can't open a gap under a wall. Enemies are physics-driven too (CharacterBody3D + gravity + MoveAndSlide, NO height sampling), steer to player, hold at 4.0 standoff.
- Scripted building test: player sprints +X at building 2 (face x=19.00) for 1.2 s → stopped at x=18.60 (= face − capsule radius 0.4 exactly), onFloor=True. PASS.
- Shooting: raycast from the camera through screen center (player RID excluded); in auto mode the camera aims at the nearest enemy's chest, player walks to ≤24 units, fires every 0.45 s. Tracer (thin unshaded cylinder, 90 ms) + orange death puff on kill. All 3 enemies destroyed, hunt time 3.8 s, final counter Hits: 3/3, HUD is code-built CanvasLayer (dark panel + label, "+" crosshair via CenterContainer).
- Proof frames (Xvfb + opengl3/llvmpipe, 1280x720): probe2-frames/frame_early.png — Hits: 0/3, all 3 orange enemies visible, crosshair sitting on the nearest enemy; probe2-frames/frame_late.png — Hits: 3/3, no enemies in view. Pixel-verified twice: in-engine counter-region diff 73/7,238 sampled px; external PIL: 289 changed px, ALL inside the digit box (the 0→3 glyph change).
- Logs show onFloor=True for every hunt sample (single False at t=0.3 is the post-teleport spawn drop, landed by t=0.8). Process RSS during run: 329 MB. Fix iterations: 0 — build clean and full PASS on the first run. Round-1 walk-test re-run after: identical SMOKE numbers, no regression.
- Run: `xvfb-run -a godot --path . res://GameProbe2.tscn --rendering-driver opengl3 --resolution 1280x720 -- --probe2-test` (exit 0 = PASS).
