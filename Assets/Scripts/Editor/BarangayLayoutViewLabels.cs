using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the names on a BarangayLayoutView in the Scene view: the essential places, the evacuation sites (marked
/// "[evacuation?]" until confirmed) and the main roads. Kept in the editor so the game code never needs UnityEditor.
/// </summary>
static class BarangayLayoutViewLabels
{
    static GUIStyle plain, bold;

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
    static void Draw(BarangayLayoutView view, GizmoType type)
    {
        if (!view.showLabels || view.Labels.Count == 0) return;
        var sceneView = SceneView.currentDrawingSceneView;
        var cam = sceneView != null ? sceneView.camera : Camera.current;
        if (cam == null) return;

        if (plain == null)
        {
            var background = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));             // so names read over the lines
            background.Apply();
            plain = new GUIStyle(EditorStyles.miniLabel) { padding = new RectOffset(3, 3, 1, 1) };
            plain.normal.background = background;
            bold = new GUIStyle(plain) { fontStyle = FontStyle.Bold, fontSize = plain.fontSize + 1 };
        }

        float maxDistance = view.labelDistance * view.labelDistance;
        Vector3 lift = Vector3.up * view.markerSize * 1.5f;
        foreach (var label in view.Labels)
        {
            Vector3 world = view.transform.TransformPoint(view.ToLocal(label.position) + lift);
            if (!cam.orthographic && !label.important && (world - cam.transform.position).sqrMagnitude > maxDistance) continue;
            Vector3 viewport = cam.WorldToViewportPoint(world);
            if (viewport.z < 0f || viewport.x < -0.1f || viewport.x > 1.1f || viewport.y < -0.1f || viewport.y > 1.1f) continue;

            var style = label.important ? bold : plain;
            style.normal.textColor = label.color;
            Handles.Label(world, label.text, style);
        }
    }
}
