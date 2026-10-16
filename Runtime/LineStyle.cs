using System;
using UnityEngine;

namespace UnityLines
{
    /// <summary>Units used for the full width of a camera-facing line stroke.</summary>
    public enum LineWidthSpace { Pixels, World }

    /// <summary>Appearance of a line/path; independent of its points or the gameplay system that owns it.</summary>
    [Serializable]
    public struct LineStyle
    {
        public Color Color;
        public float Width;
        public LineWidthSpace WidthSpace;
        public bool DepthTest;

        /// <summary>Creates a stroke. Depth-tested strokes are hidden by opaque scene geometry.</summary>
        public LineStyle(Color color, float width = 2, LineWidthSpace widthSpace = LineWidthSpace.Pixels, bool depthTest = true)
        { Color = color; Width = width; WidthSpace = widthSpace; DepthTest = depthTest; }

        /// <summary>Rejects invalid input before it reaches mesh generation.</summary>
        internal void Validate()
        {
            if (!LinePath.IsFinite(Width) || Width <= 0 || !LinePath.IsFinite(Color.r) || !LinePath.IsFinite(Color.g)
                || !LinePath.IsFinite(Color.b) || !LinePath.IsFinite(Color.a))
                throw new ArgumentException("Line width must be positive and all style values finite.");
            if (WidthSpace != LineWidthSpace.Pixels && WidthSpace != LineWidthSpace.World)
                throw new ArgumentOutOfRangeException(nameof(WidthSpace));
        }
    }
}
