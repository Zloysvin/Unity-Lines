# Changelog

## 0.1.1

- Added an Inspector opacity slider and `LineDrawing.Opacity` to fade all strokes using a shader multiplier without rebuilding meshes.
- Added `LineStyle.Opacity` and `LineHandle.Opacity` for convenient per-line color-alpha changes.
- Zero overall opacity skips rendering while retaining persistent paths; transient submissions still expire normally.

## 0.1.0

First standalone Unity Package Manager release.

- Code API for lines, polylines, cubic/quadratic Beziers, arcs and circles.
- Retained handles and frame-only submissions; optional relative transforms.
- Pixel or world-unit widths, color/alpha, depth-tested and overlay strokes.
- Adaptive curve sampling with screen-space error targets and LOD hysteresis.
- GPU stroke expansion, cached geometry and uploads, frustum culling and depth clipping.
- Isolated `UnityLines` assembly and namespace, bundled URP shader/material.
- Importable Basic Gallery prefab and usage guide.
