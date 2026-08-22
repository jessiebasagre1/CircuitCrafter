#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Drag-to-position editor for RufusSetupStepData.
///
/// Select a step asset and its background screenshot is drawn right in the Inspector with the
/// step's rects on top of it. Drag a box to move it, drag its bottom-right corner to resize.
/// The numbers are written straight back to the asset.
///
/// WHY THIS EXISTS RATHER THAN "LINE IT UP IN PLAY MODE"
/// Positioning these in Play mode does not work, for two reasons that are easy to miss:
///
///   1. Play-mode edits are DISCARDED on exit. Every nudge is thrown away the moment you press
///      stop, which is the worst possible way to find that out.
///
///   2. Dragging a RectTransform in the Scene view changes its OFFSETS, not its anchors. The
///      view positions these controls purely by anchor with zero offsets, so a dragged control
///      moves on screen while its anchors - the numbers you would have read back - stay exactly
///      where they were. You would copy the old values and wonder why nothing changed.
///
/// Editing the asset directly sidesteps both. It also works with the panel switched off, with
/// the scene unopened, and it survives everything, because an asset is not part of Play mode.
/// </summary>
[CustomEditor(typeof(RufusSetupStepData))]
public class RufusSetupStepDataEditor : Editor
{
    private const float CornerGrip = 14f;
    private const float MaxPreviewHeight = 420f;

    private string _dragPath;
    private bool _resizing;
    private Vector2 _grabOffset;

    private static readonly Color HotspotFill = new Color(0.20f, 0.85f, 0.40f, 0.22f);
    private static readonly Color HotspotLine = new Color(0.20f, 0.95f, 0.45f, 1f);
    private static readonly Color DecoyFill = new Color(0.95f, 0.30f, 0.30f, 0.22f);
    private static readonly Color DecoyLine = new Color(1f, 0.35f, 0.35f, 1f);
    private static readonly Color FieldFill = new Color(0.30f, 0.60f, 1f, 0.22f);
    private static readonly Color FieldLine = new Color(0.40f, 0.70f, 1f, 1f);
    private static readonly Color TimedFill = new Color(1f, 0.80f, 0.25f, 0.22f);
    private static readonly Color TimedLine = new Color(1f, 0.85f, 0.35f, 1f);

    private struct Handle
    {
        public string path;
        public string label;
        public Color fill;
        public Color line;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        serializedObject.Update();
        DrawPositioner();
        serializedObject.ApplyModifiedProperties();
    }

    // ==================================================================

    private void DrawPositioner()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Position on the screenshot", EditorStyles.boldLabel);

        var spriteProp = serializedObject.FindProperty("background");
        var sprite = spriteProp != null ? spriteProp.objectReferenceValue as Sprite : null;

        if (sprite == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a Background sprite above and the screenshot appears here with the step's " +
                "rects drawn on it. Drag a box to move it; drag its bottom-right corner to resize.",
                MessageType.Info);
            return;
        }

        var handles = HandlesForKind();
        if (handles.Count == 0)
        {
            EditorGUILayout.HelpBox("This step kind has no positionable rects.", MessageType.None);
            return;
        }

        Rect preview = ReservePreviewRect(sprite);
        DrawSprite(preview, sprite);

        // Events first, drawing second: handling a drag after painting would show the box one
        // frame behind the cursor, which feels broken even though the numbers are right.
        HandleMouse(preview, handles);

        foreach (var h in handles)
        {
            var prop = serializedObject.FindProperty(h.path);
            if (prop == null) continue;

            Rect box = ToScreen(preview, prop.rectValue);
            DrawBox(box, h.fill, h.line, h.label);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Drag a box to move it. Drag its bottom-right corner to resize. Coordinates are 0-1 " +
            "across the image from the TOP-LEFT, and the fields above update as you drag - type " +
            "in them directly if you want exact numbers.",
            MessageType.None);

        DrawDecoyControls();
    }

    private List<Handle> HandlesForKind()
    {
        var list = new List<Handle>();

        var kindProp = serializedObject.FindProperty("kind");
        if (kindProp == null) return list;

        switch ((RufusSetupStepData.StepKind)kindProp.enumValueIndex)
        {
            case RufusSetupStepData.StepKind.ClickHotspot:
                list.Add(new Handle { path = "hotspot", label = "Hotspot (correct)", fill = HotspotFill, line = HotspotLine });

                // Only offer the decoy when it actually has a size - a zero-size decoy is how a
                // step says "this screen has one button", and drawing a handle for it would
                // invite someone to create one by accident.
                var decoy = serializedObject.FindProperty("decoyHotspot");
                if (decoy != null && decoy.rectValue.width > 0f && decoy.rectValue.height > 0f)
                    list.Add(new Handle { path = "decoyHotspot", label = "Decoy (wrong)", fill = DecoyFill, line = DecoyLine });
                break;

            case RufusSetupStepData.StepKind.TypeUrl:
                list.Add(new Handle { path = "inputRect", label = "Address bar", fill = FieldFill, line = FieldLine });
                list.Add(new Handle { path = "goRect", label = "Go", fill = FieldFill, line = FieldLine });
                break;

            case RufusSetupStepData.StepKind.Timed:
                list.Add(new Handle { path = "progressRect", label = "Progress bar", fill = TimedFill, line = TimedLine });
                break;
        }

        return list;
    }

    private void DrawDecoyControls()
    {
        var kindProp = serializedObject.FindProperty("kind");
        if (kindProp == null) return;
        if ((RufusSetupStepData.StepKind)kindProp.enumValueIndex != RufusSetupStepData.StepKind.ClickHotspot)
            return;

        var decoy = serializedObject.FindProperty("decoyHotspot");
        if (decoy == null) return;

        bool has = decoy.rectValue.width > 0f && decoy.rectValue.height > 0f;

        if (!has && GUILayout.Button("Add a decoy (a wrong button on this screen)"))
            decoy.rectValue = new Rect(0.57f, 0.62f, 0.13f, 0.06f);

        if (has && GUILayout.Button("Remove the decoy"))
            decoy.rectValue = new Rect(0f, 0f, 0f, 0f);
    }

    // ==================================================================
    // Preview
    // ==================================================================

    private static Rect ReservePreviewRect(Sprite sprite)
    {
        float aspect = sprite.rect.height / Mathf.Max(1f, sprite.rect.width);

        float w = EditorGUIUtility.currentViewWidth - 40f;
        float h = w * aspect;

        if (h > MaxPreviewHeight)
        {
            h = MaxPreviewHeight;
            w = h / Mathf.Max(0.0001f, aspect);
        }

        Rect line = GUILayoutUtility.GetRect(w, h);

        // Centre it if the max-height clamp made it narrower than the inspector.
        return new Rect(line.x + (line.width - w) * 0.5f, line.y, w, h);
    }

    private static void DrawSprite(Rect preview, Sprite sprite)
    {
        var tex = sprite.texture;
        if (tex == null) return;

        // Draw through the sprite's own texture rect rather than the whole texture, so a sprite
        // sliced out of a sheet still previews correctly instead of showing the entire sheet.
        Rect tr = sprite.textureRect;
        var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);

        GUI.DrawTextureWithTexCoords(preview, tex, uv);
    }

    private static void DrawBox(Rect r, Color fill, Color line, string label)
    {
        EditorGUI.DrawRect(r, fill);

        const float t = 2f;
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), line);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), line);
        EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), line);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), line);

        // Corner grip
        EditorGUI.DrawRect(new Rect(r.xMax - CornerGrip, r.yMax - CornerGrip, CornerGrip, CornerGrip), line);

        var style = new GUIStyle(EditorStyles.miniBoldLabel);
        style.normal.textColor = line;
        GUI.Label(new Rect(r.x + 3f, r.y - 15f, 220f, 14f), label, style);
    }

    // ==================================================================
    // Dragging
    // ==================================================================

    private void HandleMouse(Rect preview, List<Handle> handles)
    {
        Event e = Event.current;

        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button != 0 || !preview.Contains(e.mousePosition)) break;

                // Back to front, so the box drawn on top is the one you grab.
                for (int i = handles.Count - 1; i >= 0; i--)
                {
                    var prop = serializedObject.FindProperty(handles[i].path);
                    if (prop == null) continue;

                    Rect box = ToScreen(preview, prop.rectValue);

                    var corner = new Rect(box.xMax - CornerGrip, box.yMax - CornerGrip, CornerGrip, CornerGrip);
                    if (corner.Contains(e.mousePosition))
                    {
                        _dragPath = handles[i].path;
                        _resizing = true;
                        e.Use();
                        return;
                    }

                    if (box.Contains(e.mousePosition))
                    {
                        _dragPath = handles[i].path;
                        _resizing = false;
                        _grabOffset = e.mousePosition - new Vector2(box.x, box.y);
                        e.Use();
                        return;
                    }
                }
                break;

            case EventType.MouseDrag:
                if (string.IsNullOrEmpty(_dragPath)) break;

                var target = serializedObject.FindProperty(_dragPath);
                if (target == null) break;

                Rect n = target.rectValue;

                if (_resizing)
                {
                    float right = (e.mousePosition.x - preview.x) / preview.width;
                    float bottom = (e.mousePosition.y - preview.y) / preview.height;

                    // A minimum size keeps a box from collapsing to nothing and becoming
                    // impossible to grab again.
                    n.width = Mathf.Clamp(right - n.x, 0.01f, 1f - n.x);
                    n.height = Mathf.Clamp(bottom - n.y, 0.01f, 1f - n.y);
                }
                else
                {
                    float x = (e.mousePosition.x - _grabOffset.x - preview.x) / preview.width;
                    float y = (e.mousePosition.y - _grabOffset.y - preview.y) / preview.height;

                    n.x = Mathf.Clamp(x, 0f, 1f - n.width);
                    n.y = Mathf.Clamp(y, 0f, 1f - n.height);
                }

                target.rectValue = Round(n);
                e.Use();
                Repaint();
                break;

            case EventType.MouseUp:
                if (!string.IsNullOrEmpty(_dragPath))
                {
                    _dragPath = null;
                    _resizing = false;
                    e.Use();
                }
                break;
        }
    }

    private static Rect ToScreen(Rect preview, Rect n)
    {
        return new Rect(
            preview.x + n.x * preview.width,
            preview.y + n.y * preview.height,
            n.width * preview.width,
            n.height * preview.height);
    }

    /// <summary>Three decimals is about a pixel on a 1920-wide screenshot - past that the extra
    /// digits are noise that make the asset harder to read in a diff.</summary>
    private static Rect Round(Rect r)
    {
        return new Rect(
            Mathf.Round(r.x * 1000f) / 1000f,
            Mathf.Round(r.y * 1000f) / 1000f,
            Mathf.Round(r.width * 1000f) / 1000f,
            Mathf.Round(r.height * 1000f) / 1000f);
    }
}
#endif
