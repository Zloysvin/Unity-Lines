using System.Collections.Generic;
using UnityEngine;

namespace UnityLines
{
    /// <summary>Per-path tessellation and shader-input cache; camera movement normally changes only GPU projection.</summary>
    internal sealed class LineStrokeCache
    {
        internal readonly List<Vector3> Vertices = new List<Vector3>();
        internal readonly List<Vector3> Previous = new List<Vector3>();
        internal readonly List<Vector3> Next = new List<Vector3>();
        internal readonly List<Vector4> Data = new List<Vector4>();
        internal readonly List<int> Indices = new List<int>();
        private readonly List<Vector3> samples = new List<Vector3>();
        private readonly List<Vector3> localSamples = new List<Vector3>();
        private readonly List<Vector3> section = new List<Vector3>();
        private int sourceVersion = -1, cameraRevision = -1;
        private Matrix4x4 matrix;
        private Bounds bounds;
        private float density;
        private bool wasClipped, initialized;
        internal Bounds RenderBounds;
        internal LineStyle Style;
        internal int Revision;
        internal bool Resampled;

        internal bool Prepare(LinePath path, LineStyle style, Transform relativeTo, LineCamera camera, float limit, bool adaptive, float pixelError)
        {
            Resampled = false;
            if (path.Count < 2 || style.Color.a <= 0) return false;
            Matrix4x4 transform = relativeTo != null ? relativeTo.localToWorldMatrix : Matrix4x4.identity;
            bool sourceChanged = sourceVersion != path.Version;
            bool changed = sourceChanged || !matrix.Equals(transform);
            if (changed) bounds = LineCamera.TransformBounds(path.LocalBounds, transform);
            RenderBounds = camera.Inflated(bounds, style, limit);
            if (!Finite(RenderBounds.center) || !Finite(RenderBounds.extents) || !camera.Visible(RenderBounds)) return false;
            float desired = adaptive && path.AdaptiveSampling && path.Curves.Count > 0 ? camera.PixelsPerUnit(bounds) * ScaleBound(transform) / pixelError : 0;
            float nextDensity = density;
            // Refine as soon as error could exceed the target. Coarsen with hysteresis.
            if (desired == 0) nextDensity = 0;
            else if (desired > density || desired < density * .4f)
                nextDensity = Mathf.Pow(2, Mathf.Ceil(Mathf.Log(Mathf.Max(desired, .000001f), 2)));
            bool tessellated = sourceChanged || nextDensity != density || !initialized;
            bool sampled = changed || tessellated;
            if (tessellated)
            {
                Sample(path, nextDensity > 0 ? 1 / nextDensity : 0);
                density = nextDensity; Resampled = true;
            }
            if (sampled)
            {
                samples.Clear();
                foreach (var point in localSamples) samples.Add(transform.MultiplyPoint3x4(point));
                sourceVersion = path.Version; matrix = transform;
            }
            bool clipped = !camera.InsideDepth(bounds);
            bool styleChanged = !Style.Color.Equals(style.Color) || Style.Width != style.Width || Style.WidthSpace != style.WidthSpace;
            if (sampled || styleChanged || !initialized || clipped != wasClipped || (clipped && cameraRevision != camera.Revision))
            {
                Style = style; Vertices.Clear(); Previous.Clear(); Next.Clear(); Data.Clear(); Indices.Clear(); section.Clear();
                if (!clipped) { foreach (var p in samples) AddPoint(p); Emit(path.Closed); }
                else Clip(path.Closed, camera);
                Revision++; initialized = true; wasClipped = clipped; cameraRevision = camera.Revision;
            }
            return Vertices.Count > 0;
        }
        private static bool Finite(Vector3 p) => LinePath.IsFinite(p.x) && LinePath.IsFinite(p.y) && LinePath.IsFinite(p.z);
        private static float ScaleBound(Matrix4x4 matrix)
        {
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            float xy = Mathf.Abs(Vector3.Dot(x, y)), xz = Mathf.Abs(Vector3.Dot(x, z)), yz = Mathf.Abs(Vector3.Dot(y, z));
            return Mathf.Sqrt(Mathf.Max(x.sqrMagnitude + xy + xz, Mathf.Max(y.sqrMagnitude + xy + yz, z.sqrMagnitude + xz + yz)));
        }
        private void Sample(LinePath path, float tolerance)
        {
            localSamples.Clear();
            if (tolerance <= 0 || path.Curves.Count == 0)
            { for (int i = 0; i < path.Count; i++) localSamples.Add(path[i]); return; }
            localSamples.Add(path[0]);
            int curveIndex = 0, point = 0;
            while (point < path.Count - 1)
            {
                if (curveIndex < path.Curves.Count && path.Curves[curveIndex].First == point)
                {
                    var curve = path.Curves[curveIndex++];
                    bool fullCircle = curve.Arc && curve.Last - curve.First == curve.Budget - 1;
                    bool ring = fullCircle && path.Closed && path.Curves.Count == 1 && path.Count == curve.Budget;
                    if (fullCircle && !ring) { curve.Angle *= (curve.Budget - 1f) / curve.Budget; curve.Budget--; }
                    curve.Sample(localSamples, Matrix4x4.identity, tolerance, ring); point = path.Curves[curveIndex - 1].Last;
                }
                else localSamples.Add(path[++point]);
            }
        }
        private void AddPoint(Vector3 point)
        {
            if (!Finite(point)) return;
            if (section.Count == 0 || (section[section.Count - 1] - point).sqrMagnitude > 1e-12f) section.Add(point);
        }
        private void Clip(bool closed, LineCamera camera)
        {
            int count = samples.Count, start = 0;
            if (closed) for (int i = 0; i < count; i++)
            { float z = -camera.View.MultiplyPoint3x4(samples[i]).z; if (z < camera.Near || z > camera.Far) { start = i; break; } }
            bool broke = false;
            for (int i = 0; i < (closed ? count : count - 1); i++)
            {
                Vector3 a = samples[(start + i) % count], b = samples[(start + i + 1) % count];
                float da = -camera.View.MultiplyPoint3x4(a).z, db = -camera.View.MultiplyPoint3x4(b).z;
                float begin = 0, end = 1, delta = db - da;
                if (Mathf.Abs(delta) < 1e-8f)
                { if (da < camera.Near || da > camera.Far) { Emit(false); section.Clear(); broke = true; continue; } }
                else
                {
                    float t0 = (camera.Near - da) / delta, t1 = (camera.Far - da) / delta;
                    begin = Mathf.Max(0, Mathf.Min(t0, t1)); end = Mathf.Min(1, Mathf.Max(t0, t1));
                    if (begin > end) { Emit(false); section.Clear(); broke = true; continue; }
                }
                if (begin > 0) { Emit(false); section.Clear(); broke = true; }
                AddPoint(Vector3.LerpUnclamped(a, b, begin)); AddPoint(Vector3.LerpUnclamped(a, b, end));
                if (end < 1) { Emit(false); section.Clear(); broke = true; }
            }
            Emit(closed && !broke);
        }
        private void Emit(bool closed)
        {
            int count = section.Count;
            if (closed && count > 1 && (section[0] - section[count - 1]).sqrMagnitude < 1e-12f) { section.RemoveAt(count - 1); count--; }
            if (count < 2) return;
            closed &= count >= 3;
            int first = Vertices.Count;
            for (int i = 0; i < count; i++)
            {
                var previous = section[closed ? (i - 1 + count) % count : Mathf.Max(0, i - 1)];
                var next = section[closed ? (i + 1) % count : Mathf.Min(count - 1, i + 1)];
                int flags = closed ? 0 : (i == 0 ? 1 : 0) | (i == count - 1 ? 2 : 0) | (i == 1 ? 4 : 0) | (i == count - 2 ? 8 : 0);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vertices.Add(section[i]); Previous.Add(previous); Next.Add(next);
                    Data.Add(new Vector4(side, Style.Width, Style.WidthSpace == LineWidthSpace.World ? 1 : 0, flags));
                }
            }
            for (int i = 0; i < (closed ? count : count - 1); i++)
            {
                int a = first + i * 2, b = first + ((i + 1) % count) * 2;
                Indices.Add(a); Indices.Add(a + 1); Indices.Add(b); Indices.Add(b); Indices.Add(a + 1); Indices.Add(b + 1);
            }
        }
    }
}
