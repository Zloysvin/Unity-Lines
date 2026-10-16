using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityLines
{
    /// <summary>Shared GPU stroke batch. Uploads only when its ordered cache entries or their revisions change.</summary>
    internal sealed class LineMeshBatch : IDisposable
    {
        private readonly Mesh mesh;
        private readonly List<LineStrokeCache> entries = new List<LineStrokeCache>();
        private readonly List<int> revisions = new List<int>();
        private readonly List<Vector3> vertices = new List<Vector3>(), previous = new List<Vector3>(), next = new List<Vector3>();
        private readonly List<Vector4> data = new List<Vector4>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        private int count;
        private bool dirty;
        private Bounds bounds;
        internal bool Uploaded { get; private set; }
        public int VertexCount { get; private set; }
        public LineMeshBatch(string name) { mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic(); }
        public void Clear() { count = 0; VertexCount = 0; dirty = false; Uploaded = false; }
        internal void Append(LineStrokeCache cache)
        {
            if (count == 0) bounds = cache.RenderBounds; else bounds.Encapsulate(cache.RenderBounds);
            if (count == entries.Count) { entries.Add(cache); revisions.Add(cache.Revision); dirty = true; }
            else if (!ReferenceEquals(entries[count], cache) || revisions[count] != cache.Revision)
            { entries[count] = cache; revisions[count] = cache.Revision; dirty = true; }
            count++; VertexCount += cache.Vertices.Count;
        }
        public void Render(Camera camera, Material material, int layer)
        {
            if (count != entries.Count)
            { entries.RemoveRange(count, entries.Count - count); revisions.RemoveRange(count, revisions.Count - count); dirty = true; }
            if (count == 0) return;
            if (dirty)
            {
                vertices.Clear(); previous.Clear(); next.Clear(); data.Clear(); colors.Clear(); triangles.Clear();
                foreach (var entry in entries)
                {
                    int start = vertices.Count;
                    vertices.AddRange(entry.Vertices); previous.AddRange(entry.Previous); next.AddRange(entry.Next); data.AddRange(entry.Data);
                    for (int i = 0; i < entry.Vertices.Count; i++) colors.Add(entry.Style.Color);
                    foreach (int index in entry.Indices) triangles.Add(start + index);
                }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, previous); mesh.SetUVs(1, next); mesh.SetUVs(2, data); mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0, false); mesh.bounds = bounds; Uploaded = true;
            }
            var parameters = new RenderParams(material) { camera = camera, layer = layer, worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, lightProbeUsage = LightProbeUsage.Off };
            Graphics.RenderMesh(parameters, mesh, 0, Matrix4x4.identity);
        }
        public void Dispose() { if (mesh != null) UnityEngine.Object.Destroy(mesh); }
    }
}
