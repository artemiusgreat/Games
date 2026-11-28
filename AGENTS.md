# Rules

## 1. Naming Conventions & `this.` Qualification
- **No `snake_case` or underscores in identifiers.** Use `MaxHealth`, never `_maxHealth`, `_MaxHealth`, or `MAX_HEALTH`.
- **Exceptions (only these):**
  - Godot virtual method overrides, which begin with `_` (`_Ready`, `_EnterTree`, `_ExitTree`, `_Process`, `_PhysicsProcess`, `_Input`, `_UnhandledInput`, `_Draw`, `_GuiInput`, `_Notification`, `_IntegrationForces`, etc.). The `_` prefix is allowed only on overrides of Godot virtuals, never on your own methods.
  - Discards (`_`, `out _`, `(_, _) =>`).
- **`PascalCase`:** classes, structs, enums, namespaces, methods, properties, constants, fields, events, local functions. Prefix interfaces with `I`. Prefix type parameters with `T` (`TState`).
- **`camelCase`:** locals and parameters (`float deltaTime`, `int currentScore`).
- **Mandatory `this.`:** always prefix instance field, property, method, and event access with `this.`. Static members and constants are accessed via the type name (`Constants.MaxSpeed`). Underscore-prefixed fields are forbidden because `this.` already distinguishes members from locals.
- **Verification:** `.editorconfig` naming rules may not catch mid-word underscores or ALL_CAPS. Verify with test identifiers (`Max_Health`, `MAX_HEALTH`) and, if they pass, treat this rule as manually enforced and check it in review.

## 2. Build & Verification Loop
- **Build:** run `dotnet build` after any C# file modification. A task is not complete while any warning or error remains.
- **Format:** run `dotnet format --verify-no-changes` before finishing.
- **Godot partial classes:**
  - Every class inheriting from `GodotObject` (including `Node`, `Resource`, `RefCounted`) must be `partial`, as must any class containing a nested Godot class. Never remove `partial`.

## 3. Node References & Scene Wiring
- **Call down, signal up.** A node may call methods on children it owns, through `[Export]` references or unique-name lookups. It must not call methods on its parent or siblings. Communicate upward and sideways with signals or events (see §5, §7).
- **Never** use path-style strings such as `GetNode<Sprite2D>("Visuals/Body/Sprite2D")`.
- **Fail fast:** in `_Ready()`, validate every required `[Export]` reference with `ArgumentNullException.ThrowIfNull(this.NodeName);`.

## 4. Performance & Memory (Zero-Allocation Hot Paths)
Hot paths: `_Process`, `_PhysicsProcess`, `_Input`, `_UnhandledInput`, `_IntegrationForces`, per-frame signal handlers, and any tight loop.
- **No heap allocations in hot paths:**
  - No `new` of classes, no capturing lambdas or closures, no string building or interpolation.
  - No LINQ.
  - No `GetNode()`/`GetTree()` lookups. Cache in `_Ready()`.
  - No `GetChildren()` (allocates a new array each call).
  - No `foreach` over interface-typed collections (boxing). Use arrays or `List<T>`.
- **Cache `StringName`s.** String literals passed to Godot APIs (`Input.IsActionPressed("jump")`, `EmitSignal`) convert to `StringName` and may allocate on each call. 
- **Avoid per-frame `EmitSignal`** where possible, since it allocates a `Variant` argument array. Emit on state change, not every frame.
- **Vectors:** `Vector2`/`Vector3` are stack-allocated structs. Allocation risk comes from boxing, not from `new Vector2(...)`.
- **Tooling:** use a heap-allocation analyzer or `BannedApiAnalyzers` to enforce this. Prose rules alone are not reliable.

## 5. Signals & Lifecycle Management
- **Subscribe in `_EnterTree()`, unsubscribe in `_ExitTree()`.** Every `+=` needs a matching `-=`. `_Ready()` runs once, but nodes can leave and re-enter the tree (pooling, reparenting), so `_Ready`/`_ExitTree` pairing loses events.
- **No lambdas or anonymous methods as subscribers.** They cannot be unsubscribed. Use named methods.

## 6. Architecture & Code Cleanliness
- **Decouple logic from `Node`:** keep `Node` scripts thin (input, animation, audio, physics triggers). Put state machines, combat math, inventory, and game rules in plain C# classes (POCOs) with no `using Godot;`.
- **Enforce the boundary structurally:** POCOs live in a separate class library with no GodotSharp reference, plus a test project. Use `System.Numerics` types or primitives in POCOs and convert at the Node boundary.
- **Logging in POCOs:** POCOs do not call `Dbg`. They throw exceptions or use an injected `IGameLogger` (a boundary interface and the one sanctioned exception to the single-implementation rule in §7). `Dbg.Print(...)` is the project's Godot-side logging wrapper and must be defined once in `Core/Dbg.cs`.
- **Small files:** keep hand-written scripts under 250 lines (generated code excluded). Split into composable child nodes (`HealthComponent`, `MovementComponent`).
- **Exceptions:**
  - Never write empty `catch` blocks or catch `Exception` broadly (`CA1031`).
  - Catch specific types and either handle them meaningfully or log and rethrow. Logging and continuing counts as swallowing.
- **Threading:** do not call Godot APIs from background threads. Use `CallDeferred` to marshal back to the main thread.
- **Shared Resources:** `Resource` instances are shared by default. Do not mutate them at runtime unless you call `Duplicate()` first.

## 7. Design Patterns & Architecture Triggers
- **Plan before coding:** before a non-trivial feature, state which design pattern(s) apply and why, or state that none apply.
- **Guardrails:**
  - Try to follow SOLID and DRY principles.
  - No interfaces or factories for a single implementation (exception: the `IGameLogger` boundary seam in §6).
  - No catch-all `*Manager` classes. Autoload singletons need a specific, narrow responsibility and name.

## 8. Definition of Done
- [ ] `dotnet build` is clean (no warnings, no errors).
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] No `snake_case` or underscores outside the allowed exceptions.
- [ ] Every `+=` has a matching `-=` in `_ExitTree()`.
- [ ] No allocations added to hot paths.
- [ ] Required `[Export]` references are validated in `_Ready()`.
- [ ] Hand-written scripts are under 250 lines.