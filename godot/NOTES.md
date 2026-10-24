# Godot 4.7.2 (.NET/mono) — notes

- Toolchain: official mono Linux zip from GitHub releases (108MB), unzip, run. No installer, no account.
- Project scaffolded by hand: project.godot + GodotPoc.csproj (Godot.NET.Sdk/4.7.2, net8.0) + Main.tscn (one Node3D + Main.cs). Everything else built in code at runtime.
- Build: `dotnet build` (PATH needs ~/.dotnet10). First build 25s (NuGet restore), incremental 3s.
- Fix iterations: 2
  1. Compile: `PackedFloat32Array` not found — in the C# bindings `HeightMapShape3D.MapData` is a plain `float[]`. One-line fix.
  2. Runtime env: headless run crashed (SIGSEGV after hostfxr error) because `dotnet` was not on PATH for the engine process — Godot shells out to `dotnet` to locate hostfxr. Fixed by exporting PATH; no code change.
- Smoke: `godot --headless --path . -- --walk-test` → SMOKE start=(0, 2.78, 8) end=(-0.02, 0.52, -20.34) moved=28.43 onFloor=True. Player ran forward 3s at 9.5 u/s, stayed glued to terrain. PASS.
- Steps needing the user: NONE. No GUI was opened at any point.
