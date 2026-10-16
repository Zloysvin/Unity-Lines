using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityLines
{
    /// <summary>Reusable sampled geometry for a single continuous line. Curves are mathematical inputs sampled into this buffer.</summary>
    public sealed class LinePath
    {
        private readonly List<Vector3> points = new List<Vector3>();
        internal readonly List<LineCurve> Curves = new List<LineCurve>();
        internal int Version { get; private set; }
        private int boundsVersion = -1;
        private Bounds bounds;
        private bool adaptiveSampling = true;
        /// <summary>Allows camera-dependent detail for mathematical curves; explicit polyline corners are preserved.</summary>
        public bool AdaptiveSampling { get => adaptiveSampling; set { if (adaptiveSampling != value) { adaptiveSampling = value; Version++; } } }
        internal Bounds LocalBounds
        {
            get
            {
                if (boundsVersion == Version) return bounds;
                bounds = new Bounds(points.Count > 0 ? points[0] : Vector3.zero, Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);
                foreach (var curve in Curves)
                    if (curve.Arc) { Vector3 extent = LineCamera.Abs(curve.B) + LineCamera.Abs(curve.C); bounds.Encapsulate(curve.A - extent); bounds.Encapsulate(curve.A + extent); }
                    else { bounds.Encapsulate(curve.B); bounds.Encapsulate(curve.C); }
                boundsVersion = Version; return bounds;
            }
        }
        /// <summary>Number of sampled points, excluding any implicit closing segment.</summary>
        public int Count => points.Count;
        /// <summary>Whether the last point connects back to the first.</summary>
        public bool Closed { get; private set; }
        /// <summary>Reads a sampled point without allocating or exposing the mutable backing list.</summary>
        public Vector3 this[int index] => points[index];

        /// <summary>Empties the path while retaining its allocated capacity.</summary>
        public void Clear() { points.Clear(); Curves.Clear(); Closed = false; Version++; }

        /// <summary>Replaces the path with one straight segment.</summary>
        public void SetLine(Vector3 start, Vector3 end)
        {
            ValidatePoint(start); ValidatePoint(end);
            Clear(); points.Add(start); points.Add(end);
        }

        /// <summary>Copies an ordered point list. Empty/single-point paths are allowed and simply draw nothing.</summary>
        public void SetPolyline(IReadOnlyList<Vector3> source, bool closed = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            for (int i = 0; i < source.Count; i++) ValidatePoint(source[i]);
            Clear();
            for (int i = 0; i < source.Count; i++) points.Add(source[i]);
            Closed = closed;
        }

        /// <summary>Copies another reusable path, preserving closure.</summary>
        public void CopyFrom(LinePath source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(source, this)) return;
            Clear(); points.AddRange(source.points); Curves.AddRange(source.Curves); Closed = source.Closed; adaptiveSampling = source.adaptiveSampling;
        }

        /// <summary>Starts an open path with one point, for subsequent AddPoint/BezierTo calls.</summary>
        public void Begin(Vector3 start) { ValidatePoint(start); Clear(); points.Add(start); }

        /// <summary>Appends a point to the current path.</summary>
        public void AddPoint(Vector3 point) { ValidatePoint(point); points.Add(point); Version++; }

        /// <summary>Enables/disables the implicit final segment without duplicating points.</summary>
        public void SetClosed(bool closed) { if (Closed != closed) { Closed = closed; Version++; } }

        /// <summary>Samples a cubic Bezier from start to end using two control points and an explicit segment budget.</summary>
        public void SetBezier(Vector3 start, Vector3 control1, Vector3 control2, Vector3 end, int segments = 48)
        {
            ValidatePoint(start); ValidatePoint(control1); ValidatePoint(control2); ValidatePoint(end); ValidateSegments(segments);
            Begin(start); BezierTo(control1, control2, end, segments);
        }

        /// <summary>Appends a cubic Bezier starting at the last path point, enabling connected multi-curve paths.</summary>
        public void BezierTo(Vector3 control1, Vector3 control2, Vector3 end, int segments = 48)
        {
            if (points.Count == 0) throw new InvalidOperationException("Begin the path before appending a curve.");
            ValidatePoint(control1); ValidatePoint(control2); ValidatePoint(end); ValidateSegments(segments);
            Vector3 start = points[points.Count - 1];
            Curves.Add(new LineCurve { First = points.Count - 1, Last = points.Count - 1 + segments, A = start, B = control1, C = control2, D = end, Budget = segments });
            Version++;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                // De Casteljau avoids large polynomial coefficients and retains endpoint precision.
                Vector3 a = Vector3.LerpUnclamped(start, control1, t);
                Vector3 b = Vector3.LerpUnclamped(control1, control2, t);
                Vector3 c = Vector3.LerpUnclamped(control2, end, t);
                points.Add(Vector3.LerpUnclamped(Vector3.LerpUnclamped(a, b, t), Vector3.LerpUnclamped(b, c, t), t));
            }
        }

        /// <summary>Samples a quadratic Bezier by converting its single control point to cubic controls.</summary>
        public void SetQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, int segments = 32)
        {
            ValidatePoint(start); ValidatePoint(control); ValidatePoint(end);
            SetBezier(start, Vector3.LerpUnclamped(start, control, 2f / 3), Vector3.LerpUnclamped(end, control, 2f / 3), end, segments);
        }

        /// <summary>Samples an arc in an arbitrary plane; startDirection is projected onto that plane and angle is signed degrees.</summary>
        public void SetArc(Vector3 center, Vector3 normal, Vector3 startDirection, float radius, float angleDegrees, int segments = 64)
        {
            ValidatePoint(center); ValidatePoint(normal); ValidatePoint(startDirection); ValidateSegments(segments);
            if (!IsFinite(radius) || radius <= 0 || !IsFinite(angleDegrees) || Mathf.Abs(angleDegrees) > 360)
                throw new ArgumentException("Arc radius must be positive and sweep must be within -360..360 degrees.");
            if (normal.sqrMagnitude < 0.000001f) throw new ArgumentException("Arc normal must be nonzero.");
            normal.Normalize();
            Vector3 radial = Vector3.ProjectOnPlane(startDirection, normal);
            if (radial.sqrMagnitude < 0.000001f) throw new ArgumentException("Arc start direction must not be parallel to its normal.");
            radial = radial.normalized * radius;
            bool closed = Mathf.Approximately(Mathf.Abs(angleDegrees), 360);
            if (closed && segments < 3) throw new ArgumentOutOfRangeException(nameof(segments), "A circle needs at least three segments.");
            Clear();
            Vector3 tangent = Vector3.Cross(normal, radial);
            Curves.Add(new LineCurve { Arc = true, First = 0, Last = closed ? segments - 1 : segments, A = center, B = radial, C = tangent, Angle = angleDegrees * Mathf.Deg2Rad, Budget = segments });
            for (int i = 0; i < (closed ? segments : segments + 1); i++)
            {
                float angle = angleDegrees * Mathf.Deg2Rad * i / segments;
                points.Add(center + radial * Mathf.Cos(angle) + tangent * Mathf.Sin(angle));
            }
            Closed = closed;
        }

        /// <summary>Samples a closed circle in any plane, with a stable automatically selected start direction.</summary>
        public void SetCircle(Vector3 center, Vector3 normal, float radius, int segments = 96)
        {
            ValidatePoint(normal);
            Vector3 reference = Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            SetArc(center, normal, reference, radius, 360, segments);
        }

        /// <summary>Checks a coordinate before it can poison mesh bounds or projection.</summary>
        internal static void ValidatePoint(Vector3 point)
        {
            if (!IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z)) throw new ArgumentException("Line points must be finite.");
        }
        /// <summary>Whether a scalar can be used in geometry.</summary>
        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        /// <summary>Bounds per-curve work while allowing callers to choose their visual sampling quality.</summary>
        private static void ValidateSegments(int segments)
        {
            if (segments < 1 || segments > 4096) throw new ArgumentOutOfRangeException(nameof(segments), "Use 1..4096 segments per curve.");
        }
    }
}
