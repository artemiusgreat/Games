# Stride 4.4.3 (templates) / 4.4.0 (engine) — notes

- Toolchain: `dotnet new install Stride.Templates.Games.Starters::4.4.3` (11s), scaffolded from
  `stride-thirdpersonplatformer` (no empty template exists). Template ships a Linux launcher project.
- HEADLESS TAX: the template's first build failed in the AssetCompiler — the Skybox compiler needs a
  Vulkan device and this VM has no GPU. Fixed by installing mesa-vulkan-drivers (lavapipe/llvmpipe);
  apt took ~10 min, almost all of it a slow mirror during `apt-get update`. After that, 964/968 asset
  steps had already succeeded; with llvmpipe the full build passes in ~18s.
  On a Windows machine with a real GPU this step does not exist.
- Approach: deleted ALL template gameplay (scripts, level prefabs/models/materials/animations) and the
  7,506-line MainScene; hand-authored a 65-line MainScene.sdscene (copied light + camera component
  blocks verbatim from the template; one "Game" entity hosting my WorldBuilder script). GameSettings +
  GraphicsCompositor + Skybox assets kept. The YAML dialect is imitable but this was the fiddliest
  scene setup of the four engines.
- Code: WorldBuilder (StartupScript) builds everything at runtime: terrain mesh via
  Buffer.Vertex/Index + VertexPositionNormalColor, primitives via GeometricPrimitive.*.New +
  ToMeshDraw(), materials via Material.New + MaterialDescriptor (ComputeColor diffuse).
  PlayerController is a SyncScript; scripts added at runtime (player.Add(script)) activate correctly.
- Terrain following = HEIGHT SAMPLING, not physics: Stride's HeightfieldColliderShapeDesc is built
  around editor-baked heightmap assets (IHeightStickArraySource); no documented code-first path was
  found. The slice spec allows sampling where it is less code — here it clearly was. The engine's
  CharacterComponent (SetVelocity API) exists but needs a collider surface to stand on.
- API archaeology cost: GeometricPrimitive moved namespaces (Stride.Graphics.GeometricPrimitives),
  TransformComponent.RotationEuler and IGame.Exit() don't exist in 4.4, System.Buffer ambiguity.
  I dumped the shipped assemblies with a reflection probe instead of guessing — ~2 min, effective.
- Smoke: `xvfb-run -a ./StridePoc.Linux --walk-test` with DOTNET_ROOT=~/.dotnet10 and
  VK_ICD_FILENAMES=lvp_icd.json. SMOKE start=(0,2.18,8) end=(0,1.27,-23.58) moved=31.60. PASS.
  (Runtime compiles shaders on first run ~10s; ALSA errors are the missing sound card, harmless.)
- Steps needing the user: NONE (no Game Studio GUI, no account). Root/apt was available on this VM.

## Round 2 (2026-10-25/26) — Probe 1: animated asset import + playback — PASS
- Asset: Khronos glTF sample "Fox" (Survey/Walk/Run, 24 joints, 1 skinned mesh,
  embedded-atlas texture) — sources in StridePoc/Resources/Fox/ (Fox.gltf + Fox.bin + Texture.png).
- DIRECT glTF IMPORT WORKS — no FBX conversion needed. Stride's importer is
  Assimp-based (ThreeDAssetImporter, Silk.NET.Assimp / Ultz.Native.Assimp 6.0.2)
  and reads .gltf natively, producing Model/Skeleton/Animation/Material/Texture
  assets with correct clip stacks (Survey 3.417s, Walk 0.708s, Run 1.158s).
  The .glb variant FAILS: ImportTextures throws ArgumentNullException because a
  GLB-embedded texture has no file location — use the split .gltf form.
- Descriptor authoring WITHOUT Game Studio: possible, via Stride's own APIs in a
  small console tool (probe2-tools/ImportTool): force-load all Stride assemblies
  (AssemblyScan registration), ThreeDAssetImporter.Import(...), then
  AssetFileSerializer.Save(...) per item. Emitted YAML is exactly the Game Studio
  dialect (!Model/!Skeleton/!Animation/...). Package.Load(...) standalone FAILED
  ("Error while pre-loading package") — the importer route does not need it.
  Extension note: model assets are .sdm3d in 4.4, not .sdmodel.
- TWO build gotchas (assets silently NOT compiled otherwise, Content.Load throws
  "asset could not be found" at runtime): (1) code-loaded assets must be listed
  in StridePoc.Game.sdpkg RootAssets (format `- <guid>:<Location>` per asset,
  learned by serializing a synthetic Package) — nothing in the scene references
  them, so there is no dependency closure; (2) the game csproj must reference
  Stride.Assets.Models or the asset-type registrations are absent. The Stride
  asset cache (obj/stride) also had to be deleted once for new files to appear.
- Playback: AnimationComponent Animations dict + Play("Survey") /
  Crossfade("Walk", 0.25s) from a SyncScript driver (FoxProbe.cs, 125 lines);
  Survey is Fox's stationary clip (it has no true idle). Fox scaled 0.012
  (glTF units are ~cm: 79 units tall -> ~0.95 m).
- PROOF (rendered frames, OS-level Xvfb root capture — probe2-tools/capture):
  probe-frames/probe1-idle.png (Survey stance, stationary) vs probe1-walk.png
  (Walk, mid-gait): fox region meanAbsDiff 21.95, 19.0% of pixels changed;
  walk vs walk2 (0.35 s later, ~half a Walk cycle) 3.0% changed — pose changes
  within the clip, background near-static. Pose captures at yaw 90 confirmed the
  body is fully textured; rear views look black only because this scene's
  single directional light leaves unlit sides near-black (same as round-1
  terrain back-slopes). Peak RSS 428 MB (llvmpipe).

## Round 2 — Probe 3: scale (1000x1000, instancing, shadows) — PASS
- ScaleWorldBuilder.cs (~300 lines incl. player controller): --probe3 dispatch
  in WorldBuilder (round-1 path untouched; --walk-test still passes, moved=30.26).
- Terrain 1000x1000, 250x250 segments = 125,000 tris, height = round-1 function
  family stretched (x0.15 coords) and amplified x4 (peaks ~ +/-18).
- Ground material = real Stride PBR material built in code: MaterialDescriptor
  with MaterialDiffuseMapFeature(ComputeTextureColor) + Lambert + GGX
  microfacet specular (F0 0.04). Texture is procedural: 512x512 baked in code
  from the height function (grass/dirt/rock by height+slope) x value noise.
  GOTCHA: Stride Color.R/G/B are BYTES — multiplying them as floats clamped
  the whole texture to white (diagnosed with a --tex-diag checkerboard build,
  which also proved UV/sampling/instancing in one frame).
- Props: 200 trees + 100 rocks = 300 props as THREE instanced draws via
  InstancingComponent + InstancingUserArray (500 matrices: trunk+canopy per
  tree, 1 per rock). InstanceCount/BoundingBox on InstancingUserArray are
  computed by UpdateWorldMatrices/Update(), not settable. Rocks are squashed
  spheres sunk into slopes (low domes) — verified rendered via a --rocks-red
  diagnostic (40 red px dome at ~130 m); in gray they are inconspicuous.
- Triangles (measured from engine MeshDraw counts at runtime): terrain 125,000;
  trunk 126 ea; canopy 1,024 ea; rock 1,024 ea (default Sphere tessellation is
  heavy); total scene 458,390. Draw calls: no public engine counter exists
  (FrameDrawCalls is an internal stat key only) — computed: 5 model draws in
  the main pass (terrain + 3 instanced + player) + shadow-pass caster draws.
- Frame time (llvmpipe, RELATIVE-ONLY): every frame saturated Stride's 500 ms
  UpdateTime clamp — 56 frames for 14 s of game time, ~0.7 s/frame wall incl.
  startup shader compile. Peak RSS 426 MB (final run; 513 MB worst observed).
- Heightfield collider: CONFIRMED unchanged from round 1 — no code-first path
  found; HeightfieldColliderShapeDesc still requires editor-baked heightmap
  assets (IHeightStickArraySource). Player movement uses height sampling.
- PROOF: probe-frames/probe3-scale.png — textured terrain filling the view
  (right side up), dirt biome band on the far ridge, ~12 instanced trees with
  contact shadows distributed to the horizon, player capsule + shadow.
