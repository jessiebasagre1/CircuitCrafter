#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The widget factory for COC 1's recreated Windows UI - the Rufus window, the Windows Setup
/// screens, and the post-install desktop apps.
///
/// SELF-CONTAINED ON PURPOSE.
/// This does not use COC3WinTheme or COC3WinWidgets. Those are COC 3's, one of them is an
/// Editor script you were told you could delete once COC 3 was built, and a shared dependency
/// between two modules is exactly the kind of thing that breaks six months later. The palette
/// is baked in below instead - Windows 10, same as COC 3, just its own copy.
///
/// LAYOUT MODEL
/// Everything anchors TOP-LEFT with a top-left pivot and is placed by explicit (x, y) in
/// window-space pixels measured DOWNWARD from the top-left corner. No layout groups: real
/// Windows dialogs are pixel-positioned, and auto-layout fights that at every turn. The
/// finished window is scaled once to fit whatever panel it lands in.
/// </summary>
public static class COC1WinWidgets
{
    // ==================================================================
    // Palette - Windows 10 / Rufus 4.x
    // ==================================================================

    public static Color WindowBg = Hex(0xF0F0F0);
    public static Color TitleBar = Hex(0xFFFFFF);
    public static Color WindowBorder = Hex(0xB4B4B4);
    public static Color Separator = Hex(0xDFDFDF);

    public static Color TextColor = Hex(0x000000);
    public static Color DimText = Hex(0x6D6D6D);
    public static Color LinkText = Hex(0x0066CC);
    public static Color HeaderText = Hex(0x1F1F1F);

    public static Color FieldBg = Hex(0xFFFFFF);
    public static Color FieldBorder = Hex(0x7A7A7A);
    public static Color FieldDisabled = Hex(0xF0F0F0);
    public static Color Placeholder = Hex(0x9A9A9A);

    public static Color ButtonFace = Hex(0xE1E1E1);
    public static Color ButtonBorder = Hex(0xADADAD);
    public static Color Accent = Hex(0x0078D4);
    public static Color Selection = Hex(0xCCE8FF);

    public static Color ProgressTrack = Hex(0xE6E6E6);
    public static Color ProgressFill = Hex(0x3FA34D);

    public static Color ConsoleBg = Hex(0x0C0C0C);
    public static Color ConsoleText = Hex(0xCCCCCC);

    // ==================================================================
    // Metrics
    // ==================================================================

    public const float FontSize = 14f;
    public const float SmallFontSize = 12f;
    public const float TitleFontSize = 14f;
    public const float TitleBarH = 30f;
    public const float RowH = 26f;
    public const float RowGap = 8f;
    public const float ItemH = 22f;

    /// <summary>Set once by the builder before it starts. A static rather than a parameter on
    /// every call: this is an Editor tool building one window at a time, and threading a font
    /// through forty call sites buys nothing.</summary>
    public static TMP_FontAsset Font;

    private const string UndoLabel = "COC1 Build UI";

    // ==================================================================
    // Core primitives
    // ==================================================================

    /// <summary>A rect anchored to its parent's top-left, positioned in downward pixels.</summary>
    public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        return rt;
    }

    /// <summary>A rect that stretches to fill its parent, with optional per-side insets.</summary>
    public static RectTransform Stretch(Transform parent, string name,
                                        float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        rt.localScale = Vector3.one;
        return rt;
    }

    public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    /// <summary>A filled rect with a 1px border, drawn as a border box with an inset fill.
    /// Sharper than a 9-sliced sprite and needs no art. Returns the FILL - callers put their
    /// content inside the fill, not the border.</summary>
    public static RectTransform BorderedBox(Transform parent, string name, float x, float y, float w, float h,
                                            Color fill, Color border, float thickness = 1f)
    {
        var outer = Box(parent, name, x, y, w, h, border).rectTransform;

        var inner = Rect(outer, "Fill", thickness, thickness, w - thickness * 2f, h - thickness * 2f);
        var img = inner.gameObject.AddComponent<Image>();
        img.color = fill;

        return inner;
    }

    public static TextMeshProUGUI Label(Transform parent, string name,
                                        float x, float y, float w, float h, string text,
                                        TextAlignmentOptions align = TextAlignmentOptions.Left,
                                        bool bold = false, Color? colorOverride = null,
                                        float? sizeOverride = null)
    {
        var rt = Rect(parent, name, x, y, w, h);
        return Dress(rt, text, align, bold, colorOverride, sizeOverride);
    }

    /// <summary>Same label, but stretched to its parent instead of pixel-placed - used for the
    /// bits of Unity's own control anatomy (dropdown captions, item labels) that must stretch.</summary>
    public static TextMeshProUGUI StretchLabel(Transform parent, string name, string text,
                                              float left, float top, float right, float bottom,
                                              TextAlignmentOptions align = TextAlignmentOptions.Left,
                                              Color? colorOverride = null, float? sizeOverride = null)
    {
        var rt = Stretch(parent, name, left, top, right, bottom);
        return Dress(rt, text, align, false, colorOverride, sizeOverride);
    }

    private static TextMeshProUGUI Dress(RectTransform rt, string text, TextAlignmentOptions align,
                                         bool bold, Color? colorOverride, float? sizeOverride)
    {
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();

        // Always resolve a font asset explicitly. A TMP text left with a null font relies on
        // its own Awake to pick up the default, which has not run yet on an object built from
        // an Editor script - and anything that parses the text before then throws.
        if (Font != null) label.font = Font;
        else if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        label.fontSize = sizeOverride ?? FontSize;
        label.color = colorOverride ?? TextColor;
        label.alignment = align;
        label.text = text;
        label.enableAutoSizing = false;
        label.raycastTarget = false;              // decoration must never eat clicks
        if (bold) label.fontStyle = FontStyles.Bold;

        return label;
    }

    // ==================================================================
    // Window frame
    // ==================================================================

    /// <summary>
    /// Builds a window with a title bar and returns its BODY rect - the client area content
    /// goes into. Body coordinates start at (0,0) top-left of the client area, so a layout
    /// never has to know the title bar height.
    /// </summary>
    public static RectTransform Window(Transform parent, string name, string title,
                                       float w, float h, out RectTransform windowRoot,
                                       float padding = 14f)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(holder, UndoLabel);

        var hrt = (RectTransform)holder.transform;
        hrt.SetParent(parent, false);
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = Vector2.zero;
        hrt.sizeDelta = new Vector2(w, h);
        hrt.localScale = Vector3.one;

        var frameImg = holder.AddComponent<Image>();
        frameImg.color = WindowBorder;

        var inner = Rect(hrt, "Client", 1f, 1f, w - 2f, h - 2f);
        var innerImg = inner.gameObject.AddComponent<Image>();
        innerImg.color = WindowBg;

        var bar = Box(inner, "TitleBar", 0f, 0f, w - 2f, TitleBarH, TitleBar);
        Label(bar.transform, "Title", 10f, 0f, w - 120f, TitleBarH, title,
              TextAlignmentOptions.Left, false, TextColor, TitleFontSize);

        // Decoration, not a Button. A live close glyph on a station panel would only confuse
        // the learner - the real CLOSE button is built separately and wired to ClosePanelButton.
        Label(bar.transform, "CloseGlyph", w - 40f, 0f, 30f, TitleBarH, "✕",
              TextAlignmentOptions.Center, false, DimText);

        Box(inner, "TitleSeparator", 0f, TitleBarH, w - 2f, 1f, Separator);

        var body = Rect(inner, "Body", padding, TitleBarH + padding,
                        w - 2f - padding * 2f, h - 2f - TitleBarH - padding * 2f);

        windowRoot = hrt;
        return body;
    }

    /// <summary>Scales a built window so it fits inside the panel it was dropped into, without
    /// ever scaling it UP past 1:1 - a Windows dialog blown up past its native size looks
    /// wrong immediately.</summary>
    public static void FitInside(RectTransform windowRoot, RectTransform panel, float margin = 20f)
    {
        if (windowRoot == null || panel == null) return;

        float pw = panel.rect.width - margin * 2f;
        float ph = panel.rect.height - margin * 2f;
        if (pw <= 0f || ph <= 0f) return;

        float sx = pw / windowRoot.sizeDelta.x;
        float sy = ph / windowRoot.sizeDelta.y;
        float s = Mathf.Min(1f, Mathf.Min(sx, sy));

        windowRoot.localScale = new Vector3(s, s, 1f);
    }

    // ==================================================================
    // Controls
    // ==================================================================

    public static Button Button(Transform parent, string name,
                                float x, float y, float w, float h, string text,
                                bool isDefault = false, bool enabled = true)
    {
        var fill = BorderedBox(parent, name, x, y, w, h,
                               enabled ? ButtonFace : FieldDisabled,
                               isDefault ? Accent : ButtonBorder);

        var btn = fill.gameObject.AddComponent<Button>();
        btn.targetGraphic = fill.GetComponent<Image>();
        btn.interactable = enabled;

        Label(fill, "Label", 0f, 0f, w - 2f, h - 2f, text, TextAlignmentOptions.Center,
              false, enabled ? TextColor : DimText);
        return btn;
    }

    /// <summary>A section header with the thin rule beside it that Rufus and most Win32 apps
    /// use - "Drive Properties", "Format Options", "Status".</summary>
    public static void SectionHeader(Transform parent, string name, float x, float y, float w, string text)
    {
        Label(parent, name, x, y, 200f, 18f, text, TextAlignmentOptions.Left, true,
              HeaderText, SmallFontSize);

        // DO NOT ask TMP how wide the text is here.
        // preferredWidth forces a text parse, and a TextMeshProUGUI added microseconds ago has
        // not had its material reference built yet - the getter itself throws a
        // NullReferenceException deep inside MaterialReferenceManager. Guarding the returned
        // value does not help; the call never returns.
        //
        // The rule beside a header does not need to be pixel-exact, so estimate from the
        // character count. Deterministic, instant, and it cannot throw.
        float textW = Mathf.Min(200f, text.Length * SmallFontSize * 0.62f + 10f);
        Box(parent, name + "_Rule", x + textW, y + 9f, Mathf.Max(0f, w - textW), 1f, Separator);
    }

    /// <summary>A combo box that only LOOKS like one: field chrome, a caption and an arrow, and
    /// no TMP_Dropdown behind it. Used for every Rufus control the simulation does not model
    /// (partition scheme, file system, cluster size...). Drawing them as real dropdowns would
    /// invite the learner to change settings that do nothing.</summary>
    public static void FakeCombo(Transform parent, string name, float x, float y, float w, string value,
                                 bool dimmed = false)
    {
        var fill = BorderedBox(parent, name, x, y, w, RowH,
                               dimmed ? FieldDisabled : FieldBg, FieldBorder);

        Label(fill, "Caption", 7f, 0f, w - 34f, RowH - 2f, value, TextAlignmentOptions.Left,
              false, dimmed ? DimText : TextColor);
        Label(fill, "Arrow", w - 26f, 0f, 16f, RowH - 2f, "▼", TextAlignmentOptions.Center,
              false, DimText, SmallFontSize);
    }

    /// <summary>A read-only text field - Rufus's "Volume label" and friends.</summary>
    public static void FakeField(Transform parent, string name, float x, float y, float w, string value)
    {
        var fill = BorderedBox(parent, name, x, y, w, RowH, FieldBg, FieldBorder);
        Label(fill, "Text", 7f, 0f, w - 14f, RowH - 2f, value, TextAlignmentOptions.Left);
    }

    // ==================================================================
    // A REAL TMP_Dropdown
    // ==================================================================

    /// <summary>
    /// Builds a working TMP_Dropdown with the full anatomy Unity expects: a caption, and a
    /// Template holding Viewport > Content > Item, driven by a ScrollRect.
    ///
    /// Every piece below is required. Miss one and the dropdown looks fine in the Scene view
    /// and then does nothing at all when clicked at runtime, which is a miserable thing to
    /// debug - so this is built once, here, and reused.
    /// </summary>
    public static TMP_Dropdown Dropdown(Transform parent, string name, float x, float y, float w,
                                        string caption, float templateHeight = 140f)
    {
        var fill = BorderedBox(parent, name, x, y, w, RowH, FieldBg, FieldBorder);

        var dd = fill.gameObject.AddComponent<TMP_Dropdown>();
        dd.targetGraphic = fill.GetComponent<Image>();

        var captionText = Label(fill, "Label", 7f, 0f, w - 34f, RowH - 2f, caption,
                                TextAlignmentOptions.Left);
        Label(fill, "Arrow", w - 26f, 0f, 16f, RowH - 2f, "▼", TextAlignmentOptions.Center,
              false, DimText, SmallFontSize);

        // ---- Template ----------------------------------------------------
        var template = new GameObject("Template", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(template, UndoLabel);

        var trt = (RectTransform)template.transform;
        trt.SetParent(fill, false);
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(1f, 0f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, 2f);
        trt.sizeDelta = new Vector2(0f, templateHeight);
        trt.localScale = Vector3.one;

        var tImg = template.AddComponent<Image>();
        tImg.color = FieldBg;

        var outline = template.AddComponent<Outline>();
        outline.effectColor = FieldBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var scroll = template.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;

        // ---- Viewport ----------------------------------------------------
        var viewport = Stretch(trt, "Viewport");
        viewport.pivot = new Vector2(0f, 1f);

        var vImg = viewport.gameObject.AddComponent<Image>();
        vImg.color = Color.white;

        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // ---- Content -----------------------------------------------------
        var content = new GameObject("Content", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(content, UndoLabel);

        var crt = (RectTransform)content.transform;
        crt.SetParent(viewport, false);
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, ItemH);
        crt.localScale = Vector3.one;

        // ---- Item --------------------------------------------------------
        var item = new GameObject("Item", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(item, UndoLabel);

        var irt = (RectTransform)item.transform;
        irt.SetParent(crt, false);
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(1f, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = Vector2.zero;
        irt.sizeDelta = new Vector2(0f, ItemH);
        irt.localScale = Vector3.one;

        var toggle = item.AddComponent<Toggle>();

        // White, not the selection blue: the Toggle's own ColorTint transition handles hover,
        // and a permanently-blue background would make every row look selected.
        var itemBg = Stretch(irt, "Item Background");
        var itemBgImg = itemBg.gameObject.AddComponent<Image>();
        itemBgImg.color = Color.white;

        var itemCheck = Stretch(irt, "Item Checkmark");
        var checkImg = itemCheck.gameObject.AddComponent<Image>();
        checkImg.color = Accent;
        itemCheck.anchorMin = new Vector2(0f, 0.5f);
        itemCheck.anchorMax = new Vector2(0f, 0.5f);
        itemCheck.pivot = new Vector2(0.5f, 0.5f);
        itemCheck.sizeDelta = new Vector2(3f, ItemH);
        itemCheck.anchoredPosition = new Vector2(2f, 0f);

        var itemLabel = StretchLabel(irt, "Item Label", "Option", 10f, 1f, 10f, 1f);

        toggle.targetGraphic = itemBgImg;
        toggle.graphic = checkImg;
        toggle.isOn = true;

        scroll.viewport = viewport;
        scroll.content = crt;

        dd.template = trt;
        dd.captionText = captionText;
        dd.itemText = itemLabel;

        template.SetActive(false);

        return dd;
    }

    // ==================================================================
    // Controls that get REPOSITIONED at runtime
    // ==================================================================
    //
    // Everything above builds a border box with the live control on the inner Fill. That is
    // right for a static window, and wrong for anything the view moves at runtime: moving the
    // Fill leaves its border behind. These variants put the control on the ROOT and draw the
    // border with an Outline component instead, so the whole thing travels together.

    /// <summary>A button whose own RectTransform is the thing that moves.</summary>
    public static Button FlatButton(Transform parent, string name, float x, float y, float w, float h,
                                    string text, out TextMeshProUGUI label,
                                    Color? face = null, Color? border = null)
    {
        var img = Box(parent, name, x, y, w, h, face ?? ButtonFace);

        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = border ?? ButtonBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        label = StretchLabel(img.rectTransform, "Label", text, 2f, 2f, 2f, 2f,
                             TextAlignmentOptions.Center);
        return btn;
    }

    /// <summary>An invisible click region for sitting on top of a screenshot. The Image is fully
    /// transparent but still receives clicks - uGUI hit-tests the rect, not the pixels, unless
    /// you raise alphaHitTestMinimumThreshold. The outline is a separate Image so it can be
    /// switched on and off per step without disturbing the button itself.</summary>
    public static Button HotspotButton(Transform parent, string name, float x, float y, float w, float h,
                                       out TextMeshProUGUI label, out Image outlineImage)
    {
        // ALPHA 1/255, NOT 0. AND cullTransparentMesh OFF.
        //
        // A Graphic whose mesh is fully transparent is CULLED by the CanvasRenderer, and
        // GraphicRaycaster skips culled graphics outright:
        //     if (graphic.depth == -1 || !graphic.raycastTarget || graphic.canvasRenderer.cull)
        //         continue;
        // So a hotspot at alpha 0 has raycastTarget ticked, sits in the right place, is active,
        // and is still completely unclickable - with nothing on screen to hint at why.
        //
        // 1/255 is the smallest non-zero step an 8-bit channel can hold. It is invisible to
        // the eye and enough to keep the mesh alive. Switching the cull off as well means it
        // survives someone later setting the colour back to a clean zero.
        var img = Box(parent, name, x, y, w, h, new Color(1f, 1f, 1f, 1f / 255f));
        img.raycastTarget = true;

        var cr = img.GetComponent<CanvasRenderer>();
        if (cr != null) cr.cullTransparentMesh = false;

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        // The ring lives on its OWN child, not on the button.
        // Switching the outline off per step has to not touch the button's own Image: that
        // Image is the raycast target, and disabling it would make the hotspot unclickable
        // while still looking present. A separate child can be switched off freely.
        var ringRt = Stretch(img.rectTransform, "Outline");
        outlineImage = ringRt.gameObject.AddComponent<Image>();
        outlineImage.color = new Color(1f, 1f, 1f, 0f);
        outlineImage.raycastTarget = false;

        var outline = ringRt.gameObject.AddComponent<Outline>();
        outline.effectColor = Accent;
        outline.effectDistance = new Vector2(2f, -2f);

        label = StretchLabel(img.rectTransform, "Label", "", 2f, 2f, 2f, 2f,
                             TextAlignmentOptions.Center, Accent);
        return btn;
    }

    /// <summary>A working TMP_InputField whose root is the thing that moves.</summary>
    public static TMP_InputField InputField(Transform parent, string name,
                                            float x, float y, float w, float h, string placeholderText)
    {
        var img = Box(parent, name, x, y, w, h, FieldBg);

        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = FieldBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var field = img.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = img;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 0;                 // a limit silently truncates a long URL
        field.contentType = TMP_InputField.ContentType.Standard;   // never Alphanumeric - it eats / and :

        var area = Stretch(img.rectTransform, "Text Area", 8f, 3f, 8f, 3f);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = StretchLabel(area, "Placeholder", placeholderText ?? "", 0f, 0f, 0f, 0f,
                              TextAlignmentOptions.Left, Placeholder);
        ph.fontStyle = FontStyles.Italic;

        var txt = StretchLabel(area, "Text", "", 0f, 0f, 0f, 0f, TextAlignmentOptions.Left);

        field.textViewport = area;
        field.textComponent = txt;
        field.placeholder = ph;
        if (Font != null) field.fontAsset = Font;
        else if (TMP_Settings.defaultFontAsset != null) field.fontAsset = TMP_Settings.defaultFontAsset;
        field.pointSize = FontSize;

        return field;
    }

    /// <summary>A progress bar whose root is the thing that moves.</summary>
    public static Slider FlatProgressBar(Transform parent, string name,
                                         float x, float y, float w, float h, Color? fillColor = null)
    {
        var img = Box(parent, name, x, y, w, h, ProgressTrack);

        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = FieldBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var slider = img.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.interactable = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;

        var fillArea = Stretch(img.rectTransform, "Fill Area", 1f, 1f, 1f, 1f);
        var fill = Stretch(fillArea, "Fill");
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = fillColor ?? ProgressFill;

        slider.fillRect = fill;
        slider.value = 0f;

        return slider;
    }

    /// <summary>
    /// The same working TMP_Dropdown as above, but with its root as the thing that moves - for
    /// the install screen, where OSInstallSkinner repositions one shared dropdown across six
    /// different looks. The bordered variant would leave its border behind.
    /// </summary>
    public static TMP_Dropdown FlatDropdown(Transform parent, string name, float x, float y,
                                            float w, float h, string caption,
                                            float templateHeight = 160f,
                                            float? textSize = null,
                                            float? itemHeight = null)
    {
        // THE SIZE AND THE ROW HEIGHT TRAVEL TOGETHER, BECAUSE ONE WITHOUT THE OTHER DOES NOTHING.
        // The list rows were a fixed 22 high while the text on the screen around them is 17pt.
        // 17pt glyphs do not fit a 22px row, so the items were the one place on the screen where
        // the text had to stay small - which is exactly what "the fonts are not equal" looks like.
        // Passing a size without making room for it would leave them clipped instead of small,
        // which is no better.
        float size = textSize ?? FontSize;
        float rowH = itemHeight ?? Mathf.Max(ItemH, size * 1.8f);
        var img = Box(parent, name, x, y, w, h, FieldBg);

        var border = img.gameObject.AddComponent<Outline>();
        border.effectColor = FieldBorder;
        border.effectDistance = new Vector2(1f, -1f);

        var dd = img.gameObject.AddComponent<TMP_Dropdown>();
        dd.targetGraphic = img;

        // Right inset 34, not 30: the arrow cell grew by 4 and the caption has to stop clear of it.
        var captionText = StretchLabel(img.rectTransform, "Label", caption, 8f, 1f, 34f, 1f,
                                       TextAlignmentOptions.Left, null, size);
        // THE ARROW IS AN ICON, NOT BODY TEXT, AND MUST NOT SCALE WITH IT.
        // Raising it to the caption's size was a mistake: a 20pt glyph does not fit the 16px-wide
        // cell it sits in, and with Ellipsis overflow a glyph that does not fit is not drawn at
        // all - so the arrow vanished from every dropdown rather than merely looking cramped.
        // It stays at SmallFontSize, and the cell is widened a little so it still has room on a
        // dropdown whose text is now larger.
        Label(img.rectTransform, "Arrow", w - 30f, 0f, 24f, h, "▼", TextAlignmentOptions.Center,
              false, DimText, SmallFontSize);

        var template = new GameObject("Template", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(template, UndoLabel);

        var trt = (RectTransform)template.transform;
        trt.SetParent(img.rectTransform, false);
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(1f, 0f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, 2f);
        trt.sizeDelta = new Vector2(0f, templateHeight);
        trt.localScale = Vector3.one;

        var tImg = template.AddComponent<Image>();
        tImg.color = FieldBg;

        var tOutline = template.AddComponent<Outline>();
        tOutline.effectColor = FieldBorder;
        tOutline.effectDistance = new Vector2(1f, -1f);

        var scroll = template.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;

        var viewport = Stretch(trt, "Viewport");
        viewport.pivot = new Vector2(0f, 1f);
        var vImg = viewport.gameObject.AddComponent<Image>();
        vImg.color = Color.white;
        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(content, UndoLabel);
        var crt = (RectTransform)content.transform;
        crt.SetParent(viewport, false);
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, rowH);
        crt.localScale = Vector3.one;

        var item = new GameObject("Item", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(item, UndoLabel);
        var irt = (RectTransform)item.transform;
        irt.SetParent(crt, false);
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(1f, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = Vector2.zero;
        irt.sizeDelta = new Vector2(0f, rowH);
        irt.localScale = Vector3.one;

        var toggle = item.AddComponent<Toggle>();

        var itemBg = Stretch(irt, "Item Background");
        var itemBgImg = itemBg.gameObject.AddComponent<Image>();
        itemBgImg.color = Color.white;

        var itemCheck = Stretch(irt, "Item Checkmark");
        var checkImg = itemCheck.gameObject.AddComponent<Image>();
        checkImg.color = Accent;
        itemCheck.anchorMin = new Vector2(0f, 0.5f);
        itemCheck.anchorMax = new Vector2(0f, 0.5f);
        itemCheck.pivot = new Vector2(0.5f, 0.5f);
        itemCheck.sizeDelta = new Vector2(3f, rowH);
        itemCheck.anchoredPosition = new Vector2(2f, 0f);

        var itemLabel = StretchLabel(irt, "Item Label", "Option", 10f, 1f, 10f, 1f,
                                     TextAlignmentOptions.Left, null, size);

        toggle.targetGraphic = itemBgImg;
        toggle.graphic = checkImg;
        toggle.isOn = true;

        scroll.viewport = viewport;
        scroll.content = crt;

        dd.template = trt;
        dd.captionText = captionText;
        dd.itemText = itemLabel;

        template.SetActive(false);
        return dd;
    }

    // ==================================================================
    // Progress bar
    // ==================================================================

    /// <summary>A Slider with no handle, styled as a Windows progress bar. Returned as a Slider
    /// because that is what the existing views drive (value 0-1).</summary>
    public static Slider ProgressBar(Transform parent, string name, float x, float y, float w, float h,
                                     Color? fillColor = null)
    {
        var track = BorderedBox(parent, name, x, y, w, h, ProgressTrack, FieldBorder);

        var slider = track.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.interactable = false;               // display only - never draggable
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;

        var fillArea = Stretch(track, "Fill Area", 1f, 1f, 1f, 1f);

        var fill = Stretch(fillArea, "Fill");
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = fillColor ?? ProgressFill;

        slider.fillRect = fill;
        slider.value = 0f;

        return slider;
    }

    // ==================================================================
    // Misc
    // ==================================================================

    public static void HorizontalRule(Transform parent, string name, float x, float y, float w)
    {
        Box(parent, name, x, y, w, 1f, Separator);
    }

    /// <summary>Full-bleed background for a screen that is not a window - the BIOS setup screen,
    /// the POST screen, the OOBE. Stretches to the whole panel.</summary>
    public static RectTransform FullScreen(Transform parent, string name, Color color)
    {
        var rt = Stretch(parent, name);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return rt;
    }

    private static Color Hex(int rgb)
    {
        return new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
    }
}
#endif