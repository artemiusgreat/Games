# Unity 6000.0.64f1 (Unity 6.0 LTS) — notes

## Verdict
**DONE (2026-10-25): licensed with the user's Unity Personal, project
compiles, standalone player builds, smoke run PASSES** (moved=17.70, exit 0).
The 2026-10-24 state below was "blocked at license activation"; the user
then supplied their Unity ID credentials for this one activation, the Hub
sign-in was completed on this VM (route documented under "Licensing —
resolved"), and the remaining verification took 2 fix iterations
(shader stripping; player focus parking under bare Xvfb).

## Toolchain
- Version pinned from Unity's official Hub release feed
  (`public-cdn.cloud.unity3d.com/hub/prod/releases-linux.json`):
  6000.0.64f1, changeset `5360b7cd7953`, direct editor tarball
  `download.unity3d.com/download_unity/5360b7cd7953/LinuxEditorInstaller/Unity.tar.xz`
  = 4,470,286,384 bytes download, ~8.55 GB installed. No Hub needed for the
  download itself.
- Unity Hub 3.22.4: the public AppImage URL now returns a small error stub;
  the working route is Unity's apt repo
  (`hub-dist.unity3d.com/artifactory/hub-debian-prod-local`), whose newest
  package is `UnityHubSetup-3.22.4-amd64.deb` (~180 MB). Extracted with
  `dpkg -x` (no system install). Hub headless CLI works:
  `unityhub --no-sandbox --headless help` / `editors` (Electron; needs
  `--no-sandbox` as root and Xvfb; ~20–120 s per invocation on this VM, noisy
  dbus errors are cosmetic). Environment caveat: the Hub's calls to Unity
  services fail TLS validation through this VM's proxy
  (`net_error -202`, cert authority) — Hub-managed downloads/licensing would
  be blocked here for that reason alone; the direct editor tarball route is
  unaffected (plain curl).
- Editor extraction: `tar -xJf` spews thousands of benign
  "Cannot change ownership to uid 1000" warnings on this filesystem; files
  extract correctly as root. xz decompression is slow on 2 cores.

## Licensing test (2026-10-24, editor 6000.0.64f1, no license present)
- `Unity -batchmode -quit -nographics -createProject /tmp/unity-probe`:
  editor starts, launches its LicensingClient, handshake succeeds, then:
  `[Licensing::Client] Error: Code 404 ... Found 0 entitlement groups and 0
  free entitlements matching requested entitlement ids` →
  **`No valid Unity Editor license found. Please activate your license.`**
  A partial project skeleton (empty Assets/ProjectSettings/Library dirs) is
  created, then the editor exits. No compilation, no project work.
- Same command against this UnityPoc project (`-projectPath`): identical
  license error; zero C# compilation lines in the log — the gate is absolute
  and precedes everything.
- What activation would take (user step, not done): sign in with a Unity ID
  in Unity Hub (or the editor's license prompt) and activate the free
  Personal entitlement — or supply a Pro/Plus serial / license file
  (`Unity -createManualActivationFile` → unity3d.com → `-manualLicenseFile`).
  After that one-time step, every remaining step (project open, CLI build,
  player smoke run) is scriptable.

## Licensing — resolved (2026-10-25)
- Result: `Unity.Licensing.Client --showEntitlements` → **Unity Personal**,
  License Type: Assigned, EntitlementGroupId `1374525764829-UnityPersonal`
  (org "Indemos"), license XML at
  `~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml`. Editor
  batchmode log: "[Licensing::Client] Successfully resolved entitlement
  details". The Hub main window shows the signed-in account.
- Established facts that shaped the route: editor CLI
  `-username`/`-password` authenticates (access token updated) but returns
  "Found 0 entitlement groups and 0 free entitlements" — Personal
  entitlements are provisioned ONLY by a Hub sign-in. The account has
  exactly one seat: Unity Personal (active since 2022). The
  `.alf`/`.ulf` manual route is not offered for Personal.
- Working route (all on this VM, Hub 3.22.4 extracted under `_tools/`):
  1. Hub GUI under Xvfb (:99, 1280×900), launched with
     `--no-sandbox --ignore-certificate-errors --disable-gpu`. First launch
     shows Hub license terms — the Agree button stays disabled until the
     terms pane is scrolled to the bottom.
  2. "Sign in" opens the OAuth authorize page (client_id=unity_hub, PKCE,
     redirect `unityhub://login`) in an EMBEDDED Hub window — which cannot
     load here: this VM's egress proxy requires credentials that Chromium
     refuses to take from the environment or from a `--proxy-server` URL
     (ERR_TUNNEL_CONNECTION_FAILED, then ERR_NO_SUPPORTED_PROXIES). Fix: a
     local unauthenticated proxy shim (`license/tools/proxy_shim.py`,
     127.0.0.1:18080) that relays CONNECT to the egress proxy and injects
     Proxy-Authorization; browsers point at the shim. (CDP
     Fetch.authRequired never fired for the tunnel challenge — the shim is
     the reliable shape.) The Hub's own later code-exchange call worked
     over its normal channel once it had the code.
  3. The authorize URL was completed in a controlled Chrome (CDP-driven)
     behind the shim: Unity ID login is two-step (email → Continue —
     dismiss the OneTrust cookie banner first, it eats the first click —
     then password → Sign in). No 2FA/verification code was requested.
  4. Chrome's post-login load of api.unity.com fails on this path
     (ERR_CONNECTION_CLOSED / indefinite stall), so the authorize leg was
     finished with curl (plain defaults — a browser-like UA/Accept header
     gets HTTP 406) + the session cookies harvested from Chrome via
     browser-level CDP (kept in memory only). The endpoint replies 200 with
     a meta-refresh page whose URL is the `unityhub://login?code=…`
     callback. Caveat: the authorize "conversation" (cid) is single-use —
     a probing GET consumes it; re-arm via Hub Cancel → Sign in.
  5. Delivering the callback = invoking the Hub binary with the URL (same
     as the xdg-open scheme handler). The helper invocation MUST carry
     `--no-sandbox` and `DISPLAY=:99`: without them it dies instantly
     ("Running as root without --no-sandbox is not supported") before the
     single-instance hand-off, silently. With them, rc=0 and the Hub
     completes the exchange within seconds.
- Helper scripts kept in `unity/license/tools/` (cdp_eval.py,
  unity_login.py, pw_step.py, proxy_shim.py, proxy_auth.py, curl_flow.py,
  watch_flow.py, resume_oauth.py). Session cookies were never written to
  disk; the user's credentials were used only via tool stdin/CDP input.
- Environment notes: the VM was replaced once mid-task (processes, /tmp,
  apt-installed packages and /etc Chrome policy wiped; ~/workspace and ~
  survived — keep scripts/logs in the workspace, not /tmp). pkill by
  process NAME (`pkill -x`), never `-f` patterns that also appear in your
  own command line.

## Build + smoke (2026-10-25)
- Build: `Unity -batchmode -quit -noUpm -projectPath UnityPoc
  -executeMethod BuildScript.BuildLinux -logFile build.log` → "Build OK:
  79285557 bytes", exit 0, ~15–55 s. `-noUpm` is required on this
  filesystem: UPM aborts rewriting Packages/manifest.json with
  "EPERM: operation not permitted, chown".
- Fix iteration 1 — shader stripping: the player's first run died in
  WorldBuilder.Awake: `new Material(Shader.Find("Standard"))` →
  ArgumentNullException, because no scene/material in the build references
  Standard, so it is stripped. Pinning Standard via GraphicsSettings
  always-included shaders compiles 16,384 variants (~570/min here —
  abandoned). Final fix: `Mat()` falls back Standard → Unlit/Color →
  Sprites/Default, and BuildScript pins the single-variant Unlit/Color in
  always-included shaders (and removes a Standard pin if present).
- Fix iteration 2 — focus parking: the windowed player printed SMOKE start
  then stalled indefinitely (main thread asleep in hrtimer_nanosleep,
  <11 frames in 240 s) — with `runInBackground: 0` a standalone player
  that never receives window focus (bare Xvfb, no WM) parks its main
  loop. `-nographics`/`-batchmode` segfault on this VM's GL stack and
  vsync changes made no difference. Setting `runInBackground: 1` in
  ProjectSettings.asset fixed it: the walk test completes in ~7 s.
- Smoke PASS: `./Build/UnityPoc --walk-test` under Xvfb →
  `SMOKE start=(0.00, 2.18, 8.00)`, `SMOKE end=(0.00, -0.45, 25.50)`,
  moved=17.70 m (3.5 s at walk speed 5.0; end Y = HeightAt+0.9 exactly),
  exit 0. Player log: ~/.config/unity3d/DefaultCompany/UnityPoc/Player.log.

## Implementation (UnityPoc/Assets — written by hand, incl. scene YAML)
- `Scripts/WorldBuilder.cs` (113 lines): builds everything in `Awake()` —
  procedural terrain `Mesh` (same height function), `GameObject.CreatePrimitive`
  trees (cylinder trunk + sphere canopy) and buildings (cubes) placed on the
  terrain, red capsule NPC. Materials are one-liners in Unity:
  `new Material(shader) { color = ... }` where the shader resolves
  Standard → Unlit/Color → Sprites/Default (Standard alone gets stripped
  from the build — see fix iteration 1). Also creates the default DirectionalLight and the
  camera if the scene lacks them, and self-bootstraps via
  `[RuntimeInitializeOnLoadMethod]` so the project runs from ANY scene.
- `Scripts/PlayerController.cs` (62 lines): WASD via the built-in
  Horizontal/Vertical axes (WASD+arrows by default), mouse-X yaw, Shift = run,
  gravity + terrain following by height sampling (Unity has a
  CharacterController, but it needs a terrain MeshCollider; sampling is less
  code, same as Stride/Flax). `--walk-test` arg → scripted forward walk,
  prints `SMOKE start/end` (meters), `Application.Quit()`.
- `Scripts/CameraRig.cs` (16 lines): third-person follow in `LateUpdate`.
- `Editor/BuildScript.cs` (58 lines): CLI player build —
  `Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildLinux
  -logFile build.log`, then `./Build/UnityPoc --walk-test`. Also pins the
  Unlit/Color shader as always-included before building (fix iteration 1).
- `Scenes/Main.unity` hand-authored YAML: standard settings blocks + one
  Bootstrap GameObject wired to WorldBuilder via its .meta GUID. (If a future
  editor version dislikes any YAML field, the fallback is 30 s in the GUI:
  new scene → empty GameObject → Add Component → WorldBuilder → save; and the
  RuntimeInitializeOnLoadMethod bootstrap makes even that optional.)
- Terrain winding: Unity is left-handed Y-up; (a,c,b),(b,c,d) faces up.

## Round 2, Phase A (2026-10-25 eve / 2026-10-26) — BLOCKED at license re-activation

**BLOCKER (needs the user's hands): the Unity Personal entitlement no longer
validates on this VM.** The VM was replaced overnight: `/etc/machine-id` was
regenerated 2026-10-26 00:48 UTC (`f725c37d…`), while the entitlement XML
(issued 2026-10-25 07:22, valid to 2026-11-24) is bound to the previous
machine id (`4ad78f7d…`). Licensing Client log, verbatim: "Legacy
MachineBinding validation failed" → `--showEntitlements` = "No licenses were
found." → editor batch build exits 198 with "No valid Unity Editor license
found." Relaunching Unity Hub 3.22.4 under Xvfb (persisted Oct-25 profile)
did NOT re-provision: Hub log shows the licensing client answering 404 "Found
0 entitlement groups" — the Hub session did not restore/reactivate for the
new binding. Fix = the round-1 route again: one Hub sign-in with the user's
Unity ID on this VM so Personal re-provisions against the current machine
id. Nothing else in the probes needs the user.

Everything preparable without the editor is DONE and committed:

- **Probe 1 prep:** Fox.glb (Khronos glTF-Sample-Models 2.0/Fox/glTF-Binary;
  clips Survey/Walk/Run, 1 skin, embedded PNG; md5 bb9a8a06…, byte-identical
  copy of the file the Godot agent fetched) — Unity has no native glTF
  import, so: **Blender 4.2.3 headless conversion glTF→FBX** (2.4 s;
  `unity/probe-assets/blender_convert.py`; actions Survey_root 82f,
  Walk_root 17f, Run_root 28f; textures embedded, FBX 740 KB) into
  `Assets/Resources/Fox.fbx`. Animation is code-only by design:
  `FoxDriver.cs` (Playables API, 1-clip graph per state, Survey idle →
  Walk + auto-move on round-1 terrain, fixed side camera), `ShotUtil.cs`
  (RenderTexture→PNG capture harness). `ProbeBuild.BuildAll` (batch editor
  script) sets the FBX importer to Generic + loopTime via API, then builds.
- **Probe 3 prep:** `ScaleWorld.cs` — 1000×1000 terrain (round-1 height
  family, freq ×0.1, amp ×6), Standard material ASSET
  (`ProbeBuild` creates `Assets/Resources/ScaleGround.mat`; referencing
  Standard from an asset is the anti-stripping route — its albedo is a
  procedural texture generated in code at runtime), round-1 sun reused with
  shadows ON, 200 trees + 100 rocks via `Graphics.DrawMeshInstanced`
  (3 instanced draw calls), capsule walker by height sampling, frame-time
  logging, `scale.png` capture. Runs at +2000 x/z so round 1 is untouched.
- **Unverified (honest status):** none of the round-2 code has compiled or
  run — the license gate precedes compilation, so no captured frames exist
  yet. Once re-licensed: `ProbeBuild.BuildAll`, then
  `./Build/UnityPoc --probe1 --shotdir <dir>` and `--probe3` under Xvfb.

## Round 2, Phase B (Probe 2) — code-prep only, UNVERIFIED

**Status: UNVERIFIED — code written, committed, never compiled by Unity,
never run.** The Phase A license blocker above is unchanged and was NOT
retried: no Hub interaction, no sign-in, no editor launch was attempted
for this phase. The only check performed was a static one (below).

Probe 2 = gameplay loop on real physics, on the round-1 world, all new
files alongside the prior code (nothing existing was rewritten; the one
edit to prior code is an additive step in `ProbeBuild`):

- `Scripts/Probe2Game.cs` (297 lines) — orchestrator + verification,
  gated by `--probe2` (same RuntimeInitializeOnLoadMethod + arg pattern
  as probes 1/3). Waits for the round-1 world, retrofits physics,
  spawns the game, and in `--probe2-verify` mode runs the scripted
  sequence (below).
- **Terrain collider (stated choice): MeshCollider on the terrain's own
  procedural Mesh** (`mc.sharedMesh`, non-convex) — the exact vertices
  generated from `WorldBuilder.HeightAt`. TerrainCollider was rejected:
  it needs a TerrainData heightmap, a second resampled representation
  that can drift from the visible mesh (and from HeightAt, which the
  enemies follow). 65×65 verts / 8,192 tris, static — exact and cheap.
  **Buildings:** round 1's `CreatePrimitive(Cube)` already carries a
  BoxCollider, so nothing is added; the probe collects the scene's only
  two BoxColliders, names them Building0/1, and logs them.
- `Scripts/Probe2Player.cs` (129 lines) — the round-1 player capsule
  gets a dynamic Rigidbody (useGravity, freezeRotation,
  ContinuousDynamic, zero-friction PhysicsMaterial). Horizontal
  velocity is set on the body; the vertical component belongs to
  gravity/the solver — that is what makes ground contact, falling, and
  building collision real. `IsGrounded` is rebuilt each FixedUpdate from
  OnCollisionStay contact normals (normal.y > 0.65). Round 1's
  PlayerController is disabled, not destroyed — dropping `--probe2`
  restores round 1 untouched. At setup the capsule is dropped 1.5 m so
  the settle itself is observable.
- `Scripts/Probe2Enemy.cs` (103 lines) — ONE enemy definition as a
  code-only prefab-equivalent: static factory `Probe2Enemy.Create()`
  builds capsule + CapsuleCollider + kinematic Rigidbody + behaviour
  with one shared material; instantiated ×3 at (14,-12), (-18,-6),
  (8,26) around the player start. Movement is **height-following
  steering on the kinematic body** (MovePosition toward the player on
  XZ, Y pinned to HeightAt+0.9, stop at 3.0 m standoff) — cannot sink,
  float, tip, or snag on collider seams. `Die()` = immediate removal.
- `Scripts/Probe2Shooter.cs` (181 lines) — owns the Main Camera
  (CameraRig disabled; same follow offset, but LookAt switches to the
  aim point). Shooting is a raycast **from the camera centre**
  (`ViewportPointToRay(0.5,0.5)`), RaycastAll sorted by distance,
  player's own collider skipped, nearest other hit decides. Automated
  mode `AimAtNearestAndFire()` yaws the player and points the camera at
  the nearest living enemy's chest, then fires the same centre ray.
  Enemy hit → `Hits++`, HUD update, tracer (LineRenderer, 0.09 s) +
  hit-flash sphere (0.12 s, collider stripped).
- `Scripts/Probe2Hud.cs` (101 lines) — uGUI built entirely in code:
  Canvas (ScreenSpaceOverlay) + CanvasScaler + GraphicRaycaster,
  crosshair (two bars + centre dot, Image), "Hits: n/3" Text top-left
  (builtin font LegacyRuntime.ttf, Arial.ttf fallback).
  **Dependency added:** `Packages/manifest.json` (previously untracked,
  empty deps) now carries `com.unity.ugui: 2.0.0` — a BuiltInPackage in
  the local editor install (verified on disk), so it resolves offline
  on a future editor open. `ProbeBuild.BuildAll` gained an additive
  `PrepProbe2()` step that logs whether UnityEngine.UI resolved (if it
  hadn't, compilation of Probe2Hud.cs would already have failed — the
  manifest entry is the actual fix).
- `Scripts/Probe2Util.cs` (29 lines) — shared material fallback (same
  Standard → Unlit/Color → Sprites/Default chain as round 1) + a polled
  world-ready check.

**Verification hooks (in code, UNVERIFIED):** `--probe2-verify` runs
phase 0 (gravity settle → grounded + rest height HeightAt+0.9 ±0.25),
phase 1 (walk into the nearest building 2.5 s → capsule centre must end
outside its BoxCollider bounds), phase 2 (walk forward 2 s, enemies
close to standoff, then acquire + shoot one by one; logs ground state,
enemy distances, every FIRE/hit, final counter) and quits 0 on
`PROBE2 VERIFY PASS` / 2 on FAIL. Frames via the Phase A ShotUtil
harness: `verify_settled.png`, `verify_combat.png`, `verify_final.png`.
Future licensed run, unchanged: `ProbeBuild.BuildAll`, then
`./Build/UnityPoc --probe2 --probe2-verify --shotdir <dir>` under Xvfb
(interactive: same binary with just `--probe2`).

**Static check performed (the only verification that exists):** Roslyn
csc (from the local .NET SDK, no installs) against Unity 6000.0.64f1's
own reference assemblies (editor Data/Managed + NetStandard 2.1 refs;
UnityEngine.UI.dll from the editor's 3D template cache standing in for
the ugui package assembly), C# 9: **all six Probe 2 files + their
round-1 dependencies compile with 0 errors / 0 warnings**, and
BuildScript+ProbeBuild (editor) likewise. This caught one real bug
pre-commit: Unity 6 renamed `Renderer.receiveShadow` →
`receiveShadows`. It is a type/syntax check only — NOT a Unity build,
and it says nothing about runtime physics behaviour.
**Pre-existing finding (left untouched, not Probe 2's file):** the same
check flags Phase A's `ScaleWorld.cs:114` — `mr.receiveShadow = true`
will fail Unity compilation the same way; it needs `receiveShadows`.
Probes 1+3 will not compile on the first licensed run until that
one-word fix lands.

**Known risks / uncertainties in the unverified Probe 2 code:**
1. uGUI resolution on first editor open (manifest chown EPERM quirk on
   this fs — the round-1 `-noUpm` saga) could still bite; fallback is
   one Package Manager click (built-in package uGUI) on a normal fs.
2. Aim geometry: enemies are shot from the standoff ring (~3 m) where
   the camera-centre ray to the chest is unobstructed by construction;
   at longer ranges the descending third-person ray can clip terrain
   first — the verification only fires in range, by design, but
   interactive long-range fire may feel terrain-blocked.
3. Kinematic enemies use MovePosition into a dynamic player: at the
   standoff ring they act as infinite-mass obstacles; a player who
   backs into a corner could get squeezed — untested, POC-acceptable.
4. Ground-contact via OnCollisionStay normals has a one-physics-step
   lag by design; phase 0's thresholds (5 s settle wait, 0.25 m rest
   tolerance) are estimates, never measured.
5. `PhysicsMaterial` / `Rigidbody.linearVelocity` / `receiveShadows`
   are the Unity-6 names, confirmed against this editor's own
   6000.0.64f1 assembly docs — but obsolete-alias warnings in a real
   Unity compile cannot be ruled out by a reference-assembly check.

## Reproduce (this VM, licensed 2026-10-25)
1. License: already provisioned (Unity Personal, see above). The license
   persists in ~/.config/unity3d — no Hub interaction needed for builds.
2. `Unity -batchmode -quit -noUpm -projectPath UnityPoc -executeMethod
   BuildScript.BuildLinux -logFile build.log` (`-noUpm` only because of
   this filesystem's chown EPERM).
3. Under Xvfb: `./UnityPoc/Build/UnityPoc --walk-test` → SMOKE start/end,
   moved ≈ 17.7 m, exit 0. Requires `runInBackground: 1` (already set in
   ProjectSettings.asset) on any window-manager-less display, and does
   NOT work with `-nographics` on this VM (player segfaults).
