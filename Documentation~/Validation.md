# Package validation

## 0.1.1 opacity update

The updated standalone assembly and the matching project's runtime/editor assemblies compile successfully with Unity 6000.0.39f1 compiler references. Runtime and shader copies were checked for parity apart from namespace/resource names. The rendering results below apply to 0.1.0; no new player rendering tests were run for 0.1.1.

To check opacity manually, vary the Line Drawing component's Opacity slider between 1, 0.5 and 0. Retained lines should fade, disappear and return. Use a path with color alpha 0.5 and drawer opacity 0.5 to check their combined 0.25 alpha, and inspect both depth-tested and overlay strokes. During an otherwise static fade above zero, LastUploadCount and LastResampledPathCount should stay zero after warmup. Per-path opacity changes may upload their containing batch.

## 0.1.0 package checks

Version 0.1.0 was checked in a separate Unity project with no game scripts. The project referenced this repository as a local UPM dependency through `package.json`; it did not copy the runtime into Assets.

## Configuration

- Unity 6000.0.39f1, Universal Render Pipeline 17.0.3, Render Graph enabled.
- Windows x64 standalone player, Mono, Direct3D 11, NVIDIA RTX 4060 Laptop GPU.
- 1280 x 960 render targets, standard perspective and orthographic cameras.

## Results

The player build succeeded with **zero errors**. Unity's bundled URP terrain shader emitted an unrelated compiler warning; the package shader compiled successfully.

Package Manager resolved the local dependency, discovered the declared sample, and recognized it in the normal imported-samples directory. The sample was copied to that directory by the validation harness; the Package Manager UI import button was not automated. The demo prefab's component reference loaded correctly, and its separate sample assembly compiled against `UnityLines`.

All **24 runtime assertions passed**; see [raw results](ValidationResults.txt). They cover:

- Standalone runtime assembly and inclusion of the Resources material/shader in a player build.
- Demo prefab setup, all five shape colors, rendering, disable and re-enable cleanup.
- Static mesh/tessellation reuse, adaptive detail reduction, fixed segment counts and increased detail when approaching a curve.
- Perspective and orthographic rendering without magenta error-shader pixels.
- Retained visibility, drawer disable/re-enable, offscreen culling and a near-plane crossing.
- Handle disposal, frame-only submission/expiry, and clearing retained geometry.

The [gallery screenshot](Gallery.png) was captured from this player. Package metadata was also checked for unique asset GUIDs, valid material/prefab references, matching meta files, documentation links and absence of game dependencies. Rendering algorithms match the source renderer; packaging changes isolate the namespace, assembly and resource names.

This was a packaging and rendering smoke check, not a new performance benchmark. It does not establish support for other graphics APIs, platforms, render pipelines, XR, or custom camera projections.

## Recheck in your project

1. Install via Package Manager and confirm there are no compilation errors.
2. Import **Basic Gallery**, place its prefab in an empty URP scene, and enter Play mode.
3. Confirm the cyan line, yellow cubic, green circle, orange arc, white quadratic and moving cyan segment appear in Game view.
4. Disable/re-enable the prefab and confirm the gallery returns without duplicate cameras or drawers.
5. Make a player build and repeat the visual check to verify shader inclusion on your target platform.
6. Use the guide's examples with your own camera, opaque occluders, transforms and expected path counts; inspect the Unity Profiler and Frame Debugger.
