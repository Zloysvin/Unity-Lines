using UnityEngine;

namespace UnityLines
{
    /// <summary>Camera matrices, clipping planes and pixel scales read once per drawer/frame.</summary>
    internal sealed class LineCamera
    {
        internal Matrix4x4 View;
        private Matrix4x4 projection;
        private readonly Plane[] planes = new Plane[6];
        internal float Near, Far;
        internal int Revision;
        private bool orthographic;
        private float px, py;
        internal Vector4 Viewport;
        internal void Update(Camera camera)
        {
            var view = camera.worldToCameraMatrix; var proj = camera.projectionMatrix;
            float near = camera.nearClipPlane + Mathf.Max(.0001f, camera.nearClipPlane * .001f), far = camera.farClipPlane;
            if (!View.Equals(view) || !projection.Equals(proj) || Near != near || Far != far) Revision++;
            View = view; projection = proj; Near = near; Far = far; orthographic = camera.orthographic;
            Viewport = new Vector4(camera.pixelWidth, camera.pixelHeight, 0, 0);
            px = Mathf.Abs(proj.m00) * Viewport.x * .5f; py = Mathf.Abs(proj.m11) * Viewport.y * .5f;
            GeometryUtility.CalculateFrustumPlanes(proj * view, planes);
        }
        internal static Vector3 Abs(Vector3 p) => new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z));
        internal static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 e = bounds.extents;
            Vector3 extent = Abs(matrix.MultiplyVector(new Vector3(e.x, 0, 0))) + Abs(matrix.MultiplyVector(new Vector3(0, e.y, 0))) + Abs(matrix.MultiplyVector(new Vector3(0, 0, e.z)));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), extent * 2);
        }
        internal bool Visible(Bounds bounds)
        {
            Vector3 c = bounds.center, e = bounds.extents;
            for (int i = 0; i < 6; i++)
                if (Vector3.Dot(planes[i].normal, c) + planes[i].distance + Vector3.Dot(Abs(planes[i].normal), e) < 0) return false;
            return true;
        }
        internal bool InsideDepth(Bounds worldBounds)
        { Bounds b = TransformBounds(worldBounds, View); return -b.center.z - b.extents.z >= Near && -b.center.z + b.extents.z <= Far; }
        // Upper bound for the projection's derivative over the entire curve bounds.
        // This converts world-space chord error to a conservative screen-space error.
        internal float PixelsPerUnit(Bounds worldBounds)
        {
            if (orthographic) return Mathf.Max(px, py);
            Bounds b = TransformBounds(worldBounds, View);
            float z = Mathf.Max(Near, -b.center.z - b.extents.z);
            float x = Mathf.Abs(b.center.x) + b.extents.x, y = Mathf.Abs(b.center.y) + b.extents.y;
            return Mathf.Max(px, py) / z * Mathf.Sqrt(2 + (x * x + y * y) / (z * z));
        }
        internal Bounds Inflated(Bounds bounds, LineStyle style, float miter)
        {
            Bounds b = TransformBounds(bounds, View);
            float worldPixel = (orthographic ? 1 : Mathf.Max(Near, -b.center.z + b.extents.z)) / Mathf.Max(.0001f, Mathf.Min(px, py));
            float radius = style.WidthSpace == LineWidthSpace.World ? style.Width * .5f : style.Width * .5f * worldPixel;
            bounds.Expand(2 * ((radius + worldPixel) * miter + worldPixel));
            return bounds;
        }
    }
}
