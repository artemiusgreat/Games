# Engine POC — same minimal 3D slice in 4 C# engines

Slice spec (identical everywhere): ~100x100 procedural terrain (hills/valleys, code-generated),
2-3 primitive trees, 2 box buildings, 1 static NPC (distinct color), third-person player
(WASD + Shift run, gravity/terrain following). No custom lighting, no animation, no UI, no interaction.
Scope note: MechWarrior-style arena slice (open-world/GTA explicitly out of scope per user).

Task start: 2026-10-24T19:25:17Z (epoch 1792869917)
Machine: Linux VM, 2 cores, 7.9GB RAM, 90GB free disk, Xvfb available, .NET SDK 10.0.112 (~/.dotnet10), SDK 9 (~/.dotnet).

## Summary table (filled as engines complete)

| Engine | Install min | Coding min | Total min | C# lines (hand-written) | Builds? | Runs (smoke)? | Fix iterations | Blockers / steps needing the user |
|---|---|---|---|---|---|---|---|---|
| Godot 4 (.NET) | ~1 | ~2 | ~3 | 232 | YES | YES (headless) | 2 | none — no GUI, no account |
| Stride 4.4 | ~12 | ~7 | ~19 | 253 | YES | YES (Xvfb + llvmpipe) | 3 | none — but see headless Vulkan note |
| Flax | | | | | | | | |
| Unity | | | | | | | | |

## Per-engine log

### Godot 4.7.2 (.NET/mono) — DONE 19:27:46Z
- Install: download 7s (108MB zip from GitHub releases) + unzip/version ~10s → ~1 min total.
- Coding: hand-wrote project.godot, csproj, Main.tscn + 3 C# files (232 lines). dotnet build OK after 1 fix
  (C# bindings: HeightMapShape3D.MapData is float[], not PackedFloat32Array).
- Smoke: headless run needed dotnet on PATH (engine shells out for hostfxr) — env fix, then PASS:
  player moved 28.43 units in 3s at run speed, onFloor=True, followed terrain height down.
- Blockers needing user: none.

### Stride 4.4 — DONE 19:47Z (phase start 19:27:53Z, ~19 min to smoke pass)
- Install ~12 min: dotnet templates (11s) + scaffold + first template build (~4 min, mostly NuGet/asset
  compile) + mesa-vulkan-drivers for headless Vulkan (~10 min, ~9 of it a slow apt mirror; overlaps coding).
  Without a GPU the template build fails: the Skybox asset compiler needs a Vulkan device. llvmpipe fixes it.
- Coding ~7 min: reflection-probed the 4.4 assemblies for exact APIs, hand-authored a 65-line scene YAML
  (template scene was 7,506 lines of level content — deleted along with all template gameplay),
  3 C# files (253 lines). 1 compile-fix round (4 small errors: moved namespaces, removed APIs).
- Smoke PASS under Xvfb+llvmpipe: moved 31.6 units in 3s, terrain-following Y. Runtime-added scripts work.
- Terrain following is height sampling: Stride's heightfield collider is asset-pipeline-oriented with no
  documented code-first path (spec allows sampling where it is less code).
- Blockers needing user: none. No GUI opened, no account.

