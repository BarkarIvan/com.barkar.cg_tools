# Custom MipMap Generator (AGENTS)

> [!note]
> Obsidian-friendly quick map for contributors.

## Summary
- GPU-only mip generation for Unity textures using a compute shader and an editor window.
- Supports Color, NormalMap (optional Toksvig-in-alpha), and DataMap with per-channel filters.
- Outputs either a single `.cmips` file or per-platform variant assets.

## Entry points
- Editor window: `Editor/CustomMipMapGenerator/CustomMipMapGeneratorWindow.cs`
- Compute shader: `Editor/CustomMipMapGenerator/CustomMipMapGenerator.compute`
- CMIPS format + IO: `Editor/CustomMipMapGenerator/CustomMipMapGeneratorMipFile.cs`
- CMIPS importer: `Editor/CustomMipMapGenerator/CustomMipMapGeneratorImporter.cs`
- Auto-gen (profiles): `Editor/CustomMipMapGenerator/CustomMipMapGeneratorAutoGeneration.cs`
- Auto-gen hook: `Editor/CustomMipMapGenerator/CustomMipMapGeneratorAutoProcessor.cs`

## Data flow (single file)
1. User generates `.cmips` from the window or profile auto-gen.
2. GPU compute builds mip chain in `CustomMipMapGeneratorGpu`.
3. `CustomMipMapGeneratorMipFile` writes RGBA32 mip data (optionally Deflate-compressed).
4. `CustomMipMapGeneratorImporter` reads `.cmips`, decompresses if needed, creates `Texture2D`,
   then Unity compresses per active build target.

## Texture kinds
- Color: sRGB (gamma correction enabled in Gamma color space).
- NormalMap: renormalized normals; optional Toksvig in alpha.
- DataMap: linear data (no gamma); per-channel filters available.

## Toksvig
- Alpha stores `|Na|` (length of summed normal before normalization) per mip.
- Alpha filtering forced to `None` while Toksvig is enabled.
- Base mip is preprocessed by `PrepareToksvigBase`.

## Filtering
- Filter modes: Kaiser (sharper, more ringing risk) and EWA (smoother).
- Edge-aware only affects Color/Data maps (not normal maps).
- Per-channel filters (DataMap only): Average, Min, Max, LinearRoughness, LinearSmoothness,
  PowerMean, PreserveCoverage.

## CMIPS format
- Version 2 (backward compatible with v1).
- Payload is full RGBA32 mip chain.
- Compression flag in header: 0 = raw, 1 = Deflate (only used if smaller).

## Compute kernels
- `GenerateMip`: main downsample/filter pass (normal renorm + Toksvig).
- `PrepareToksvigBase`: writes `|Na|` into alpha for mip0 when Toksvig is active.
- `ComputeAlphaStats` + `ApplyAlphaScale`: coverage preservation pass (per-channel).
- `SharpenMip` + `CopyMip`: optional unsharp for early mips.

## Notes
- DataMap hides Alpha Filter Mode; use per-channel PreserveCoverage for mask-like channels.
- Toksvig requires shader-side roughness adjustment (see UI help text).
