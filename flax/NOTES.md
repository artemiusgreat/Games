# Flax 1.13.7012 — notes

## Verdict
Code complete and **compiling clean for both Game and GameEditor targets** via
Flax.Build, but **runtime smoke test impossible on this VM**: the Flax editor
crashes at startup here, and cooking (required before FlaxGame will run a
project) is an editor function.

## Toolchain
- GitHub releases carry source only; binaries come from flaxengine.com direct
  links: `FlaxEditorLinux.zip` (232 MB) + `FlaxLinuxTools.zip` (138 MB, the
  Linux game runtime: `Binaries/Game/x64/Release/FlaxGame` + libFlaxEngine.so).
  Extracted to `_tools/flax-1.13/` (617 MB). No account, no installer.
- Requirements met: .NET 10 SDK, Vulkan (llvmpipe), libcurl present.

## BLOCKER — editor crashes at startup on this VM
`FlaxEditor` (tried Development, Release, and Debug configs; with/without Xvfb;
`-headless`, `-null` renderer, `-CoreLimit=1`, `MALLOC_CHECK_=0`, GC heap caps)
aborts within ~0.03–1.5 s with glibc heap assertions:
`malloc(): corrupted top size` / `malloc(): invalid size (unsorted)` /
`Fatal glibc error: malloc.c:2599 (sysmalloc)` — during engine Content asset
registration, right after hostfxr load. `ldd` clean (no missing libs).
VM context: 7.9 GB RAM total but ~7.5 GB already committed by other tenants
(~0.5 GB available, ~1 GB of the 4 GB commit limit free; FlaxGame itself
reported "Total Used Physical Memory: 7.63 Gb (98%)"). The editor is by far the
heaviest toolchain of the four engines; on a normal machine (incl. the user's
own) this step is a non-event. Project creation via `FlaxEditor -new` was
therefore impossible; scaffolded from the official BasicTemplate sample instead
(copy + rename, scene GUIDs kept consistent with GameSettings).

## Build path that DOES work without the editor
- `dotnet Flax.Build.dll -build -project=FlaxPoc.flaxproj -platform=Linux
  -configuration=Development` compiles the game C# in ~7–15 s.
- Layout gap bridged with symlinks: the build tool resolves the game engine
  assembly at `Source/Platforms/Linux/Binaries/Game/x64/Development/
  FlaxEngine.CSharp.dll`, but the zip ships it at `Binaries/Game/x64/Release/`.
  Symlinked the Release assembly into the expected Development path.
- Flax.Build swallows csc diagnostics; rerun csc with the generated
  `Cache/Intermediate/.../Game.CSharp.response` file to see real errors.
- Template bug found: the stock `PerfCounter.cs` uses editor-only APIs
  (`ProfilerGPU`, `ProfilingTools`) and fails the Game target on a CLI build.
  Deleted it (its scene entry was already removed).
- `Flax.Build -deploy` silently no-ops without the editor; `FlaxGame -project
  <dir>` → "Cannot load game" (needs cooked content).

## Implementation (Source/Game/WorldBuilder.cs, single script)
- Flax units are **centimeters**: slice authored in meters, scaled ×100 (U).
- Scene is plain JSON: kept the template's Scene/Sky/SkyLight/EnvironmentProbe/
  DirectionalLight, dropped floor/cube/UI/mobile entries, added an EmptyActor
  "Bootstrap" + a `WorldBuilder` script entry (`{"TypeName":"WorldBuilder",
  "ParentID":<actor id>,"V":{}}`).
- All geometry is runtime code: terrain via `Content.CreateVirtualAsset<Model>()`
  + `Mesh.UpdateMesh` (docs HOWTO), primitives via my own box/lathe mesh
  builders (unit box, cylinder, cone).
- Color: one parameterized base material (copied from the FPS template sample)
  + per-object `MaterialInstance` tints in code (`Entries[0].Material` accepts
  `MaterialBase`). The tint loop sets any parameter named Color/Base Color/
  Albedo — **the one runtime-unverified bit**: if the shipped base material
  names its color parameter differently, objects render in its default color
  until the name is matched.
- Terrain winding: Flax is left-handed and the docs specify **clockwise**
  triangles; used (a,b,c),(b,d,c). If the terrain ever renders inside-out on a
  real machine, flip the index order — flagged as the second unverified bit.
- Player: height sampling (same call as Stride — Flax has a CharacterController
  but it needs cooked collider surfaces; sampling is less code and zero-risk).
  Named input axes: `Input.GetAxis("Horizontal"/"Vertical"/"Mouse X")`;
  run = `KeyboardKeys.Shift`; exit = `Engine.RequestExit()`.
- Walk test: launch the cooked game with `--walk-test` → prints
  `SMOKE start=...` / `SMOKE end=... moved=...` (meters) and exits.
  On a healthy machine: open the project in the editor → Build & Run, or
  `FlaxEditor -project FlaxPoc -headless -build "Development.Linux"`.

## To run this on a normal machine
1. Install Flax 1.13 (launcher or the same zips).
2. If building via CLI, recreate the symlink bridge above (or just open the
   project in the editor once — the editor generates everything itself).
3. Build & Run; for the smoke test pass `--walk-test`.
