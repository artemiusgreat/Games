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
| Stride 4.4 | | | | | | | | |
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

