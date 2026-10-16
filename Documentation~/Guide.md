# Lines and curves from code

`UnityLines` draws world-space lines without Unity's LineRenderer or a paid package. It accepts straight segments, ordered point lists, cubic/quadratic Beziers, arcs and circles. Orbit paths, routes and tactical indicators can all use the same API; the drawing code knows nothing about ships or their orders.

Install the package as described in the [README](../README.md). Runtime source is in `Runtime/`; projects using assembly definitions must reference `UnityLines`.

## Setup

Add **Line Drawing** to an empty GameObject and assign **Target Camera**, then reference that component from your script. Leaving Target Camera empty uses `Camera.main`. No Canvas, MeshRenderer, per-line GameObject or material setup is needed. The drawer loads its included material from `Runtime/Resources/UnityLines/LineStroke.mat`.

Alternatively call `LineDrawing.Create(camera)` once when your system starts. It returns a new scene-owned drawer GameObject; destroy that GameObject when its owner no longer needs it. Do not create a drawer every frame. Share one drawer among systems that use the same camera, or use separate drawers when they need independent lifetimes. Multiple cameras each need their own drawer because thickness and facing depend on the camera. The drawer GameObject's layer must be included in the camera's Culling Mask.

## Draw for one frame

This complete component draws a cyan connection while both endpoints exist. Attach it to a GameObject and assign its fields.

```csharp
using UnityLines;
using UnityEngine;

public class ConnectionExample : MonoBehaviour
{
    [SerializeField] private LineDrawing drawing;
    [SerializeField] private Transform start;
    [SerializeField] private Transform end;

    private void Update()
    {
        if (drawing != null && drawing.isActiveAndEnabled && start != null && end != null)
            drawing.DrawLine(start.position, end.position,
                new LineStyle(Color.cyan, width: 2));
    }
}
```

All `Draw...` methods submit for the current frame only. Stop calling them and the line disappears. Call them on the main thread in `Update`, or in a `LateUpdate` with an execution order below 100. The drawer renders in `LateUpdate` at order 100, after camera scripts with lower execution orders. Calls after that submission are too late for that frame and are discarded on the next frame. Avoid FixedUpdate for frame-only drawing: rendered frames can occur without a physics tick. A disabled drawer rejects transient submissions.

## Keep a path and update it

A retained path stays visible until you hide, clear or dispose it. Set its shape when the definition changes; the renderer manages any additional LOD resampling as the camera changes. Here a circle follows a Transform, including its rotation and scale:

```csharp
using UnityLines;
using UnityEngine;

public class RangeCircleExample : MonoBehaviour
{
    [SerializeField] private LineDrawing drawing;
    [SerializeField] private float radius = 20;
    private LineHandle circle;

    private void OnEnable()
    {
        circle = drawing.CreatePath(
            new LineStyle(Color.green, 2, depthTest: false),
            relativeTo: transform);
        circle.Path.SetCircle(Vector3.zero, Vector3.up, radius, segments: 128);
    }

    public void SetRadius(float value)
    {
        radius = value;
        if (circle != null && !circle.IsDisposed)
            circle.Path.SetCircle(Vector3.zero, Vector3.up, radius, 128);
    }

    private void OnDisable()
    {
        circle?.Dispose();
        circle = null;
    }
}
```

Assign the drawer before enabling that example and use a positive radius. `relativeTo` is optional: without it, points are world coordinates. With it, points are local coordinates transformed every frame. Width in world units remains a world measurement even if that Transform is scaled. Dispose the handle if its reference Transform is destroyed; a destroyed reference otherwise behaves like null and its points become world coordinates.

Use `handle.Visible = false` to hide without losing geometry, `handle.Style = new LineStyle(...)` to change appearance, and `handle.Path.SetLine/SetPolyline/SetBezier/...` to replace geometry. `Style` is a struct, so assign the whole value, or copy it, edit the copy and assign it back. `handle.Path.Clear()` keeps an empty registered handle; `handle.Dispose()` unregisters it. `drawing.Clear()` invalidates ALL its handles, so their owners must create new ones. Disabling the drawer hides retained paths and preserves them for re-enabling; destroying it releases handles, meshes and runtime materials.

## Geometry methods

The following snippets assume an existing `drawing` and `style = new LineStyle(Color.cyan, 2)`; vector arguments are ordinary world positions/directions unless you supply `relativeTo`.

```csharp
drawing.DrawLine(start, end, style);
drawing.DrawPolyline(points, style, closed: false);
drawing.DrawBezier(start, control1, control2, end, style, segments: 64);
drawing.DrawQuadraticBezier(start, control, end, style, segments: 32);
drawing.DrawArc(center, Vector3.up, Vector3.forward,
    radius: 40, angleDegrees: 120, style: style, segments: 48);
drawing.DrawCircle(center, Vector3.up, radius: 40, style: style, segments: 128);
```

`points` can be a `Vector3[]` or `List<Vector3>`. The API copies submitted polylines, so changing the source afterwards does not affect that frame's command. Ordered points define one continuous stroke. Empty and one-point paths draw nothing; duplicate projected points are skipped. Use `closed: true` to connect the last point to the first without repeating it.

`LinePath` is also a reusable geometry buffer independent of a renderer. Use it to combine straight sections and connected curves:

```csharp
// Allocate once, keep this buffer in a field, and rebuild when the route changes.
var path = new LinePath();
path.Begin(start);
path.AddPoint(waypoint);
path.BezierTo(control1, control2, destination, segments: 64);

// Each Update, or copy once into a retained handle with handle.Path.CopyFrom(path).
drawing.DrawPolyline(path, style);
```

Curves use adaptive screen-space detail by default. `segments` is the maximum segment budget per curve, in the range 1..4096; a closed circle needs at least three. The renderer keeps the mathematical controls for arcs and Beziers and chooses fewer points when the curve is small or nearly straight. Tight bends and close-up curves receive more detail, up to that budget. Explicit polylines preserve all authored points. For custom splines or predicted trajectories, sample points in your own system and pass the ordered list to `SetPolyline` or `DrawPolyline`.

## Quality and level of detail

```csharp
drawing.PixelError = 0.75f;       // Default projected chord-error target, in pixels.
drawing.AdaptiveSampling = true; // Default: automatic detail for curves.

// Preserve an exact segment count for one retained path:
handle.Path.AdaptiveSampling = false;

// Or use the original fixed-budget behavior for the entire drawer:
drawing.AdaptiveSampling = false;
```

The Inspector exposes **Adaptive Sampling** and **Pixel Error**. Smaller error targets produce finer curves. The error is a conservative target for standard perspective/orthographic cameras, subject to the segment cap; very close curves or too-small budgets may exceed it. It measures the centerline's chord approximation, not every antialiased edge pixel. Increase the segment budget if a curve still appears angular at your closest intended zoom.

The renderer converts the pixel target into a conservative local-space tolerance using cached camera information, curve bounds and transform scale. Cubics subdivide where their controls depart from the endpoint chord; arcs use a sagitta bound. Error density is rounded up into discrete levels. Refinement happens when required; coarsening waits until the required density falls below 40% of the cached level. This hysteresis avoids repeated resampling around a threshold. Detail changes are discrete, not blended, so choose a small enough error target for your visual needs.

`LinePath.Count` and its indexer continue to expose the original fixed-budget points. Adaptive render points are internal to each drawer. `LastVertexCount` reports actual submitted vertices, `LastResampledPathCount` reports paths resampled this frame, and `LastUploadCount` reports rebuilt mesh uploads.

Arc angles are signed degrees within -360..360, using `Quaternion.AngleAxis` around the supplied normal. The start direction is projected into the arc plane and must not be parallel to its normal. Full 360-degree arcs close automatically. Circle start orientation is chosen automatically; use an arc when a specific start direction matters. Invalid coordinates, nonpositive widths/radii and invalid sample counts throw argument exceptions.

## Appearance and depth

```csharp
// Fixed camera pixel-coordinate thickness, hidden behind opaque geometry.
var tactical = new LineStyle(Color.cyan, 2);

// World-unit thickness: grows when the camera moves closer.
var physical = new LineStyle(Color.yellow, 0.1f, LineWidthSpace.World);

// Visible through scene geometry, with partial transparency.
var overlay = new LineStyle(new Color(0, 1, 1, 0.6f), 3, depthTest: false);
```

Always construct a style with a positive width; `default(LineStyle)` has zero width and is invalid. Depth-tested lines obey opaque depth but do not write depth themselves. Transparent objects may not occlude them because those materials often do not write depth. Overlay lines bypass depth and render in a later transparent queue. They still respect camera clipping/culling and do not cover Screen Space Overlay Canvas UI. They are world geometry, not uGUI elements.

Strokes face the camera, have flat ends with antialiased edges, and share continuous joins. **Miter Limit** bounds corner extension in multiples of half-width plus the feather. Sharp reversals, intersecting strokes and translucent overlaps may show strip overlap; this version does not calculate geometric unions or provide selectable round/bevel joins. Subpixel widths can fade. Pixel widths use camera screen coordinates; dynamic resolution and upscalers can affect their final appearance.

## Renderer and performance

The vertex shader expands centerlines into camera-facing strokes, including width, joins and end caps. Camera movement normally updates shader projection without rebuilding retained meshes. Each drawer reads camera matrices and frustum planes once per frame and rejects paths using conservative bounds inflated for stroke width. Paths crossing the near/far planes are clipped on the CPU, with their clipped geometry updated when the camera moves.

Each path caches its local sampled points and world-space shader inputs. A moving Transform reuses local samples while its LOD remains valid, but updates the world geometry. Geometry/style edits, clipping changes and LOD changes invalidate the relevant cache. Each of the two depth-mode batches uploads only when its ordered entries or their revisions change. Changing a path in a batch currently uploads that whole batch; this is not a per-path GPU buffer allocator. `Draw...` submissions still rebuild changing transient geometry, so use retained handles for unchanged paths.

Storage grows to its high-water mark and is reused. Per-path cache data is released when its handle is no longer referenced; the drawer's batch references are updated on the next submission. Keeping disposed handles in your own lists also keeps their managed caches alive. Batch capacities remain available for reuse.

Retained paths still have a per-path CPU cost for bounds, visibility and LOD checks. Changing geometry costs more than moving only the camera, and GPU cost still scales with vertices and coverage. Profile your actual tactical view before choosing a point budget or comparing against Unity's LineRenderer.

The shader and material are included in the package, and the material's Resources reference keeps the shader available in builds. The renderer uses Unity's [Graphics.RenderMesh API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMesh.html). Runtime scope is URP desktop cameras, perspective or orthographic. Scene view authoring previews, XR, custom oblique camera projections, SVG import, fills, dashes and Canvas clipping are outside this version's scope. Gameplay integration remains in the systems that call this tool.

## Try the sample

Import **Basic Gallery** from the package's **Samples** tab, drag `BasicGallery.prefab` into an empty URP scene, and press Play. The prefab creates its own camera and drawer at runtime; no references or materials need assigning. The five retained shapes demonstrate lines, cubic and quadratic Beziers, arcs, circles, pixel/world widths and both depth modes. The animated sixth line demonstrates frame-only drawing. The source is intentionally small enough to copy into your own scripts.

The gallery camera is positioned relative to the prefab, looks along local +Z, and clears to a dark background. Use an empty scene to avoid competing cameras. Disable/re-enable the prefab to recreate the demo; disabling releases its owned drawer, handles and camera.

## Troubleshooting

- **Nothing renders:** use a URP project with a pipeline asset assigned in Graphics/Quality settings. The package installs its URP dependency but does not change your project's rendering settings. Check Target Camera (or the MainCamera tag), camera culling mask, depth and that the drawer is enabled. Rendering occurs in Play mode/Game view.
- **Pink or unsupported shader:** use Unity 6 and URP 17; Built-in and HDRP are unsupported. Confirm the package material still references the included shader. You do not need to add it to Always Included Shaders.
- **Cannot find UnityLines in code:** add `using UnityLines;` and, if your scripts use an asmdef, add a reference to `UnityLines`.
- **Sample missing:** import it from Package Manager first. `Samples~` is excluded from compilation until imported.
- **Curve looks angular up close:** increase its segment budget or reduce Pixel Error. Explicit point lists are never automatically smoothed.
- **Line disappears between frames:** `Draw...` lasts one frame; call it from Update, or create a retained handle.
- **Multiple projects:** install this same package in each project. Keep package changes in the repository; Git package cache edits are not durable.
