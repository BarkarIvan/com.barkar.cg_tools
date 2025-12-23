# AGENTS.md

## Project overview
- Custom mipmap generator for Unity textures. GPU-only pipeline with a compute shader and an editor window.
- Supports color, normal map (with optional Toksvig-in-alpha), and packed/data maps with per-channel filtering.

## Entry points
- Editor window: `Assets/Scripts/Editor/CustomMipMapGenerator/CustomMipMapGeneratorWindow.cs`.
- Compute shader: `Assets/Scripts/Editor/CustomMipMapGenerator/CustomMipMapGenerator.compute`.

## Core behaviors
- GPU-only: no CPU fallback path. Mips are generated in a compute shader and read back with AsyncGPUReadback.
- `Full-Res Mips` controls how many initial mip levels sample from the full-res source; later mips sample from the previous mip.
- Texture types:
  - Color: sRGB (gamma correction enabled).
  - NormalMap: normal renormalization; optional Toksvig in alpha.
  - DataMap: linear data (no gamma), per-channel filters available.

## Toksvig
- When enabled, alpha stores `|Na|` (length of the summed normal before normalization) for each mip level.
- Alpha filtering is forced off while Toksvig is enabled to avoid overwriting Toksvig data.
- Base mip is preprocessed by `PrepareToksvigBase` to write `|Na|` into alpha before the mip chain.

## Filtering
- Filter modes: Kaiser (sharper, more ringing risk) and EWA (smoother, less ringing).
- Edge-aware option only affects color/data maps (not normal maps).
- Per-channel filters (DataMap only): Average, Min, Max, LinearRoughness, LinearSmoothness, PowerMean, PreserveCoverage.
- PreserveCoverage can be applied to any channel (via per-channel filter) and uses the Alpha Clip threshold.

## Kernels (compute)
- `GenerateMip`: main downsample/filter pass (handles normal map renorm + Toksvig).
- `PrepareToksvigBase`: writes `|Na|` into alpha for mip0 when Toksvig is active.
- `ComputeAlphaStats` + `ApplyAlphaScale`: coverage preservation pass (per-channel).
- `SharpenMip` + `CopyMip`: optional unsharp for early mips.

## Tips
- DataMap hides Alpha Filter Mode; use per-channel PreserveCoverage for mask-like channels.
- Toksvig requires a shader-side roughness adjustment (see the help text in the UI).
