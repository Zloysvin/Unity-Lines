# Basic Gallery

1. Use an empty scene in a Unity 6 URP project.
2. Drag `BasicGallery.prefab` into the scene.
3. Press **Play** and open **Game** view.

No references need assigning. The prefab creates its own camera and drawer at runtime. It shows a cyan segment, yellow cubic Bezier, green circle, orange arc, white quadratic Bezier, and an animated cyan segment below them. The white curve uses world-unit width; the others use pixel width.

Read `BasicGallery.cs` for retained and frame-only usage. To draw in your own scene, add **Unity Lines > Line Drawing** to a GameObject and assign your camera; the gallery is only an example.
