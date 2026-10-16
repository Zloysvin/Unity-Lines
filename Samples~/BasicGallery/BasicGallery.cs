using UnityEngine;

namespace UnityLines.Samples
{
    /// <summary>Drag the prefab into an empty URP scene and press Play. All positions are local to this object.</summary>
    [AddComponentMenu("Unity Lines/Samples/Basic Gallery")]
    public sealed class BasicGallery : MonoBehaviour
    {
        private GameObject demoRoot;
        private LineDrawing drawing;
        private float elapsed;

        private void OnEnable()
        {
            elapsed = 0;
            demoRoot = new GameObject("Gallery camera and drawing");
            demoRoot.transform.SetParent(transform, false);
            demoRoot.transform.localPosition = new Vector3(0, 0, -14);
            var camera = demoRoot.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .035f, .06f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.fieldOfView = 50;
            drawing = demoRoot.AddComponent<LineDrawing>();
            drawing.TargetCamera = camera;

            // Retained handles: set geometry once and keep it until disposal or Clear.
            drawing.CreatePath(new LineStyle(Color.cyan, 3), transform).Path
                .SetLine(new Vector3(-5, 3, 0), new Vector3(-1, 3, 0));

            drawing.CreatePath(new LineStyle(Color.yellow, 3), transform).Path
                .SetBezier(new Vector3(1, 2, 0), new Vector3(2, 6, 0),
                    new Vector3(4, 0, 0), new Vector3(5, 3, 0), segments: 128);

            drawing.CreatePath(new LineStyle(Color.green, 3), transform).Path
                .SetCircle(new Vector3(-3, 0, 0), Vector3.forward, 1.25f, segments: 128);

            drawing.CreatePath(new LineStyle(new Color(1, .4f, .15f), 3, depthTest: false), transform).Path
                .SetArc(new Vector3(3, 0, 0), Vector3.forward, Vector3.right,
                    radius: 1.25f, angleDegrees: 260, segments: 128);

            drawing.CreatePath(new LineStyle(Color.white, .06f, LineWidthSpace.World), transform).Path
                .SetQuadraticBezier(new Vector3(-5, -3, 0), new Vector3(0, -1, 0),
                    new Vector3(5, -3, 0), segments: 128);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            // Frame-only drawing: stop submitting and it disappears on the next frame.
            float x = Mathf.Sin(elapsed) * 4;
            drawing.DrawLine(new Vector3(x - .6f, -4, 0), new Vector3(x + .6f, -4, 0),
                new LineStyle(Color.cyan, 4, depthTest: false), transform);
        }

        private void OnDisable()
        {
            // This sample owns the entire drawer. Shared drawers should dispose only their own handles.
            if (demoRoot != null)
            {
                demoRoot.SetActive(false);
                Destroy(demoRoot);
            }
            demoRoot = null;
            drawing = null;
        }
    }
}
