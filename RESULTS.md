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
| Flax 1.13 | ~8 | ~5 | ~13 | 237 | code YES (Flax.Build); cook NO (needs editor) | NO — editor crashes on this VM | 3 | Editor native crash at startup (heap assertion) on this memory-starved VM; zip layout gap for CLI builds (symlink bridge). No account/GUI reached — on a normal machine: open project in editor, Build & Run |
| Unity 6000.0.64f1 | ~68 | ~10 | ~78 + ~70 verification (2026-10-25) | 249 | YES | YES (windowed, Xvfb) | 2 | None left — Personal license provisioned via Hub sign-in on this VM (see log). Quirks: editor builds here need `-noUpm` (UPM chown EPERM on this fs); player needs `runInBackground` under bare Xvfb |

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


## Flax 1.13 — log
- 19:47:39Z phase start (epoch 1792871259) → 20:00:57Z done (epoch 1792872057). Total ≈ 13 min.
- Install ≈ 8 min: editor+tools zips (370 MB) and FlaxSamples clone (309 MB) downloaded in
  ~4 min; ~4 min diagnosing the editor startup crash (all configs/flags) and bridging the
  CLI build layout gap with symlinks.
- Coding ≈ 5 min: scene JSON surgery (python), one 237-line script, 1 compile-fix round
  (MaterialBase typing, KeyboardKeys.Shift, deleted template PerfCounter.cs).
- Build: game C# compiles clean for Game + GameEditor targets via Flax.Build (~10 s).
- Smoke: NOT RUN — editor crashes at startup on this VM (glibc heap assertion during
  engine Content scan; VM has ~0.5 GB free RAM of 7.9 GB), and FlaxGame requires cooked
  content, which only the editor produces. Fix iterations: 3. User steps needed: none here.
  Full detail: flax/NOTES.md.

## Unity 6000.0.64f1 — log
- 20:01:04Z phase start (epoch 1792872064) → 21:19:14Z done (epoch 1792875554). Total ≈ 78 min wall.
- Install ≈ 68 min, mostly PASSIVE waiting at this VM's speeds: editor tarball 4.47 GB
  downloaded at ~1.4–4.4 MB/s (~26 min, shared with the Hub download) and xz extraction
  of 8.55 GB on 2 cores (~35 min). Active toolchain work was ~15 min: version pinning via
  Unity's official Hub release feed, Hub 3.22.4 from Unity's apt-repo .deb (the public
  AppImage URL now returns an error stub), Hub headless CLI verified working
  (`--no-sandbox --headless help`/`editors`; its Unity-service TLS fails through this
  VM's proxy, net_error -202).
- Coding ≈ 10 min (done while the editor downloaded): 185 lines of game C# + 27-line
  editor build script, hand-authored scene YAML + .meta files, project skeleton.
- License probes (2 runs, ~8 s each): editor + LicensingClient start fine, find
  0 entitlements, and refuse everything with "No valid Unity Editor license found.
  Please activate your license." — for `-createProject` and for opening UnityPoc alike;
  compilation is never reached. Fix iterations: 0 (no code ever compiled).
- User steps needed: ONE — Unity ID sign-in + Personal license activation. Full
  detail: unity/NOTES.md.

### Unity — verification phase (2026-10-25, ~70 min wall)
- **Licensing: RESOLVED.** The user's existing Unity Personal seat is now
  provisioned on this VM: Licensing Client reports Unity Personal (Assigned,
  org "Indemos"), license XML written, editor log shows "Successfully resolved
  entitlement details". Route that worked: Hub GUI under Xvfb → Sign In opens
  an embedded OAuth window that cannot load through this VM's egress proxy,
  so the Unity ID login was completed in a controlled Chrome behind a local
  unauthenticated proxy shim (Chromium refuses the proxy's embedded
  credentials: ERR_TUNNEL_CONNECTION_FAILED / ERR_NO_SUPPORTED_PROXIES; the
  shim injects them). Chrome's load of the api.unity.com authorize leg also
  fails/stalls here (ERR_CONNECTION_CLOSED), so that leg was finished with
  curl + the browser's session cookies — the endpoint answers with a
  meta-refresh page carrying the `unityhub://login` callback; delivering that
  URL to the running Hub (the helper invocation needs `--no-sandbox` and a
  DISPLAY or it dies before the single-instance hand-off) completed the code
  exchange. Confirmed en route: editor CLI `-username`/`-password` alone
  authenticates but provisions 0 entitlements — Personal comes only via a
  Hub sign-in. The ~70 min was mostly environment: one full VM replacement
  mid-task (processes + /tmp + apt state wiped), plus the proxy workarounds;
  active build/fix work was ~15 min.
- Build: YES — batchmode player build OK (79,285,557 bytes). On this
  filesystem the editor needs `-noUpm`: UPM fails rewriting
  Packages/manifest.json (chown EPERM).
- Fix iterations: 2.
  1. Shader stripping: runtime `Shader.Find("Standard")` is null in the
     player because no build content references Standard. Pinning Standard
     as always-included compiles 16,384 variants (~30 min — abandoned);
     final fix: `WorldBuilder.Mat` falls back Standard → Unlit/Color →
     Sprites/Default, and BuildScript pins the single-variant Unlit/Color.
  2. Player parks without window focus: with `runInBackground: 0` the
     standalone player never gains focus under bare Xvfb (no WM) and its
     main loop stalls after Start (fewer than 11 frames in 240 s, main
     thread asleep). `runInBackground: 1` → the walk completes in ~7 s.
- Smoke: PASS (windowed under Xvfb) — `SMOKE start=(0.00, 2.18, 8.00)` →
  `SMOKE end=(0.00, -0.45, 25.50)`, **moved=17.70**, exit 0. The 3.5 s test
  runs at walk speed 5.0 (Unity's harness walks; Godot/Stride figures used
  run speed — not like-for-like). End Y matches HeightAt(x,z)+0.9 exactly.
  Note: the player segfaults with `-nographics`/`-batchmode` on this VM's
  GL stack; windowed Xvfb is the working route.
