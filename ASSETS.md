# Assets

Per the brief's section 7: only explicitly-permissive-licensed models are
allowed, every asset's source/author/license must be recorded here, and if no
suitable model is available, a primitive placeholder is used instead and
listed as missing.

**Current status: no external 3D models have been sourced yet.** This
environment did not have a confirmed way to browse/download external asset
sources during development, so every visual element so far is a Godot
built-in primitive or procedurally generated mesh — no license concerns,
since nothing was imported.

## What's used today (all procedural/primitive, CC0-equivalent by construction)

| Element | What it actually is | Source |
|---|---|---|
| Cutting tool | A plain `BoxMesh` (10x10x10 units), red `StandardMaterial3D` | Generated in `Scenes/Main.tscn`, no external asset |
| Workpiece (stock) | Procedurally generated surface-of-revolution mesh from `LatheRadiusProfile`, built at runtime by `Scripts/Material/WorkpieceVisual.cs` via `SurfaceTool` | Generated code, no external asset |
| Chips | Pooled, stretched `BoxMesh` instances, color/scale driven by `ChipClassifier` | Generated code, no external asset |
| Lighting | Godot's built-in `DirectionalLight3D` | Engine built-in |

## MISSING — placeholder only, needs a real asset

Every row below is currently a primitive shape standing in for something a
real training simulator should eventually have a proper model for. Each is
swappable independently (nothing hardcodes primitive geometry outside its own
script/scene node) once a suitably licensed model is found:

| Missing asset | Current placeholder | Where it would plug in |
|---|---|---|
| Lathe machine body/bed/headstock/tailstock | None rendered at all — only the tool and workpiece exist in the scene | A new `MeshInstance3D`/model under `Scenes/Main.tscn`, sized to match the machine coordinate ranges already defined in `linuxcnc-config/sim_lathe.ini` ([AXIS_X]/[AXIS_Z] limits) |
| Chuck / jaws holding the stock | None (the stock mesh just starts at Z=`StockZMax` in open space) | Would attach near `Workpiece`'s Z-max end in `Main.tscn` |
| Cutting tool holder / turret | `BoxMesh` cube stands in for the whole tool assembly | Replace `ToolCube` in `Main.tscn` with a real tool-holder model; tool tip offset math in `LatheRadiusProfile`/`ChipSpawner` doesn't depend on the visual mesh, so this is a drop-in swap |
| Operator panel physical housing | None — `OperatorPanel.cs` is a 2D on-screen UI overlay, not a 3D pendant model | Out of scope unless the project moves to simulating a 3D panel the user walks up to, rather than a 2D HUD |
| Floor / shop environment | None (empty background) | Would need a simple floor plane + ambient lighting at minimum for a "standing in the shop" feel |

## Licensing policy for future additions

- Only CC0, CC-BY (with attribution recorded here), or equivalently
  permissive licenses.
- No FANUC/Siemens/Haas (or any real vendor's) logos, branding, or exact
  panel/machine designs — the brief explicitly requires an original design
  that only follows the *general logic and layout* of common CNC controls.
  `OperatorPanel.cs` is already named generically ("Operator Panel", never
  "Fanuc-style" in any user-visible text) for this reason.
- When a model is added, record here: file path, source URL, author, exact
  license, and date added.
