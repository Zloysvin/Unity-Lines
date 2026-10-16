using System.Collections.Generic;
using UnityEngine;

namespace UnityLines
{
    /// <summary>Mathematical source of a sampled span; retains controls so LOD can add detail after zooming in.</summary>
    internal struct LineCurve
    {
        internal bool Arc;
        internal int First, Last, Budget;
        internal Vector3 A, B, C, D;
        internal float Angle;

        internal void Sample(List<Vector3> output, Matrix4x4 matrix, float tolerance, bool closed)
        {
            Vector3 a = matrix.MultiplyPoint3x4(A);
            if (Arc)
            {
                Vector3 u = matrix.MultiplyVector(B), v = matrix.MultiplyVector(C);
                float radiusBound = u.magnitude + v.magnitude;
                float step = 2 * Mathf.Acos(Mathf.Clamp(1 - tolerance / Mathf.Max(radiusBound, .000001f), -1, 1));
                int count = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(Angle) / Mathf.Max(step, .000001f)), Mathf.Min(closed ? 4 : 1, Budget), Budget);
                // The first point is already in output; full circles omit the repeated endpoint.
                for (int i = 1; i < (closed ? count : count + 1); i++)
                { float angle = Angle * i / count; output.Add(a + u * Mathf.Cos(angle) + v * Mathf.Sin(angle)); }
            }
            else Subdivide(output, a, matrix.MultiplyPoint3x4(B), matrix.MultiplyPoint3x4(C), matrix.MultiplyPoint3x4(D), tolerance * tolerance, Budget);
        }
        private static float DistanceSquared(Vector3 p, Vector3 a, Vector3 d)
        { Vector3 delta = d - a; float size = delta.sqrMagnitude; return (p - (a + delta * (size > 0 ? Mathf.Clamp01(Vector3.Dot(p - a, delta) / size) : 0))).sqrMagnitude; }
        private static void Subdivide(List<Vector3> output, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float errorSquared, int budget)
        {
            if (budget <= 1 || Mathf.Max(DistanceSquared(b, a, d), DistanceSquared(c, a, d)) <= errorSquared) { output.Add(d); return; }
            Vector3 ab = (a + b) * .5f, bc = (b + c) * .5f, cd = (c + d) * .5f;
            Vector3 abc = (ab + bc) * .5f, bcd = (bc + cd) * .5f, middle = (abc + bcd) * .5f;
            int left = budget / 2;
            Subdivide(output, a, ab, abc, middle, errorSquared, left);
            Subdivide(output, middle, bcd, cd, d, errorSquared, budget - left);
        }
    }
}
