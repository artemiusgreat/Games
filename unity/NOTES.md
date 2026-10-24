# Unity 6000.0.64f1 (Unity 6.0 LTS) — notes

## Verdict
**Blocked at license activation — exactly the expected wall, reached cleanly
and documented.** Everything up to the gate works from pure CLI on this VM:
Hub installed and operates headless; the editor downloaded *without* Hub and
runs. But the editor refuses all project work — including `-createProject`
and opening this project — until a Unity account activates a license.
Per instructions, no account was created and no credentials were requested.
The slice code (212 lines incl. build script) is complete and hand-reviewed
but **not compile-verified**: the license gate fires before compilation.

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

## Implementation (UnityPoc/Assets — written by hand, incl. scene YAML)
- `Scripts/WorldBuilder.cs` (107 lines): builds everything in `Awake()` —
  procedural terrain `Mesh` (same height function), `GameObject.CreatePrimitive`
  trees (cylinder trunk + sphere canopy) and buildings (cubes) placed on the
  terrain, red capsule NPC. Materials are one-liners in Unity:
  `new Material(Shader.Find("Standard")) { color = ... }` — the thing Flax
  couldn't do in code. Also creates the default DirectionalLight and the
  camera if the scene lacks them, and self-bootstraps via
  `[RuntimeInitializeOnLoadMethod]` so the project runs from ANY scene.
- `Scripts/PlayerController.cs` (62 lines): WASD via the built-in
  Horizontal/Vertical axes (WASD+arrows by default), mouse-X yaw, Shift = run,
  gravity + terrain following by height sampling (Unity has a
  CharacterController, but it needs a terrain MeshCollider; sampling is less
  code, same as Stride/Flax). `--walk-test` arg → scripted forward walk,
  prints `SMOKE start/end` (meters), `Application.Quit()`.
- `Scripts/CameraRig.cs` (16 lines): third-person follow in `LateUpdate`.
- `Editor/BuildScript.cs` (27 lines): CLI player build —
  `Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildLinux
  -logFile build.log`, then `./Build/UnityPoc --walk-test`.
- `Scenes/Main.unity` hand-authored YAML: standard settings blocks + one
  Bootstrap GameObject wired to WorldBuilder via its .meta GUID. (If a future
  editor version dislikes any YAML field, the fallback is 30 s in the GUI:
  new scene → empty GameObject → Add Component → WorldBuilder → save; and the
  RuntimeInitializeOnLoadMethod bootstrap makes even that optional.)
- Terrain winding: Unity is left-handed Y-up; (a,c,b),(b,c,d) faces up.

## On a licensed machine
1. Sign in to Unity Hub once (Personal license activation — the step that
   needs the user) or place a license file; every later step is CLI.
2. `Unity -batchmode -quit -projectPath UnityPoc -executeMethod
   BuildScript.BuildLinux -logFile build.log`
3. `./UnityPoc/Build/UnityPoc --walk-test` → expect SMOKE start/end lines,
   moved ≈ 28–32 m, exit 0.
