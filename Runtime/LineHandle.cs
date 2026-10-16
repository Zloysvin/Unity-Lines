using System;
using UnityEngine;

namespace UnityLines
{
    /// <summary>A retained line owned by one drawer. Dispose it when its gameplay owner no longer needs it.</summary>
    public sealed class LineHandle : IDisposable
    {
        private LineDrawing owner;
        private LineStyle style;
        internal readonly LineStrokeCache Cache = new LineStrokeCache();
        /// <summary>Reusable points/curve buffer. Edits become visible on the next rendered frame.</summary>
        public LinePath Path { get; } = new LinePath();
        /// <summary>Stroke appearance; validated on assignment.</summary>
        public LineStyle Style { get => style; set { value.Validate(); style = value; } }
        /// <summary>Optional transform whose local space contains Path points; null means world coordinates.</summary>
        public Transform RelativeTo { get; set; }
        /// <summary>Hides the path without deleting its points or registration.</summary>
        public bool Visible { get; set; } = true;
        /// <summary>Whether the handle has been removed or its renderer destroyed.</summary>
        public bool IsDisposed => owner == null;

        /// <summary>Creates a handle through LineDrawing.CreatePath.</summary>
        internal LineHandle(LineDrawing drawer, LineStyle appearance, Transform relativeTo)
        { owner = drawer; Style = appearance; RelativeTo = relativeTo; }

        /// <summary>Removes this path; safe to call repeatedly or after scene unload.</summary>
        public void Dispose()
        {
            if (owner != null) owner.Remove(this);
            owner = null;
        }
        /// <summary>Invalidates a handle when its drawing owner is cleared.</summary>
        internal void Release() { owner = null; }
    }
}
