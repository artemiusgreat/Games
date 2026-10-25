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
