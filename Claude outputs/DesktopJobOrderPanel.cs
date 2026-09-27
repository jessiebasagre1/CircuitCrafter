using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The job order, pinned to the side of the desktop: every task of the "Configure Computer Systems"
/// phase, each with its own count, ticked off as it is finished.
///
///     JOB ORDER                    2 / 5
///     [x] Update the drivers            4/4
///     [x] Install software              4/4
///     [ ] Update Windows                1/3   <-- highlighted: the one being worked on
///     [ ] Run diagnostics               0/4
///     [ ] Service report                0/1
///
/// ATTACH THIS TO: the desktop panel (the object with the Icons and Apps children). Nothing to
/// wire, nothing to rebuild - it builds its own panel at runtime and finds its own sources.
///
/// ---------------------------------------------------------------------------------------------
/// WHY EACH ROW COUNTS ITS OWN STEPS RATHER THAN ASKING TaskManager
///
/// TaskManager knows how far along every task is, but it only ANSWERS "is it finished"
/// (IsTaskComplete). Its current/required pair lives in a private struct, so "1/3" cannot be read
/// out of it without reaching into its internals.
///
/// It does not have to be. Every one of these tasks has an owner that already knows the answer in
/// public API:
///
///     drivers / software / updates  ->  ChecklistToolManager.Items + IsItemComplete(item)
///     diagnostics                   ->  DiagnosticsTestManager.Tests + GetLastResult(test)
///     service report                ->  TaskManager.IsTaskComplete(document_work)   (one step)
///
/// So the counts come from the systems that own them, which means a count can never disagree with
/// the app window it describes - "3/4" on this panel and three ticks in Device Manager are the same
/// question asked once.
/// ---------------------------------------------------------------------------------------------
///
/// IT READS STATE, IT DOES NOT REMEMBER IT. Every repaint re-asks the owners, so a resumed save is
/// correct with no persistence of its own, and closing and reopening the desktop cannot leave a
/// stale row behind.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class DesktopJobOrderPanel : MonoBehaviour
{
    // ==================================================================
    // Rows
    // ==================================================================

    public enum RowSource
    {
        /// <summary>Counts installed items in the ChecklistToolManager whose group id is Task Id.</summary>
        ChecklistTool = 0,

        /// <summary>Counts diagnostic tests that have produced a result.</summary>
        Diagnostics = 1,

        /// <summary>One step: complete when Task Id is complete in TaskManager.</summary>
        SingleTask = 2,
    }

    [Serializable]
    public class Row
    {
        [Tooltip("What the learner reads. Keep it short - the panel is deliberately narrow.")]
        public string label = "Task";

        public RowSource source = RowSource.ChecklistTool;

        [Tooltip("For Checklist Tool: the tool's GROUP task id, e.g. install_drivers.\n" +
                 "For Single Task: the TaskManager task id, e.g. document_work.\n" +
                 "Ignored for Diagnostics.")]
        public string taskId = "";
    }

    [Header("Tasks, in the order they must be done")]
    [Tooltip("Leave this EMPTY and the five standard tasks of the configuration phase are filled in " +
             "automatically. Add rows only to rename, reorder or extend them.")]
    [SerializeField] private List<Row> rows = new List<Row>();

    // ==================================================================
    // Placement and look
    // ==================================================================

    [Header("Placement (design pixels, from the desktop's TOP-LEFT)")]
    [Tooltip("The app windows occupy x 360 to 1560, so the default sits in the clear strip to their " +
             "right. The panel is drawn on top of everything, so it never ends up behind a window.")]
    [SerializeField] private Vector2 panelPosition = new Vector2(1570f, 60f);

    [SerializeField] private float panelWidth = 330f;
    [SerializeField] private float headerHeight = 52f;
    [SerializeField] private float rowHeight = 60f;

    [Header("Look")]
    [SerializeField] private Color cardColour = new Color32(0x12, 0x23, 0x3B, 0xE6);
    [SerializeField] private Color headerColour = new Color32(0x1B, 0x33, 0x53, 0xFF);
    [SerializeField] private Color titleColour = Color.white;

    [Tooltip("A row still to do.")]
    [SerializeField] private Color pendingColour = new Color32(0xC3, 0xD7, 0xF7, 0xFF);

    [Tooltip("A finished row - deliberately dimmer, so the eye lands on what is left.")]
    [SerializeField] private Color doneColour = new Color32(0x8F, 0xA6, 0xC4, 0xFF);

    [Tooltip("The tick, and the filled part of each row's bar.")]
    [SerializeField] private Color completeColour = new Color32(0x6E, 0xE7, 0xB7, 0xFF);

    [Tooltip("The task being worked on: its label and its accent stripe.")]
    [SerializeField] private Color activeColour = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

    [SerializeField] private float titleFontSize = 20f;
    [SerializeField] private float labelFontSize = 17f;
    [SerializeField] private float counterFontSize = 16f;

    [Header("Behaviour")]
    [Tooltip("Tap the header to collapse the list down to its summary bar. On a phone the expanded " +
             "panel is a real fraction of the screen, and a learner who cannot get it out of the way " +
             "will resent it.")]
    [SerializeField] private bool collapsible = true;

    [SerializeField] private bool startCollapsed;

    [Tooltip("Flash a row gold for a moment as it completes, so a tick that happens while the " +
             "learner is looking at a window is not missed entirely.")]
    [SerializeField] private bool flashOnComplete = true;

    [Tooltip("How often the panel re-reads the task state, in seconds. Five rows of simple counting, " +
             "so this is cheap - but it is not free, and nothing here changes between frames.")]
    [SerializeField] private float refreshSeconds = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logSources = true;

    // ==================================================================

    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;

    private const float Pad = 12f;

    /// <summary>One row's objects and its last known numbers, so nothing is rewritten needlessly.</summary>
    private class Live
    {
        public Row row;
        public RectTransform rect;
        public Image accent;
        public Image boxBorder;
        public Image boxFill;
        public TMP_Text label;
        public TMP_Text counter;
        public RectTransform barFill;
        public float barWidth;

        public ChecklistToolManager tool;

        public int done = -1;
        public int total = -1;
        public bool complete;
        public float flashUntil;
    }

    private readonly List<Live> live = new List<Live>();

    private RectTransform card;
    private RectTransform body;
    private TMP_Text titleText;
    private TMP_Text summaryText;
    private TMP_Text chevron;

    private bool built;
    private bool collapsed;
    private float nextRefresh;

    // ==================================================================

    private void OnEnable()
    {
        EnsureBuilt();
        Repaint(true);
    }

    private void LateUpdate()
    {
        // Built here as well as in OnEnable, because the desktop panel may be switched on before the
        // managers it reads exist. A panel that gave up after one attempt would simply show zeroes.
        if (!built) EnsureBuilt();
        if (!built) return;

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshSeconds);

        Repaint(false);
    }

    // ==================================================================
    // Building
    // ==================================================================

    private void EnsureBuilt()
    {
        if (built) return;

        var root = transform as RectTransform;
        if (root == null)
        {
            Debug.LogWarning("[DesktopJobOrderPanel] This object has no RectTransform. Put this " +
                             "component on the desktop panel - the object with the Icons and Apps " +
                             "children.", this);
            enabled = false;
            return;
        }

        if (rows.Count == 0) rows = DefaultRows();

        float height = headerHeight + Pad + rows.Count * rowHeight + Pad;

        card = NewCard(root, height);

        var cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = cardColour;
        cardImage.raycastTarget = true;   // the panel eats its own clicks rather than the desktop's

        // Drawn last, so it sits above the app windows. The windows are built into "Apps" after
        // "Icons", so anything that must stay visible has to come after both.
        card.SetAsLastSibling();

        BuildHeader();

        body = Cell(card, "Rows", 0f, headerHeight, panelWidth, height - headerHeight);

        for (int i = 0; i < rows.Count; i++)
            live.Add(BuildRow(rows[i], i));

        built = true;

        SetCollapsed(startCollapsed);
        ResolveSources();
    }

    /// <summary>
    /// The card: anchored to a point in the desktop so it keeps its place at any aspect ratio, and
    /// sized in pixels so its contents never squash. See the note on the layout helpers below.
    /// </summary>
    private RectTransform NewCard(RectTransform parent, float height)
    {
        var go = new GameObject("JobOrderPanel", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        rect.anchorMin = new Vector2(panelPosition.x / DesignWidth, 1f - panelPosition.y / DesignHeight);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(panelWidth, height);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;

        return rect;
    }

    private void BuildHeader()
    {
        var header = Cell(card, "Header", 0f, 0f, panelWidth, headerHeight);

        var bg = header.gameObject.AddComponent<Image>();
        bg.color = headerColour;
        bg.raycastTarget = true;

        titleText = Label(header, "Title", Pad, 0f, panelWidth - 130f, headerHeight,
                          "JOB ORDER", titleFontSize, titleColour, TextAlignmentOptions.Left);
        titleText.fontStyle = FontStyles.Bold;

        summaryText = Label(header, "Summary", panelWidth - 118f, 0f, 76f, headerHeight,
                            "0 / 0", titleFontSize, titleColour, TextAlignmentOptions.Right);

        chevron = Label(header, "Chevron", panelWidth - 34f, 0f, 24f, headerHeight,
                        "-", titleFontSize, titleColour, TextAlignmentOptions.Center);

        if (!collapsible)
        {
            chevron.gameObject.SetActive(false);
            return;
        }

        var button = header.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(Toggle);
    }

    private Live BuildRow(Row row, int index)
    {
        float y = Pad + index * rowHeight;

        var rect = Cell(body, "Row_" + index, 0f, y, panelWidth, rowHeight);

        var entry = new Live { row = row, rect = rect };

        // The accent stripe down the left edge. Only the active row shows one, which is what makes
        // "the one I am on" readable at a glance rather than something to work out.
        var accent = Cell(rect, "Accent", 0f, 6f, 4f, rowHeight - 12f);
        entry.accent = accent.gameObject.AddComponent<Image>();
        entry.accent.color = activeColour;
        entry.accent.raycastTarget = false;

        // THE CHECKBOX IS DRAWN, NOT TYPED. A tick character is not in every TMP font atlas, and a
        // glyph the font does not have renders as a hollow rectangle - which on a checkbox is
        // indistinguishable from "not done yet". Two squares cannot be got wrong by a font.
        var box = Cell(rect, "Box", 12f, 6f, 22f, 22f);
        entry.boxBorder = box.gameObject.AddComponent<Image>();
        entry.boxBorder.color = pendingColour;
        entry.boxBorder.raycastTarget = false;

        var boxInner = Cell(box, "BoxFill", 2f, 2f, 18f, 18f);
        entry.boxFill = boxInner.gameObject.AddComponent<Image>();
        entry.boxFill.color = cardColour;
        entry.boxFill.raycastTarget = false;

        entry.label = Label(rect, "Label", 44f, 2f, panelWidth - 44f - 74f, 30f,
                            row.label, labelFontSize, pendingColour, TextAlignmentOptions.Left);

        entry.counter = Label(rect, "Counter", panelWidth - 70f, 2f, 58f, 30f,
                              "0/0", counterFontSize, pendingColour, TextAlignmentOptions.Right);

        // The bar. Track first, then the fill on top of it, anchored left so its width IS the
        // progress - no Slider needed for something that is never dragged.
        float barW = panelWidth - 12f - 12f;
        var track = Cell(rect, "BarTrack", 12f, 38f, barW, 6f);
        var trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(1f, 1f, 1f, 0.12f);
        trackImage.raycastTarget = false;

        entry.barWidth = barW;
        entry.barFill = Cell(track, "BarFill", 0f, 0f, 0f, 6f);
        var fillImage = entry.barFill.gameObject.AddComponent<Image>();
        fillImage.color = completeColour;
        fillImage.raycastTarget = false;

        return entry;
    }

    /// <summary>
    /// The five tasks of the configuration phase, in the order TESDA's own sequence puts them:
    /// drivers before software (an application installed onto a machine whose chipset driver is
    /// missing is a service call waiting to happen), updates next, then testing, then the paperwork
    /// that closes the job.
    /// </summary>
    private static List<Row> DefaultRows()
    {
        return new List<Row>
        {
            new Row { label = "Update the drivers",  source = RowSource.ChecklistTool, taskId = SystemConfigTaskIds.InstallDrivers },
            new Row { label = "Install software",    source = RowSource.ChecklistTool, taskId = SystemConfigTaskIds.InstallAppSoftware },
            new Row { label = "Update Windows",      source = RowSource.ChecklistTool, taskId = SystemConfigTaskIds.InstallUpdates },
            new Row { label = "Run diagnostics",     source = RowSource.Diagnostics },
            new Row { label = "Service report",      source = RowSource.SingleTask,    taskId = SystemConfigTaskIds.DocumentWork },
        };
    }

    // ==================================================================
    // Collapsing
    // ==================================================================

    public void Toggle() => SetCollapsed(!collapsed);

    public void SetCollapsed(bool value)
    {
        collapsed = value;

        if (body != null) body.gameObject.SetActive(!collapsed);
        if (chevron != null) chevron.text = collapsed ? "+" : "-";

        // The card shrinks to the header, so a collapsed panel is a summary bar and not a pane of
        // empty colour. Only its height changes - the header and rows are pinned to the top in
        // pixels, so nothing inside is resized by this.
        if (card != null)
        {
            float height = collapsed ? headerHeight : headerHeight + Pad + rows.Count * rowHeight + Pad;
            card.sizeDelta = new Vector2(panelWidth, height);
        }
    }

    // ==================================================================
    // Reading the state
    // ==================================================================

    private void Repaint(bool force)
    {
        if (!built) return;

        int rowsDone = 0;

        for (int i = 0; i < live.Count; i++)
        {
            Live entry = live[i];

            int done, total;
            Measure(entry, out done, out total);

            bool complete = total > 0 && done >= total;
            if (complete) rowsDone++;

            bool changed = force || done != entry.done || total != entry.total;
            bool justCompleted = complete && !entry.complete && entry.total >= 0;

            entry.done = done;
            entry.total = total;
            entry.complete = complete;

            if (justCompleted && flashOnComplete) entry.flashUntil = Time.unscaledTime + 0.9f;

            if (changed) Paint(entry);

            // The flash is a colour that decays, so it has to be written while it is running even
            // when the numbers have not moved.
            if (entry.flashUntil > Time.unscaledTime) PaintFlash(entry);
            else if (entry.flashUntil != 0f) { entry.flashUntil = 0f; Paint(entry); }
        }

        // The active row: the FIRST unfinished one, which is the next thing to do rather than
        // whichever one happens to be half done.
        int active = -1;
        for (int i = 0; i < live.Count; i++)
        {
            if (!live[i].complete) { active = i; break; }
        }

        for (int i = 0; i < live.Count; i++)
        {
            bool isActive = i == active;
            live[i].accent.enabled = isActive;

            if (live[i].flashUntil > Time.unscaledTime) continue;

            live[i].label.color = live[i].complete ? doneColour
                                : isActive ? activeColour
                                : pendingColour;
        }

        string summary = rowsDone + " / " + live.Count;
        if (summaryText != null && summaryText.text != summary) summaryText.text = summary;

        if (titleText != null)
        {
            string title = rowsDone >= live.Count && live.Count > 0 ? "JOB ORDER DONE" : "JOB ORDER";
            if (titleText.text != title) titleText.text = title;
        }
    }

    private void Paint(Live entry)
    {
        // Empty box while pending, filled green once done - the same read as a tick, with nothing
        // for a font to fail at.
        entry.boxBorder.color = entry.complete ? completeColour : pendingColour;
        entry.boxFill.color = entry.complete ? completeColour : cardColour;

        entry.counter.text = entry.total > 0 ? entry.done + "/" + entry.total : "-";
        entry.counter.color = entry.complete ? completeColour : pendingColour;

        float fraction = entry.total > 0 ? Mathf.Clamp01((float)entry.done / entry.total) : 0f;
        PixelRect(entry.barFill, 0f, 0f, entry.barWidth * fraction, 6f);
    }

    private void PaintFlash(Live entry)
    {
        entry.label.color = activeColour;
        entry.boxBorder.color = activeColour;
        entry.boxFill.color = activeColour;
    }

    /// <summary>How many steps of this row are done, and how many there are.</summary>
    private void Measure(Live entry, out int done, out int total)
    {
        done = 0;
        total = 0;

        switch (entry.row.source)
        {
            case RowSource.ChecklistTool:
                if (entry.tool == null) entry.tool = FindTool(entry.row.taskId);
                if (entry.tool == null) return;

                IReadOnlyList<ChecklistItemData> items = entry.tool.Items;
                if (items == null) return;

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == null) continue;
                    total++;
                    if (entry.tool.IsItemComplete(items[i])) done++;
                }
                return;

            case RowSource.Diagnostics:
                DiagnosticsTestManager tests = DiagnosticsTestManager.Instance;
                if (tests == null) tests = FindAnyObjectByType<DiagnosticsTestManager>(FindObjectsInactive.Include);
                if (tests == null || tests.Tests == null) return;

                for (int i = 0; i < tests.Tests.Count; i++)
                {
                    DiagnosticTestData test = tests.Tests[i];
                    if (test == null) continue;

                    total++;

                    // A test that RAN counts as a step taken, pass or fail - the same rule
                    // DiagnosticsTestManager itself reports to TaskManager. Whether they all passed
                    // is the service report's business, not this panel's.
                    if (tests.GetLastResult(test).HasValue) done++;
                }
                return;

            default:
                total = 1;
                done = TaskManager.Instance != null
                       && !string.IsNullOrEmpty(entry.row.taskId)
                       && TaskManager.Instance.IsTaskComplete(entry.row.taskId) ? 1 : 0;
                return;
        }
    }

    /// <summary>
    /// The checklist tool with this group id. Matched on the id rather than "the first one found",
    /// because Device Manager, Windows Update and the Software Center are all the same component -
    /// picking the wrong one would show the driver count on the Windows Update row.
    /// </summary>
    private ChecklistToolManager FindTool(string groupTaskId)
    {
        if (string.IsNullOrEmpty(groupTaskId)) return null;

        FieldInfo field = typeof(ChecklistToolManager).GetField(
            "groupTaskId", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null) return null;

        ChecklistToolManager[] all = FindObjectsByType<ChecklistToolManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if ((field.GetValue(all[i]) as string) == groupTaskId) return all[i];
        }

        return null;
    }

    /// <summary>
    /// Says once, in the Console, which source each row found - and which it did not.
    ///
    /// A row with no source shows "-" and never ticks, and from the outside that looks exactly like
    /// a row the learner simply has not reached yet. This is the difference between the two.
    /// </summary>
    private void ResolveSources()
    {
        if (!logSources) return;

        var sb = new System.Text.StringBuilder("[DesktopJobOrderPanel] Sources\n");

        for (int i = 0; i < live.Count; i++)
        {
            Live entry = live[i];
            string found;

            switch (entry.row.source)
            {
                case RowSource.ChecklistTool:
                    entry.tool = FindTool(entry.row.taskId);
                    found = entry.tool != null
                        ? entry.tool.name + " (" + (entry.tool.Items != null ? entry.tool.Items.Count : 0) + " items)"
                        : "NOT FOUND - no ChecklistToolManager has group id '" + entry.row.taskId + "'";
                    break;

                case RowSource.Diagnostics:
                    DiagnosticsTestManager tests = DiagnosticsTestManager.Instance;
                    if (tests == null) tests = FindAnyObjectByType<DiagnosticsTestManager>(FindObjectsInactive.Include);
                    found = tests != null
                        ? tests.name + " (" + (tests.Tests != null ? tests.Tests.Count : 0) + " tests)"
                        : "NOT FOUND - no DiagnosticsTestManager in this scene";
                    break;

                default:
                    found = "TaskManager task '" + entry.row.taskId + "'";
                    break;
            }

            sb.AppendLine("  " + entry.row.label + "  ->  " + found);
        }

        Debug.Log(sb.ToString(), this);
    }

    [ContextMenu("Report")]
    public void Report()
    {
        if (!built) { Debug.Log("[DesktopJobOrderPanel] Not built yet.", this); return; }

        var sb = new System.Text.StringBuilder("[DesktopJobOrderPanel] REPORT\n");

        for (int i = 0; i < live.Count; i++)
            sb.AppendLine($"  {(live[i].complete ? "DONE" : "    ")}  {live[i].done}/{live[i].total}" +
                          $"   {live[i].row.label}");

        Debug.Log(sb.ToString(), this);
    }

    // ==================================================================
    // Layout helpers
    //
    // THE PANEL IS PLACED PROPORTIONALLY AND SIZED IN PIXELS, and the difference matters.
    //
    // COC1DesktopScreens anchors its boxes proportionally in both axes, which is right for scenery
    // that should stretch with the desktop. It is wrong here: this panel CHANGES HEIGHT when it
    // collapses, and proportional children would shrink along with it - a 52px header squeezed to
    // 8px, text and all. Every child is therefore pinned to its parent's TOP-LEFT CORNER with a
    // fixed pixel size, so a change of height moves nothing that was not meant to move.
    //
    // The card itself is anchored to a POINT in the desktop (proportional position, fixed size), so
    // it keeps its place on any aspect ratio while the rows stay the size they were designed at -
    // which on a phone is the difference between a readable panel and a squashed one.
    // ==================================================================

    /// <summary>Position and size relative to the parent's top-left corner, in pixels.</summary>
    private static void PixelRect(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(w, h);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.localScale = Vector3.one;
    }

    private static RectTransform Cell(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        PixelRect(rect, x, y, w, h);
        return rect;
    }

    private static TMP_Text Label(Transform parent, string name, float x, float y, float w, float h,
                                  string text, float size, Color colour, TextAlignmentOptions alignment)
    {
        var rect = Cell(parent, name, x, y, w, h);

        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();

        // Set explicitly: a TMP text created from script has no font until something gives it one.
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;

        // No wrap plus Ellipsis: a long task name is clipped with dots rather than pushing a second
        // line through the row below it. The panel is narrow on purpose, so this will happen.
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        return label;
    }
}
