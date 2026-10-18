using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityLines
{
    /// <summary>Code-driven world-space lines and curves for one camera. Uses two reusable mesh batches and no LineRenderer.</summary>
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Unity Lines/Line Drawing")]
    public sealed class LineDrawing : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField, Range(1, 10)] private float miterLimit = 4;
        [SerializeField] private bool adaptiveSampling = true;
        [SerializeField, Range(.1f, 4)] private float pixelError = .75f;
        [SerializeField, Range(0, 1), Tooltip("Multiplies every line's color alpha. Zero hides all strokes without clearing their paths.")]
        private float opacity = 1;
        private readonly LineCamera cameraData = new LineCamera();
        private static readonly int ViewportId = Shader.PropertyToID("_LineViewport");
        private static readonly int MiterId = Shader.PropertyToID("_MiterLimit");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        /// <summary>Overall alpha multiplier for both depth modes. Finite values are clamped to [0,1].
        /// Changes use a shader uniform, preserving cached geometry. Zero skips submission and drops frame-only commands.</summary>
        public float Opacity
        {
            get => LinePath.IsFinite(opacity) ? Mathf.Clamp01(opacity) : 1;
            set
            {
                if (!LinePath.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Opacity must be finite.");
                opacity = Mathf.Clamp01(value);
            }
        }
        /// <summary>Enables curve LOD; turn off to use the original fixed segment budgets.</summary>
        public bool AdaptiveSampling { get => adaptiveSampling; set => adaptiveSampling = value; }
        /// <summary>Conservative projected chord-error target in camera pixels, subject to the curve's segment cap.</summary>
        public float PixelError { get => pixelError; set { if (!LinePath.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); pixelError = value; } }
        /// <summary>Paths whose sampled geometry was rebuilt on the last frame.</summary>
        public int LastResampledPathCount { get; private set; }
        /// <summary>Number of mesh batches uploaded on the last frame; unchanged batches are submitted without re-uploading.</summary>
        public int LastUploadCount { get; private set; }
        private readonly List<LineHandle> paths = new List<LineHandle>();
        private readonly List<FramePath> framePaths = new List<FramePath>();
        private int frameCount;
        private int submittedFrame = -1;
        private LineMeshBatch depthBatch, overlayBatch;
        private Material depthMaterial, overlayMaterial;
        private bool resourceError;

        private sealed class FramePath
        {
            public readonly LinePath Path = new LinePath();
            public LineStyle Style;
            public Transform RelativeTo;
            public readonly LineStrokeCache Cache = new LineStrokeCache();
        }

        /// <summary>Camera receiving the strokes. Null uses Camera.main; use separate drawers for multiple cameras.</summary>
        public Camera TargetCamera { get => targetCamera; set => targetCamera = value; }
        /// <summary>Number of retained paths, including hidden or empty paths.</summary>
        public int PathCount => paths.Count;
        /// <summary>Vertices submitted on the last LateUpdate, across both depth modes.</summary>
        public int LastVertexCount { get; private set; }

        /// <summary>Creates a scene-owned drawer at runtime; alternatively add the component in the Inspector.</summary>
        public static LineDrawing Create(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var drawer = new GameObject("Line Drawing").AddComponent<LineDrawing>();
            drawer.TargetCamera = camera;
            return drawer;
        }

        /// <summary>Creates an initially empty persistent path. Dispose the handle to remove it.</summary>
        public LineHandle CreatePath(LineStyle style, Transform relativeTo = null)
        {
            style.Validate();
            var handle = new LineHandle(this, style, relativeTo);
            paths.Add(handle);
            return handle;
        }

        /// <summary>Draws a straight segment for this frame. Call from Update, before the drawer's LateUpdate.</summary>
        public void DrawLine(Vector3 start, Vector3 end, LineStyle style, Transform relativeTo = null)
        { PrepareFramePath(style, relativeTo).SetLine(start, end); frameCount++; }

        /// <summary>Copies and draws a point list for this frame, optionally joining the last point to the first.</summary>
        public void DrawPolyline(IReadOnlyList<Vector3> points, LineStyle style, bool closed = false, Transform relativeTo = null)
        { PrepareFramePath(style, relativeTo).SetPolyline(points, closed); frameCount++; }

        /// <summary>Copies a reusable sampled path for this frame; later edits to the source do not affect this submission.</summary>
        public void DrawPolyline(LinePath path, LineStyle style, Transform relativeTo = null)
        { PrepareFramePath(style, relativeTo).CopyFrom(path); frameCount++; }

        /// <summary>Draws a sampled cubic Bezier for this frame.</summary>
        public void DrawBezier(Vector3 start, Vector3 control1, Vector3 control2, Vector3 end, LineStyle style, int segments = 48, Transform relativeTo = null)
        { PrepareFramePath(style, relativeTo).SetBezier(start, control1, control2, end, segments); frameCount++; }

        /// <summary>Draws a sampled quadratic Bezier for this frame.</summary>
        public void DrawQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, LineStyle style, int segments = 32, Transform relativeTo = null)
        { PrepareFramePath(style, relativeTo).SetQuadraticBezier(start, control, end, segments); frameCount++; }

        /// <summary>Draws an arc for this frame, in the supplied plane, with a signed sweep in degrees.</summary>
        public void DrawArc(Vector3 center, Vector3 normal, Vector3 startDirection, float radius, float angleDegrees, LineStyle style, int segments = 64, Transform relativeTo = null)
        { PrepareFramePath(style, relativeTo).SetArc(center, normal, startDirection, radius, angleDegrees, segments); frameCount++; }

        /// <summary>Draws a closed circle for this frame in an arbitrary plane.</summary>
        public void DrawCircle(Vector3 center, Vector3 normal, float radius, LineStyle style, int segments = 96, Transform relativeTo = null)
        { PrepareFramePath(style, relativeTo).SetCircle(center, normal, radius, segments); frameCount++; }

        /// <summary>Clears all geometry and invalidates retained handles. Buffer capacity is retained.</summary>
        public void Clear()
        {
            foreach (var path in paths) path.Release();
            paths.Clear();
            frameCount = 0;
            foreach (var path in framePaths) { path.Path.Clear(); path.RelativeTo = null; }
            LastVertexCount = 0;
        }

        /// <summary>Unregisters one retained handle.</summary>
        internal void Remove(LineHandle handle) { paths.Remove(handle); }

        /// <summary>Reuses transient command storage; geometry is committed only after the caller fills it successfully.</summary>
        private LinePath PrepareFramePath(LineStyle style, Transform relativeTo)
        {
            style.Validate();
            if (!isActiveAndEnabled) throw new InvalidOperationException("Enable LineDrawing before submitting frame-only lines.");
            if (submittedFrame != Time.frameCount)
            { frameCount = 0; submittedFrame = Time.frameCount; }
            if (frameCount == framePaths.Count) framePaths.Add(new FramePath());
            var entry = framePaths[frameCount];
            entry.Style = style; entry.RelativeTo = relativeTo;
            return entry.Path;
        }

        /// <summary>Builds camera-facing strips after the normal camera/owner updates and submits each depth mode once.</summary>
        private void LateUpdate()
        {
            LastVertexCount = 0;
            LastResampledPathCount = 0; LastUploadCount = 0;
            if (submittedFrame != Time.frameCount) frameCount = 0;
            float alpha = Opacity;
            if (alpha <= 0) { frameCount = 0; return; }
            var camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null || !camera.isActiveAndEnabled || !EnsureResources()) { frameCount = 0; return; }
            depthBatch.Clear(); overlayBatch.Clear();
            cameraData.Update(camera);
            float limit = LinePath.IsFinite(miterLimit) ? Mathf.Clamp(miterLimit, 1, 10) : 4;
            depthMaterial.SetVector(ViewportId, cameraData.Viewport); overlayMaterial.SetVector(ViewportId, cameraData.Viewport);
            depthMaterial.SetFloat(MiterId, limit); overlayMaterial.SetFloat(MiterId, limit);
            depthMaterial.SetFloat(OpacityId, alpha); overlayMaterial.SetFloat(OpacityId, alpha);
            foreach (var path in paths)
                if (path.Visible) Append(path.Cache, path.Path, path.Style, path.RelativeTo, limit);
            for (int i = 0; i < frameCount; i++)
            {
                var path = framePaths[i];
                Append(path.Cache, path.Path, path.Style, path.RelativeTo, limit);
                path.RelativeTo = null;
            }
            frameCount = 0;
            depthBatch.Render(camera, depthMaterial, gameObject.layer);
            overlayBatch.Render(camera, overlayMaterial, gameObject.layer);
            LastVertexCount = depthBatch.VertexCount + overlayBatch.VertexCount;
            LastUploadCount = (depthBatch.Uploaded ? 1 : 0) + (overlayBatch.Uploaded ? 1 : 0);
        }

        /// <summary>Routes a stroke into its depth-mode batch.</summary>
        private void Append(LineStrokeCache cache, LinePath path, LineStyle style, Transform relativeTo, float limit)
        {
            float error = LinePath.IsFinite(pixelError) ? Mathf.Max(.01f, pixelError) : .75f;
            if (cache.Prepare(path, style, relativeTo, cameraData, limit, adaptiveSampling, error))
                (style.DepthTest ? depthBatch : overlayBatch).Append(cache);
            if (cache.Resampled) LastResampledPathCount++;
        }

        /// <summary>Loads a build-included material and owns two runtime variants, without modifying the asset.</summary>
        private bool EnsureResources()
        {
            if (depthMaterial != null) return true;
            if (resourceError) return false;
            var source = Resources.Load<Material>("UnityLines/LineStroke");
            if (source == null || source.shader == null || !source.shader.isSupported)
            {
                resourceError = true;
                Debug.LogError("LineDrawing needs the included Resources/UnityLines/LineStroke material and a supported URP shader.", this);
                return false;
            }
            depthMaterial = new Material(source) { name = "Line strokes (depth)", renderQueue = 3000 };
            overlayMaterial = new Material(source) { name = "Line strokes (overlay)", renderQueue = 3100 };
            depthMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            overlayMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            depthBatch = new LineMeshBatch("Line strokes (depth)");
            overlayBatch = new LineMeshBatch("Line strokes (overlay)");
            return true;
        }

        /// <summary>Discards frame-only submissions while preserving retained paths for re-enabling.</summary>
        private void OnDisable() { frameCount = 0; submittedFrame = -1; LastVertexCount = 0; }

        /// <summary>Releases all handles and owned GPU resources on scene unload or explicit destruction.</summary>
        private void OnDestroy()
        {
            Clear();
            depthBatch?.Dispose(); overlayBatch?.Dispose();
            if (depthMaterial != null) Destroy(depthMaterial);
            if (overlayMaterial != null) Destroy(overlayMaterial);
        }
    }
}
