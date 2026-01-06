# BakePipeline Agent Guide

This folder contains a Unity Editor pipeline for remeshing, UV unwrap, and texture baking. Use this guide to make changes safely and to troubleshoot common failures.

## Scope and entry points
- `RemeshUnwrapBakeRunner.cs` is the core pipeline runner. It finds external tools, runs remesh + Blender headless, imports outputs, and creates a material.
- `RemeshUnwrapBakeWindow.cs` is the Editor UI (`Tools/LowPoly/Remesh -> Unwrap+Bake Window`) and persists settings via `EditorPrefs`.
- `bake_lod.py` is the Blender script that imports high/low, unwraps, and bakes normal + AO.

## External dependencies (required)
- **Surface Remesher**: `SurfaceRemeshingCli_bin.exe`
  - Preferred: set `SURFACE_REMESHER_EXE` to the full exe path.
  - Fallbacks: `Tools/SurfaceRemesher/SurfaceRemeshingCli_bin.exe` or package path.
- **Blender**: `blender.exe`
  - Preferred: set `BLENDER_EXE` in the window (or env var).
  - Blender 3.6+ / 4.x / 5.x supported by `bake_lod.py`.
- **Blender script**: `bake_lod.py`
  - Preferred: set `BLENDER_BAKE_SCRIPT` in the window (or env var).
  - Fallbacks: package path `Editor/BakePipeline/bake_lod.py` or `Assets/Editor/LowPolyBake/bake_lod.py`.

## Outputs and where they go
- Output folder: `Assets/Generated/LowPolyBakes/<high_name>/`
- Outputs:
  - `high_input.obj` (copied input)
  - `low_remeshed.obj` (picked remesh output)
  - `low_unwrapped.obj` (Blender export)
  - `normal.png`, `ao.png`
  - `baked.mat`
  - `blender_bake.log` (stderr/stdout from Blender)

## Remesh output selection
The remesher can produce multiple intermediate `.obj` files. The runner picks:
- The newest `.obj` in `remesh_out` by default.
- If the UI field **Output Name Filter** is set, it picks the newest `.obj` whose filename contains that substring (case-insensitive). This is useful for files like `*_noInterior.obj`.

## Blender baking notes
- Normal and AO are baked from **geometry**, not textures. `.mtl` is not required.
- The low mesh is unwrapped via Smart UV Project.
- The low mesh must overlap the high mesh spatially (scale/position); otherwise bakes may be empty.
- `--cage` uses invariant culture formatting (dot as decimal separator).

## Known logs and troubleshooting
- Blender errors: open `Assets/Generated/LowPolyBakes/<name>/blender_bake.log`.
- If Blender finishes but outputs are missing, the runner throws and points to the log.
- Long “Importing assets”: Unity may be importing large OBJ/PNG files. Check that Blender actually produced `low_unwrapped.obj`, `normal.png`, and `ao.png`.

## Typical manual validation
- Run from the window.
- Verify that `low_unwrapped.obj`, `normal.png`, `ao.png` exist in the output folder.
- Check the imported normal map has **Normal Map** type and AO has **sRGB off**.
- Apply `baked.mat` to the low mesh and inspect the result in a lit scene.

## Conventions for changes
- Keep changes localized to `RemeshUnwrapBakeRunner.cs` and `RemeshUnwrapBakeWindow.cs` when possible.
- Prefer adding clear log messages over heavy UI for debug.
- Avoid adding heavy dependencies; external tools are invoked via absolute paths.
- Do not assume a fixed package install path; use package resolution or env vars.
