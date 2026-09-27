using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Makes the text on the OS install screens as large as each box can actually hold, and guarantees
/// none of it draws outside that box.
///
/// ATTACH THIS TO: the install panel root - the object with OS Install View on it. It works on
/// every TMP text underneath, whatever built them, so no rebuild is needed and nothing in the
/// builder scripts has to change.
///
/// ---------------------------------------------------------------------------------------------
/// WHY THE FONT SCALE IN THE BUILDER GAVE BOTH SYMPTOMS AT ONCE
///
/// Every element on these screens is positioned by NORMALISED ANCHORS - COC1SetupScreens.Place
/// divides x and w by a 1920 x 1080 design space - so the boxes resize with the panel. The font
/// sizes are absolute numbers. Boxes stretch, text does not, and the two drift apart.
///
/// Multiplying the font sizes then produced exactly what you saw:
///   - where the box was tight, TMP auto-sizing shrank the text back to its original size, so it
///     looked unchanged - "still small";
///   - where the text was too big for its box, it drew straight through the window, because a TMP
///     text with the default Overflow mode simply keeps drawing past its own rectangle.
///
/// The missing line was the overflow mode. That is what this fixes.
/// ---------------------------------------------------------------------------------------------
///
/// WHAT IT DOES TO EACH LABEL
///   - remembers the size the builder gave it, once, and never compounds on top of itself
///   - turns wrapping ON and overflow to Ellipsis, so text physically cannot leave its own box -
///     the worst case is a clipped word, never a line across the window
///   - turns auto-sizing ON with that remembered size as the FLOOR and boost x it as the CEILING
///
/// So every label draws at the largest size its own box can hold, up to the boost, and no smaller
/// than it was before. Nothing can overflow, because the box is the limit rather than a suggestion.
///
/// SET THE BUILDER BACK TO FontScale = 1 AND REBUILD before using this. Otherwise the size this
/// remembers is already 1.35x, and boosting again multiplies twice.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class OSInstallTextFit : MonoBehaviour
{
    [Header("Size")]
    [Tooltip("How much larger than its authored size a label may become, if its box can hold it. " +
             "1 means \"never larger\", which still fixes overflow. 1.6 is a good start for a phone.")]
    [Range(1f, 3f)]
    [SerializeField] private float boost = 1.6f;

    [Tooltip("A label is never drawn below this, whatever the boost. Only raise it if some text is " +
             "still too small to read - and expect more of it to be clipped when you do.")]
    [SerializeField] private float neverBelow = 12f;

    [Header("When to apply")]
    [Tooltip("Reapply every frame. Needed while the install runs, because stages are shown and " +
             "hidden and their text is rewritten as it goes.")]
    [SerializeField] private bool keepApplying = true;

    [Tooltip("Include labels on objects that are switched off. On by default: nearly every stage " +
             "screen here starts inactive, and a label missed now is one that is wrong when its " +
             "stage arrives.")]
    [SerializeField] private bool includeInactive = true;

    [Tooltip("ON (correct): leave the stage label alone - the big line that says \"Installing " +
             "Windows\", \"Copying files\" and so on.\n\nThat one label is shared by every stage " +
             "and OSInstallSkinner sets its size per stage: 20 on the wizard, 19 on the partition " +
             "table, 30 on the progress screens, 26 on the out-of-box ones. Auto-sizing makes TMP " +
             "ignore fontSize completely, so touching it here freezes the label at whichever " +
             "stage was seen first - which is why the installing text went small after the " +
             "partition step. To resize that line, change labelSize on the skins in " +
             "COC1SetupScreens, or Label Font Size on OSInstallSkinner.")]
    [SerializeField] private bool leaveStageLabelAlone = true;

    private TMP_Text stageLabel;
    private bool lookedForStageLabel;

    // The size each label was built with. Keyed by the component, so a label whose text changes
    // keeps its own base and the boost never compounds.
    private readonly Dictionary<TMP_Text, float> baseSizes = new Dictionary<TMP_Text, float>();

    // ==================================================================

    private void OnEnable() => Apply();

    private void Start() => Apply();

    private void LateUpdate()
    {
        // LateUpdate, so layout has settled for this frame before anything is measured.
        if (keepApplying) Apply();
    }

    // ==================================================================

    [ContextMenu("Apply now")]
    public void Apply()
    {
        TMP_Text skip = leaveStageLabelAlone ? StageLabel() : null;

        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(includeInactive))
        {
            if (text == null) continue;

            // THE ONE LABEL THIS MUST NOT TOUCH. See leaveStageLabelAlone above: it is re-sized
            // every stage by OSInstallSkinner, and auto-sizing would override every one of those.
            if (text == skip) continue;

            // ICONS ARE NOT TEXT. A dropdown arrow, a checkmark, a close cross - one glyph in a
            // cell barely wider than itself. Growing those is pointless, and Ellipsis overflow
            // does not shrink a glyph that will not fit, it simply does not draw it: the arrow
            // disappears from every dropdown instead of looking slightly cramped.
            if (IsIcon(text)) continue;

            float authored = BaseSizeOf(text);
            float ceiling = Mathf.Max(authored, authored * boost);

            // Wrapping on and Ellipsis overflow are the two settings that make the box the limit.
            // Without them TMP draws past its own rectangle and no amount of size juggling helps.
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;

            text.fontSizeMin = Mathf.Max(neverBelow, Mathf.Min(authored, ceiling));
            text.fontSizeMax = ceiling;
            text.enableAutoSizing = true;
        }
    }

    /// <summary>
    /// Puts every label back to the size it was built with, wrapping and overflow included.
    /// </summary>
    [ContextMenu("Revert to authored sizes")]
    public void Revert()
    {
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(includeInactive))
        {
            if (text == null) continue;
            if (!baseSizes.TryGetValue(text, out float authored)) continue;

            // Auto-sizing off FIRST: while it is on, TMP clamps fontSize against min and max, so
            // writing the authored size before switching it off can land on a different number.
            text.enableAutoSizing = false;
            text.fontSize = authored;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        Debug.Log($"[OSInstallTextFit] Reverted {baseSizes.Count} label(s) to their authored sizes.", this);
    }

    /// <summary>
    /// Lists what each label actually ended up drawing at, and flags the ones that could not grow.
    ///
    /// This is the one to read when the answer is "still too small": a label pinned at its floor
    /// has a box too tight for anything bigger, and no boost will move it. That is a layout to
    /// widen in the builder, not a font size to raise.
    /// </summary>
    [ContextMenu("Report label sizes")]
    public void Report()
    {
        var sb = new StringBuilder($"[OSInstallTextFit] Labels under '{name}' (boost x{boost:0.00})\n");

        int pinned = 0;
        int counted = 0;

        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(includeInactive))
        {
            if (text == null) continue;

            counted++;

            float authored = BaseSizeOf(text);
            float drawn = text.fontSize;
            bool atFloor = drawn <= text.fontSizeMin + 0.01f;

            if (atFloor) pinned++;

            sb.AppendLine($"  {(atFloor ? "TIGHT" : "     ")}  {drawn,5:0.0} / {authored,5:0.0} authored" +
                          $"   {Path(text.transform)}");
        }

        sb.AppendLine();
        sb.AppendLine($"  {counted} label(s), {pinned} of them boxed too tightly to grow.");

        Debug.Log(sb.ToString(), this);
    }

    // ==================================================================

    /// <summary>
    /// The stage label OSInstallView writes into, found once and remembered - including when it is
    /// null, so a scene without one is not re-searched every frame.
    /// </summary>
    /// <summary>
    /// True for a one-glyph label sitting in a narrow cell - an arrow, a tick, a cross.
    ///
    /// Both tests matter. Length alone would catch a legitimate "1" in a table cell; width alone
    /// would catch a short word in a narrow column. Together they describe an icon and very little
    /// else.
    /// </summary>
    private static bool IsIcon(TMP_Text text)
    {
        string content = text.text;
        if (string.IsNullOrEmpty(content) || content.Trim().Length > 1) return false;

        var rect = text.transform as RectTransform;
        return rect == null || rect.rect.width <= 40f;
    }

    private TMP_Text StageLabel()
    {
        if (lookedForStageLabel) return stageLabel;

        lookedForStageLabel = true;
        stageLabel = BorrowStageLabel();

        if (stageLabel != null)
        {
            Debug.Log($"[OSInstallTextFit] Leaving '{stageLabel.name}' alone - OSInstallSkinner " +
                      "sets its size for each stage.", this);
        }

        return stageLabel;
    }

    private TMP_Text BorrowStageLabel()
    {
        var view = GetComponent<OSInstallView>();
        if (view == null) view = FindAnyObjectByType<OSInstallView>(FindObjectsInactive.Include);
        if (view == null) return null;

        FieldInfo field = typeof(OSInstallView).GetField(
            "stageLabel", BindingFlags.Instance | BindingFlags.NonPublic);

        return field != null ? field.GetValue(view) as TMP_Text : null;
    }

    private float BaseSizeOf(TMP_Text text)
    {
        if (baseSizes.TryGetValue(text, out float known)) return known;

        // First sighting. fontSize is the builder's number, because auto-sizing has not been
        // switched on for this label yet - which is why this is read before anything is written.
        float authored = text.fontSize > 0f ? text.fontSize : 14f;
        baseSizes[text] = authored;
        return authored;
    }

    private static string Path(Transform target)
    {
        string path = target.name;
        Transform walk = target.parent;

        // Stops at this component's own object, so the line names the screen and the label rather
        // than repeating the whole canvas path on every row.
        while (walk != null && walk != target.root)
        {
            path = walk.name + "/" + path;
            walk = walk.parent;
        }

        return path;
    }
}
