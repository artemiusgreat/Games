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
