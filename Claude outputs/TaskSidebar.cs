using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The job order for a whole module, in a sidebar that opens from an arrow on the edge of the
/// screen - one line per task, each with its own count, ticked off as it is finished.
///
///     TASKS                      3 / 11
///     ---- ASSEMBLY & DISASSEMBLY ----
///     [x] PC parts removed             7/7
///     [ ] Cables unplugged             2/5
///     [ ] PC parts reinstalled         3/9
///     ---- BOOTABLE USB ----
///     [x] Install Rufus                1/1
///     [ ] Create bootable flash drive  0/1
///     ---- OS INSTALLATION ----
///     [ ] Install Windows              0/1
///     ---- SYSTEM CONFIGURATION ----
///     [ ] Update the drivers           1/4
///     ...
///
/// ATTACH THIS TO: any object under the scene's main UI Canvas - it builds the tab, the panel and
/// every row itself. One component per scene. It works in COC 1, 2, 3 and 4 because it is told
/// WHICH tasks to watch rather than knowing what a module is.
///
/// ---------------------------------------------------------------------------------------------
/// WHY IT IS A LIST OF SOURCES AND NOT A LIST OF TASK IDS
///
/// "1/7" needs two numbers, and TaskManager only answers the one: IsTaskComplete. Its current and
/// required live in a private struct, so for a plain task id they are read by reflection, once
/// registered - and a row falls back to done/not-done if that ever stops working, with a line in
/// the Console saying so rather than a silent 0/0.
///
/// Where a system already publishes the pair, the row asks IT instead, because a count that comes
/// from the owner can never disagree with the window the learner is looking at:
///
///     Checklist Tool  ->  ChecklistToolManager.Items + IsItemComplete   (drivers, software, updates)
///     Diagnostics     ->  DiagnosticsTestManager.Tests + GetLastResult
///     OS Install      ->  OSInstallManager.CurrentStage == Complete     (not a TaskManager task)
///     Cables          ->  CableConnector.isPlugged across the scene
///
/// OS INSTALL IS THE ONE THAT WOULD HAVE BEEN WRONG. It reports to no task id at all -
/// COC1CompletionManager checks CurrentStage directly - so a row watching "install_os" would have
/// sat at 0/1 through the entire installation and for ever afterwards.
/// ---------------------------------------------------------------------------------------------
///
/// IT READS STATE, IT NEVER STORES IT. Every refresh re-asks the owners, so a resumed save is right
/// with no persistence of its own, and a row cannot go stale while the sidebar is shut.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class TaskSidebar : MonoBehaviour
{
    // ==================================================================
    // What a row watches
    // ==================================================================

    public enum RowSource
    {
        /// <summary>A TaskManager task, counted: "3/7". Use this for anything with steps.</summary>
        TaskSteps = 0,

        /// <summary>A TaskManager task, done or not: "0/1". Use this for one-off jobs.</summary>
        TaskDone = 1,

        /// <summary>Installed items in the ChecklistToolManager whose GROUP id is Task Id.</summary>
        ChecklistTool = 2,

        /// <summary>Diagnostic tests that have produced a result.</summary>
        Diagnostics = 3,

        /// <summary>The whole OS installation as one task. Task Id is ignored.</summary>
        OSInstall = 4,

        /// <summary>Cables currently unplugged, out of every cable that counts. Disassembly.</summary>
        CablesUnplugged = 5,

        /// <summary>Cables currently plugged in, out of every cable that counts. Assembly.</summary>
        CablesPlugged = 6,

        /// <summary>Cables put back, out of the cables the learner actually unplugged. Assembly.</summary>
        CablesRestored = 7,

        /// <summary>
        /// A reassembly task's PARTS only, with the cable steps taken back out of both numbers.
        /// Task Id is the assembly task, e.g. assemble_pc.
        /// </summary>
        AssemblePartsOnly = 8,
    }

    [Serializable]
    public class Row
    {
        [Tooltip("What the learner reads. Keep it short - the sidebar is deliberately narrow.")]
        public string label = "Task";

        public RowSource source = RowSource.TaskSteps;

        [Tooltip("Task Steps / Task Done: the TaskManager task id, e.g. disassemble_pc.\n" +
                 "Checklist Tool: the tool's GROUP id, e.g. install_drivers.\n" +
                 "Ignored for Diagnostics, OS Install and Cables Unplugged.")]
        public string taskId = "";
    }

    [Serializable]
    public class Section
    {
        [Tooltip("The heading above this group, e.g. \"BOOTABLE USB\". Leave empty for no heading.")]
        public string title = "SECTION";

        public List<Row> rows = new List<Row>();
    }

    // ==================================================================
    // Configuration
    // ==================================================================

    public enum RevealMode
    {
        /// <summary>Every row, all the time.</summary>
        All = 0,

        /// <summary>Finished rows and the one being worked on. What is coming next stays hidden.</summary>
        UpToCurrent = 1,

        /// <summary>The one being worked on, and nothing else.</summary>
        CurrentOnly = 2,
    }

    [Header("Tasks")]
    [Tooltip("The sections of this module's job order, in the order they must be done.\n\n" +
             "Leave EMPTY and Auto Fill builds them from whatever systems this scene actually has - " +
             "which is the whole of COC1. For COC2, COC3 and COC4, add your own rows here: the task " +
             "ids are different, so nothing can guess them for you.")]
    [SerializeField] private List<Section> sections = new List<Section>();

    [Tooltip("With Sections empty, build rows from the systems found in this scene. Turn this OFF " +
             "once you have filled Sections in by hand, so a stray manager cannot add a row.")]
    [SerializeField] private bool autoFillFromScene = true;

    [Tooltip("How much of the job order the learner can see at once.\n\n" +
             "UP TO CURRENT (recommended): what is done, plus the task in hand. The work is in a " +
             "fixed order and the panel should not read like a menu of choices - a learner who can " +
             "see step 7 while on step 2 will try to jump to it. Finished rows stay on show because " +
             "that is the part worth seeing: the ticks are the evidence of what has been done.\n\n" +
             "CURRENT ONLY: one line, nothing else. The tightest option for a phone.\n\n" +
             "ALL: the whole list, as before.\n\n" +
             "Rows LATER in the order stay hidden even when something has already made them " +
             "complete - otherwise a row could tick itself before the learner reached it.")]
    [SerializeField] private RevealMode reveal = RevealMode.UpToCurrent;

    [Tooltip("The most rows on show at once, with Up To Current. 0 means no limit.\n\n" +
             "This is a HEIGHT limit, not a style choice: twelve rows at a size readable on a phone " +
             "comes to about 1100 pixels, which is taller than the screen, and the bottom of the " +
             "list would simply be off it. Six keeps the task in hand plus the last five ticked, " +
             "which is what a learner actually looks at.")]
    [SerializeField] private int maxRowsShown = 6;

    [Header("Panel")]
    [SerializeField] private string title = "TASKS";
    [SerializeField] private float panelWidth = 420f;
    [SerializeField] private float topMargin = 60f;
    [SerializeField] private float headerHeight = 62f;
    [SerializeField] private float sectionHeight = 40f;
    [SerializeField] private float rowHeight = 72f;

    [Tooltip("Start with the sidebar open. Off by default: the arrow is the invitation, and a panel " +
             "that opens itself over the machine is the thing learners close and never reopen.")]
    [SerializeField] private bool startOpen;

    [Tooltip("How long the slide takes, in seconds. 0 snaps it open.")]
    [SerializeField] private float slideSeconds = 0.18f;

    [Header("Arrow tab")]
    [SerializeField] private Vector2 tabSize = new Vector2(56f, 130f);

    [Tooltip("Height of the tab's centre as a fraction of the screen. 0.5 is the middle, which is " +
             "the easiest place to reach with a thumb.")]
    [Range(0f, 1f)]
    [SerializeField] private float tabHeight = 0.5f;

    [Header("Look")]
    [SerializeField] private Color cardColour = new Color32(0x12, 0x23, 0x3B, 0xF0);
    [SerializeField] private Color headerColour = new Color32(0x1B, 0x33, 0x53, 0xFF);
    [SerializeField] private Color tabColour = new Color32(0x1B, 0x33, 0x53, 0xF0);
    [SerializeField] private Color titleColour = Color.white;
    [SerializeField] private Color sectionColour = new Color32(0x8F, 0xA6, 0xC4, 0xFF);
    [SerializeField] private Color pendingColour = new Color32(0xC3, 0xD7, 0xF7, 0xFF);
    [SerializeField] private Color doneColour = new Color32(0x8F, 0xA6, 0xC4, 0xFF);
    [SerializeField] private Color completeColour = new Color32(0x6E, 0xE7, 0xB7, 0xFF);
    [SerializeField] private Color activeColour = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

    [SerializeField] private float titleFontSize = 26f;
    [SerializeField] private float sectionFontSize = 18f;
    [SerializeField] private float labelFontSize = 24f;
    [SerializeField] private float counterFontSize = 22f;

    [Header("Behaviour")]
    [Tooltip("Flash a row gold as it completes, so a tick that happens while the sidebar is open but " +
             "unwatched is not missed entirely.")]
    [SerializeField] private bool flashOnComplete = true;

    [Tooltip("How often the rows re-read the task state, in seconds. Counting a handful of rows is " +
             "cheap, but it is not free and nothing here changes between frames.")]
    [SerializeField] private float refreshSeconds = 0.2f;

    [Header("Cables")]
    [Tooltip("Count cables on switched-off objects too.\n\n" +
             "ON, and it has to be ON in this project: an unplugged cable here ends up on a " +
             "switched-off object, so skipping inactive ones counted only the cables still plugged " +
             "in - the exact opposite of the work outstanding. Turn it off only if your scene keeps " +
             "spare cables around that are not part of the job.")]
    [SerializeField] private bool countInactiveCables = true;

    [Tooltip("Force the cable total to this number. 0 (recommended) counts the cables in the scene " +
             "whose Counts Toward Assembly is ticked. Set it only if the count is still wrong and " +
             "you want the panel to read correctly while you find out why - right-click this " +
             "component and choose Report to see exactly which cables were counted.")]
    [SerializeField] private int cableTotalOverride;

    [Header("Diagnostics")]
    [SerializeField] private bool logSources = true;

    // ==================================================================

    private const float Pad = 10f;

    private class Live
    {
        public Row row;
        public RectTransform rect;
        public int section;
        public bool visible = true;
        public Image boxBorder;
        public Image boxFill;
        public Image accent;
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

    /// <summary>A section's heading objects, so a section whose rows are all hidden hides too.</summary>
    private class SectionUi
    {
        public RectTransform heading;
        public RectTransform rule;
        public bool hasTitle;
    }

    private readonly List<SectionUi> sectionUi = new List<SectionUi>();

    private RectTransform card;
    private RectTransform tab;
    private TMP_Text tabArrow;
    private TMP_Text titleText;
    private TMP_Text summaryText;

    private bool built;
    private bool open;
    private float openness;          // 0 shut, 1 open
    private float cardHeight;
    private float nextRefresh;

    // ==================================================================

    private void OnEnable()
    {
        EnsureBuilt();
        Repaint(true);
    }

    private void Start()
    {
        // Again in Start: the managers this reads register their tasks in their own Start, and on the
        // first frame a count can otherwise read 0/0 for something a loaded save already finished.
        EnsureBuilt();
        Repaint(true);
    }

    private void Update()
    {
        if (!built) return;

        float target = open ? 1f : 0f;

        if (!Mathf.Approximately(openness, target))
        {
            openness = slideSeconds <= 0f
                ? target
                : Mathf.MoveTowards(openness, target, Time.unscaledDeltaTime / slideSeconds);

            ApplySlide();
        }

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshSeconds);

        // Repainted while shut as well as open: the tab's own summary is the point of a shut
        // sidebar, and a row that ticked while it was closed must already be right when it opens.
        Repaint(false);
    }

    // ==================================================================
    // Opening
    // ==================================================================

    public void Toggle() => SetOpen(!open);

    public void SetOpen(bool value)
    {
        open = value;
        if (tabArrow != null) tabArrow.text = open ? ">" : "<";
    }

    private void ApplySlide()
    {
        if (card != null)
            card.anchoredPosition = new Vector2(panelWidth * (1f - openness), -topMargin);

        if (tab != null)
            tab.anchoredPosition = new Vector2(-panelWidth * openness, 0f);
    }

    // ==================================================================
    // Building
    // ==================================================================

    private void EnsureBuilt()
    {
        if (built) return;

        var parent = transform as RectTransform;
        if (parent == null)
        {
            Debug.LogWarning("[TaskSidebar] This object has no RectTransform, so there is nothing to " +
                             "build into. Put this component on an object under the scene's UI Canvas.",
                             this);
            enabled = false;
            return;
        }

        if (sections.Count == 0 && autoFillFromScene) sections = AutoFill();

        if (sections.Count == 0)
        {
            Debug.LogWarning("[TaskSidebar] No sections, and nothing in this scene to build them " +
                             "from. Add your rows to Sections in the Inspector - in COC2, COC3 and " +
                             "COC4 the task ids are this module's own, so they have to be typed in.",
                             this);
            enabled = false;
            return;
        }

        // A starting height only; Relayout sets the real one once it knows which rows are on show.
        cardHeight = headerHeight + Pad + Pad;

        card = RightEdge(parent, "TaskSidebarPanel", panelWidth, cardHeight, 1f);
        var cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = cardColour;
        cardImage.raycastTarget = true;   // the panel absorbs its own taps, not the scene's

        BuildHeader();
        BuildBody();

        tab = RightEdge(parent, "TaskSidebarTab", tabSize.x, tabSize.y, tabHeight);
        var tabImage = tab.gameObject.AddComponent<Image>();
        tabImage.color = tabColour;
        tabImage.raycastTarget = true;

        tabArrow = Label(tab, "Arrow", 0f, 0f, tabSize.x, tabSize.y, "<", titleFontSize + 4f,
                         titleColour, TextAlignmentOptions.Center);

        var button = tab.gameObject.AddComponent<Button>();
        button.targetGraphic = tabImage;
        button.onClick.AddListener(Toggle);

        // Both drawn last so the sidebar is never behind another panel in the same canvas. The tab
        // after the card, so the tab stays on top while it sits against the card's edge.
        card.SetAsLastSibling();
        tab.SetAsLastSibling();

        built = true;

        SetOpen(startOpen);
        openness = startOpen ? 1f : 0f;
        ApplySlide();

        if (logSources) ReportSources();
    }

    private void BuildHeader()
    {
        var header = Cell(card, "Header", 0f, 0f, panelWidth, headerHeight);
        var bg = header.gameObject.AddComponent<Image>();
        bg.color = headerColour;
        bg.raycastTarget = false;

        titleText = Label(header, "Title", Pad + 4f, 0f, panelWidth - 130f, headerHeight,
                          title, titleFontSize, titleColour, TextAlignmentOptions.Left);
        titleText.fontStyle = FontStyles.Bold;

        summaryText = Label(header, "Summary", panelWidth - 110f, 0f, 96f, headerHeight,
                            "0 / 0", titleFontSize, titleColour, TextAlignmentOptions.Right);
    }

    private void BuildBody()
    {
        for (int s = 0; s < sections.Count; s++)
        {
            Section section = sections[s];
            var ui = new SectionUi { hasTitle = !string.IsNullOrEmpty(section.title) };

            if (ui.hasTitle)
            {
                var heading = Label(card, "Section_" + s, Pad + 4f, 0f, panelWidth - 20f, sectionHeight,
                                    section.title.ToUpperInvariant(), sectionFontSize, sectionColour,
                                    TextAlignmentOptions.Left);
                heading.fontStyle = FontStyles.Bold;
                ui.heading = heading.rectTransform;

                ui.rule = Cell(card, "Rule_" + s, Pad + 4f, 0f, panelWidth - 28f, 1f);
                var ruleImage = ui.rule.gameObject.AddComponent<Image>();
                ruleImage.color = new Color(1f, 1f, 1f, 0.14f);
                ruleImage.raycastTarget = false;
            }

            sectionUi.Add(ui);

            for (int r = 0; r < section.rows.Count; r++)
            {
                Live entry = BuildRow(section.rows[r], s + "_" + r);
                entry.section = s;
                live.Add(entry);
            }
        }

        // Positions are assigned by Relayout rather than here, because which rows are on show
        // changes as the learner works - see Relayout.
        Relayout();
    }

    /// <summary>
    /// Places every visible row, hides the rest, and resizes the card to what is actually on show.
    ///
    /// WHY POSITIONS ARE NOT FIXED AT BUILD TIME. With the job order revealed a step at a time, a
    /// hidden row must not leave a hole where it was, and the panel must not be a card of empty
    /// colour waiting for rows that have not arrived. So the visible rows are stacked from the top
    /// each time the set changes, and the card's height follows them.
    /// </summary>
    private void Relayout()
    {
        float y = headerHeight + Pad;

        for (int s = 0; s < sectionUi.Count; s++)
        {
            bool anyVisible = false;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].section == s && live[i].visible) { anyVisible = true; break; }
            }

            SectionUi ui = sectionUi[s];

            if (ui.hasTitle)
            {
                if (ui.heading != null) ui.heading.gameObject.SetActive(anyVisible);
                if (ui.rule != null) ui.rule.gameObject.SetActive(anyVisible);

                if (anyVisible)
                {
                    if (ui.heading != null) PixelRect(ui.heading, Pad + 4f, y, panelWidth - 20f, sectionHeight);
                    if (ui.rule != null) PixelRect(ui.rule, Pad + 4f, y + sectionHeight - 6f, panelWidth - 28f, 1f);
                    y += sectionHeight;
                }
            }

            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].section != s) continue;

                live[i].rect.gameObject.SetActive(live[i].visible);
                if (!live[i].visible) continue;

                PixelRect(live[i].rect, 0f, y, panelWidth, rowHeight);
                y += rowHeight;
            }
        }

        cardHeight = y + Pad;
        card.sizeDelta = new Vector2(panelWidth, cardHeight);

        // The card is pinned by its top-right corner, so a height change alone would leave it in
        // place - but the slide offset has to be reapplied after any resize.
        ApplySlide();
    }

    private Live BuildRow(Row row, string id)
    {
        var rect = Cell(card, "Row_" + id, 0f, 0f, panelWidth, rowHeight);
        var entry = new Live { row = row, rect = rect };

        // The stripe down the left edge. Only the active row shows one, which is what makes "the one
        // I am on" readable at a glance instead of something to work out.
        var accent = Cell(rect, "Accent", 0f, 6f, 5f, rowHeight - 12f);
        entry.accent = accent.gameObject.AddComponent<Image>();
        entry.accent.color = activeColour;
        entry.accent.raycastTarget = false;

        // THE CHECKBOX IS DRAWN, NOT TYPED. A tick character is not in every TMP font atlas, and a
        // glyph the font lacks renders as a hollow rectangle - which on a checkbox cannot be told
        // apart from "not done yet". Two squares cannot be got wrong by a font.
        // 28px box against a 24pt label: sized to be read at arm's length on a phone, not to be
        // decorative next to the text.
        var box = Cell(rect, "Box", 16f, 8f, 28f, 28f);
        entry.boxBorder = box.gameObject.AddComponent<Image>();
        entry.boxBorder.color = pendingColour;
        entry.boxBorder.raycastTarget = false;

        var inner = Cell(box, "BoxFill", 3f, 3f, 22f, 22f);
        entry.boxFill = inner.gameObject.AddComponent<Image>();
        entry.boxFill.color = cardColour;
        entry.boxFill.raycastTarget = false;

        entry.label = Label(rect, "Label", 56f, 4f, panelWidth - 56f - 100f, 36f,
                            row.label, labelFontSize, pendingColour, TextAlignmentOptions.Left);

        entry.counter = Label(rect, "Counter", panelWidth - 96f, 4f, 80f, 36f,
                              "0/0", counterFontSize, pendingColour, TextAlignmentOptions.Right);

        // Track, then fill on top of it anchored left, so the fill's WIDTH is the progress. No
        // Slider for something that is never dragged.
        float barW = panelWidth - 32f - 16f;
        var track = Cell(rect, "BarTrack", 16f, rowHeight - 20f, barW, 8f);
        var trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(1f, 1f, 1f, 0.12f);
        trackImage.raycastTarget = false;

        entry.barWidth = barW;
        entry.barFill = Cell(track, "BarFill", 0f, 0f, 0f, 8f);
        var fillImage = entry.barFill.gameObject.AddComponent<Image>();
        fillImage.color = completeColour;
        fillImage.raycastTarget = false;

        return entry;
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

            // A flash is a colour that decays, so it has to be rewritten while it runs even when the
            // numbers have not moved.
            if (entry.flashUntil > Time.unscaledTime) PaintFlash(entry);
            else if (entry.flashUntil != 0f) { entry.flashUntil = 0f; Paint(entry); }
        }

        // The active row is the FIRST unfinished one - the next thing to do, rather than whichever
        // happens to be half done.
        int active = -1;
        for (int i = 0; i < live.Count; i++)
        {
            if (!live[i].complete) { active = i; break; }
        }

        bool visibilityChanged = false;

        for (int i = 0; i < live.Count; i++)
        {
            bool isActive = i == active;
            live[i].accent.enabled = isActive;

            // WHAT IS ON SHOW. The rule is by POSITION IN THE ORDER, not by whether a row happens to
            // be finished: a row later in the list stays hidden even when its numbers already read
            // complete. Without that, "Cables plugged 5/5" would tick itself on the first frame -
            // every cable starts plugged in - and the learner would see a task ticked before they
            // had reached it, let alone done it.
            // "Where the learner is": the active row, or the last row once everything is finished.
            int here = active < 0 ? live.Count - 1 : active;

            // The oldest row still on show, so a long job order cannot grow taller than the screen.
            int first = reveal == RevealMode.UpToCurrent && maxRowsShown > 0
                ? Mathf.Max(0, here - (maxRowsShown - 1))
                : 0;

            bool show = reveal == RevealMode.All
                        || (reveal == RevealMode.UpToCurrent && i >= first && i <= here)
                        || (reveal == RevealMode.CurrentOnly && i == here);

            if (live[i].visible != show)
            {
                live[i].visible = show;
                visibilityChanged = true;
            }

            if (live[i].flashUntil > Time.unscaledTime) continue;

            live[i].label.color = live[i].complete ? doneColour
                                : isActive ? activeColour
                                : pendingColour;
        }

        if (visibilityChanged) Relayout();

        string summary = rowsDone + " / " + live.Count;
        if (summaryText != null && summaryText.text != summary) summaryText.text = summary;

        if (titleText != null)
        {
            string caption = live.Count > 0 && rowsDone >= live.Count ? "ALL TASKS DONE" : title;
            if (titleText.text != caption) titleText.text = caption;
        }
    }

    private void Paint(Live entry)
    {
        // Empty box while pending, filled green once done: the same read as a tick, with nothing for
        // a font to fail at.
        entry.boxBorder.color = entry.complete ? completeColour : pendingColour;
        entry.boxFill.color = entry.complete ? completeColour : cardColour;

        entry.counter.text = entry.total > 0 ? entry.done + "/" + entry.total : "-";
        entry.counter.color = entry.complete ? completeColour : pendingColour;

        float fraction = entry.total > 0 ? Mathf.Clamp01((float)entry.done / entry.total) : 0f;
        PixelRect(entry.barFill, 0f, 0f, entry.barWidth * fraction, 8f);
    }

    private void PaintFlash(Live entry)
    {
        entry.label.color = activeColour;
        entry.boxBorder.color = activeColour;
        entry.boxFill.color = activeColour;
    }

    private void Measure(Live entry, out int done, out int total)
    {
        done = 0;
        total = 0;

        switch (entry.row.source)
        {
            case RowSource.TaskSteps:
                int current, required;
                if (ReadTaskProgress(entry.row.taskId, out current, out required))
                {
                    done = current;
                    total = Mathf.Max(1, required);
                    return;
                }

                // Not registered yet, or the step counts could not be read. Done/not-done is still
                // an honest answer, and it ticks correctly - it just cannot say "3 of 7".
                total = 1;
                done = IsComplete(entry.row.taskId) ? 1 : 0;
                return;

            case RowSource.TaskDone:
                total = 1;
                done = IsComplete(entry.row.taskId) ? 1 : 0;
                return;

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
                    // DiagnosticsTestManager itself reports to TaskManager. Whether everything
                    // passed is the service report's business, not this sidebar's.
                    if (tests.GetLastResult(test).HasValue) done++;
                }
                return;

            case RowSource.OSInstall:
                total = 1;
                done = OSInstallManager.Instance != null
                       && OSInstallManager.Instance.CurrentStage == OSInstallManager.InstallStage.Complete
                       ? 1 : 0;
                return;

            case RowSource.AssemblePartsOnly:
                int steps, needed;
                if (ReadTaskProgress(entry.row.taskId, out steps, out needed))
                {
                    int put, pulled;

                    // The cable steps come straight back out of BOTH numbers. PCDisassemblySetup
                    // raises what assemble_pc requires by one per cable unplugged, so its required
                    // count is parts + cables - and a row labelled "parts" must not quote it.
                    if (ReadCablesRestored(out put, out pulled))
                    {
                        done = Mathf.Max(0, steps - put);
                        total = Mathf.Max(1, needed - pulled);
                    }
                    else
                    {
                        done = steps;
                        total = Mathf.Max(1, needed);
                    }
                    return;
                }

                total = 1;
                done = IsComplete(entry.row.taskId) ? 1 : 0;
                return;

            case RowSource.CablesRestored:
                int restored, owed;
                if (ReadCablesRestored(out restored, out owed))
                {
                    // Nothing has been unplugged yet: 0 of 0 would read as finished, so the row shows
                    // one outstanding step until there is something to put back.
                    total = Mathf.Max(1, owed);
                    done = owed == 0 ? 0 : restored;
                    return;
                }

                // Could not read the sets - fall back to counting cables that are plugged in.
                CountCables(true, out done, out total);
                return;

            case RowSource.CablesUnplugged:
                int countedOut, alreadyBack, pulledOut;

                // The DENOMINATOR is how many cables the PC has; the NUMERATOR is how many the game
                // says the learner pulled. Counting unplugged objects for the numerator would also
                // credit a cable that was already out when the scene loaded, and would miss one whose
                // object is switched off - which is exactly how this read "2 of 6".
                CountCables(false, out countedOut, out total);

                done = ReadCablesRestored(out alreadyBack, out pulledOut) ? pulledOut : countedOut;
                return;

            default:   // CablesPlugged
                CountCables(true, out done, out total);
                return;
        }
    }

    private struct CableGroup
    {
        public int ends;
        public int plugged;
    }

    private readonly Dictionary<VerletRope, CableGroup> ropes = new Dictionary<VerletRope, CableGroup>();

    /// <summary>
    /// Cables plugged in, or unplugged, out of the cables that are actually part of the job.
    ///
    /// THREE THINGS DECIDE WHAT COUNTS, and each was a wrong number on its own.
    ///
    /// countsTowardAssembly is CableConnector's own flag, and it is the same flag the game itself
    /// obeys - Plug and Unplug only tell PCDisassemblySetup when it is ticked. Honouring it here
    /// keeps this panel and the assemble_pc task counting the same set of cables.
    ///
    /// Switched-off objects are INCLUDED by default, because in this project an unplugged cable ends
    /// up inactive. Skipping them counted only the cables still plugged in, which is how a PC with
    /// five cables out reported "2 of 6".
    ///
    /// Connectors that share a rope are ONE cable. A lead with a draggable plug at both ends has two
    /// CableConnectors and is still one cable to the person holding it, so counting ends would put
    /// the total above the number of cables in the case.
    /// </summary>
    private void CountCables(bool wantPlugged, out int done, out int total)
    {
        done = 0;
        total = 0;
        ropes.Clear();

        CableConnector[] cables = FindObjectsByType<CableConnector>(
            countInactiveCables ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < cables.Length; i++)
        {
            CableConnector cable = cables[i];
            if (cable == null || !cable.countsTowardAssembly) continue;

            if (cable.rope == null)
            {
                // Its own cable: one end, and that end's state is the cable's state.
                total++;
                if (cable.isPlugged == wantPlugged) done++;
                continue;
            }

            CableGroup group;
            ropes.TryGetValue(cable.rope, out group);

            group.ends++;
            if (cable.isPlugged) group.plugged++;

            ropes[cable.rope] = group;
        }

        foreach (CableGroup group in ropes.Values)
        {
            total++;

            // A cable counts as plugged only when EVERY end of it is in, and as unplugged as soon as
            // any end is out - which is what the game does too: pulling either end raises what
            // assemble_pc asks for.
            bool fullyPlugged = group.plugged >= group.ends;
            if (fullyPlugged == wantPlugged) done++;
        }

        if (cableTotalOverride > 0)
        {
            total = cableTotalOverride;
            done = Mathf.Min(done, total);
        }
    }

    // ==================================================================

    private static FieldInfo toRestoreField;
    private static FieldInfo restoredField;
    private static bool lookedForCableSets;

    /// <summary>
    /// Cables put back, out of the cables the learner actually pulled out.
    ///
    /// THIS IS THE ONE TO USE ON THE ASSEMBLY ROW, and it is the only source that cannot disagree
    /// with the task it describes. PCDisassemblySetup raises what assemble_pc requires by one for
    /// every cable unplugged and reports a step for every cable put back - so its own two sets ARE
    /// the assembly's cable progress. Counting cable objects instead answers a different question
    /// ("how many cables exist"), which is why a PC with six connectors and five of them pulled can
    /// read 5 in one place and 6 in another and have both be correct.
    /// </summary>
    private static bool ReadCablesRestored(out int restored, out int owed)
    {
        restored = 0;
        owed = 0;

        PCDisassemblySetup setup = PCDisassemblySetup.Instance;
        if (setup == null) return false;

        if (!lookedForCableSets)
        {
            lookedForCableSets = true;

            const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            toRestoreField = typeof(PCDisassemblySetup).GetField("cablesToRestore", Hidden);
            restoredField = typeof(PCDisassemblySetup).GetField("cablesRestored", Hidden);
        }

        if (toRestoreField == null || restoredField == null) return false;

        var owedSet = toRestoreField.GetValue(setup) as ICollection;
        var restoredSet = restoredField.GetValue(setup) as ICollection;

        if (owedSet == null || restoredSet == null) return false;

        owed = owedSet.Count;
        restored = restoredSet.Count;
        return true;
    }

    private static bool IsComplete(string taskId)
    {
        return TaskManager.Instance != null
               && !string.IsNullOrEmpty(taskId)
               && TaskManager.Instance.IsTaskComplete(taskId);
    }

    // ==================================================================
    // Reading step counts out of TaskManager
    // ==================================================================

    private static FieldInfo tasksField;
    private static FieldInfo currentField;
    private static FieldInfo requiredField;
    private static bool lookedForTasks;
    private static bool warnedAboutTasks;

    /// <summary>
    /// The current and required step counts of a task.
    ///
    /// REFLECTION, AND WHY IT IS THE RIGHT CALL HERE. TaskManager keeps its progress in a private
    /// Dictionary of a private struct and exposes only IsTaskComplete. The alternative is to add a
    /// public accessor to TaskManager - which is cleaner, and worth doing if this sidebar stays -
    /// but a display feature should not require a change to the class every module depends on to
    /// save correctly. Nothing is written, and a failure falls back to done/not-done with one
    /// warning rather than a row stuck at 0/0.
    /// </summary>
    private static bool ReadTaskProgress(string taskId, out int current, out int required)
    {
        current = 0;
        required = 0;

        if (string.IsNullOrEmpty(taskId) || TaskManager.Instance == null) return false;

        if (!lookedForTasks)
        {
            lookedForTasks = true;
            tasksField = typeof(TaskManager).GetField(
                "tasks", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (tasksField == null)
        {
            if (!warnedAboutTasks)
            {
                warnedAboutTasks = true;
                Debug.LogWarning("[TaskSidebar] TaskManager has no private field called 'tasks' any " +
                                 "more, so step counts cannot be read. Every Task Steps row will " +
                                 "show 0/1 or 1/1 instead of \"3/7\" - it still ticks correctly.");
            }
            return false;
        }

        var dictionary = tasksField.GetValue(TaskManager.Instance) as IDictionary;
        if (dictionary == null || !dictionary.Contains(taskId)) return false;

        object progress = dictionary[taskId];
        if (progress == null) return false;

        if (currentField == null || requiredField == null)
        {
            Type type = progress.GetType();
            currentField = type.GetField("current");
            requiredField = type.GetField("required");
        }

        if (currentField == null || requiredField == null) return false;

        current = (int)currentField.GetValue(progress);
        required = (int)requiredField.GetValue(progress);
        return required > 0;
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

    // ==================================================================
    // Filling the rows in
    // ==================================================================

    /// <summary>
    /// Builds the sections from the systems this scene actually contains.
    ///
    /// PRESENCE, NOT A MODULE NAME. A scene with a PCDisassemblySetup gets the assembly rows; one
    /// with an OSInstallManager gets the install row. That is why the same component works in COC1
    /// without being told it is COC1 - and why it adds nothing at all in a scene it knows nothing
    /// about, instead of showing four rows that can never tick.
    /// </summary>
    [ContextMenu("Fill sections from this scene")]
    public List<Section> AutoFill()
    {
        var built = new List<Section>();

        bool hasDisassembly = FindAnyObjectByType<PCDisassemblySetup>(FindObjectsInactive.Include) != null;
        bool hasRufus = FindAnyObjectByType<RufusSetupManager>(FindObjectsInactive.Include) != null;
        bool hasUsb = FindAnyObjectByType<BootableUSBCreator>(FindObjectsInactive.Include) != null;
        bool hasInstall = FindAnyObjectByType<OSInstallManager>(FindObjectsInactive.Include) != null;
        bool hasTools = FindAnyObjectByType<ChecklistToolManager>(FindObjectsInactive.Include) != null;

        if (hasDisassembly)
        {
            var s = new Section { title = "Disassembly & assembly", rows = new List<Row>() };

            // Disassembly, then assembly, in the order the work happens: parts out, cables out,
            // cables back in, parts back in. Cables plugged sits BEFORE parts reinstalled because a
            // lead behind a drive cage cannot be reached once the cage is back in - the same reason
            // a technician plugs before closing up.
            s.rows.Add(new Row { label = "PC parts removed", source = RowSource.TaskSteps, taskId = PCDisassemblySetup.TaskId });
            s.rows.Add(new Row { label = "Cables unplugged", source = RowSource.CablesUnplugged });
            s.rows.Add(new Row { label = "Cables plugged back in", source = RowSource.CablesRestored });
            s.rows.Add(new Row { label = "PC parts reinstalled", source = RowSource.AssemblePartsOnly, taskId = PCDisassemblySetup.AssembleTaskId });

            built.Add(s);
        }

        if (hasRufus || hasUsb)
        {
            var s = new Section { title = "Bootable USB", rows = new List<Row>() };

            if (hasRufus) s.rows.Add(new Row { label = "Install Rufus", source = RowSource.TaskDone, taskId = RufusSetupManager.TaskId });
            if (hasUsb) s.rows.Add(new Row { label = "Create bootable flash drive", source = RowSource.TaskDone, taskId = BootableUSBCreator.TaskId });

            built.Add(s);
        }

        if (hasInstall)
        {
            built.Add(new Section
            {
                title = "OS installation",
                rows = new List<Row> { new Row { label = "Install Windows", source = RowSource.OSInstall } }
            });
        }

        if (hasTools)
        {
            var s = new Section { title = "System configuration", rows = new List<Row>() };

            s.rows.Add(new Row { label = "Update the drivers", source = RowSource.ChecklistTool, taskId = SystemConfigTaskIds.InstallDrivers });
            s.rows.Add(new Row { label = "Install software", source = RowSource.ChecklistTool, taskId = SystemConfigTaskIds.InstallAppSoftware });
            s.rows.Add(new Row { label = "Update Windows", source = RowSource.ChecklistTool, taskId = SystemConfigTaskIds.InstallUpdates });
            s.rows.Add(new Row { label = "Test the computer", source = RowSource.Diagnostics });
            s.rows.Add(new Row { label = "Service report", source = RowSource.TaskDone, taskId = SystemConfigTaskIds.DocumentWork });

            built.Add(s);
        }

#if UNITY_EDITOR
        // So a right-click in the Editor actually sticks: the rows land in Sections and can then be
        // renamed and reordered by hand, which is how you get from this to COC2's own list.
        if (!Application.isPlaying)
        {
            sections = built;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        return built;
    }

    /// <summary>
    /// Says in the Console what each row is watching, and what it could not find.
    ///
    /// A row with no source reads "-" and never ticks, which from the outside looks exactly like a
    /// task the learner has not reached yet. This is the difference between the two.
    /// </summary>
    [ContextMenu("Report")]
    public void ReportSources()
    {
        var sb = new System.Text.StringBuilder($"[TaskSidebar] {live.Count} row(s)\n");

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

                case RowSource.OSInstall:
                    found = OSInstallManager.Instance != null
                        ? "OSInstallManager stage " + OSInstallManager.Instance.CurrentStage
                        : "waiting for OSInstallManager";
                    break;

                case RowSource.CablesUnplugged:
                case RowSource.CablesPlugged:
                case RowSource.CablesRestored:
                    found = DescribeCables();
                    break;

                case RowSource.AssemblePartsOnly:
                    int aSteps, aNeeded, aPut, aPulled;
                    found = ReadTaskProgress(entry.row.taskId, out aSteps, out aNeeded)
                        ? (ReadCablesRestored(out aPut, out aPulled)
                            ? $"task '{entry.row.taskId}' ({aSteps}/{aNeeded}) minus {aPulled} cable " +
                              $"step(s) = {Mathf.Max(0, aSteps - aPut)}/{Mathf.Max(1, aNeeded - aPulled)} parts"
                            : $"task '{entry.row.taskId}' ({aSteps}/{aNeeded}) - cable steps could not " +
                              "be separated, so this includes them")
                        : $"task '{entry.row.taskId}' (not registered yet)";
                    break;

                default:
                    int c, r;
                    found = ReadTaskProgress(entry.row.taskId, out c, out r)
                        ? $"task '{entry.row.taskId}' ({c}/{r})"
                        : $"task '{entry.row.taskId}' (not registered yet - counts appear once it is)";
                    break;
            }

            sb.AppendLine("  " + entry.row.label + "  ->  " + found);
        }

        Debug.Log(sb.ToString(), this);
    }

    /// <summary>
    /// Names every cable and says whether it was counted - the line to read when the total is one
    /// more than the number of cables you put in the scene.
    /// </summary>
    private string DescribeCables()
    {
        CableConnector[] cables = FindObjectsByType<CableConnector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int counted;
        int total;
        CountCables(true, out counted, out total);

        var sb = new System.Text.StringBuilder($"{total} cable(s) counted of {cables.Length} found");

        for (int i = 0; i < cables.Length; i++)
        {
            CableConnector cable = cables[i];
            if (cable == null) continue;

            bool active = cable.gameObject.activeInHierarchy;
            bool counts = cable.countsTowardAssembly && (active || countInactiveCables);

            sb.Append($"\n      {(counts ? "counted" : "SKIPPED")}  {cable.name}" +
                      $"   plugged={cable.isPlugged}  countsTowardAssembly={cable.countsTowardAssembly}" +
                      $"  active={active}");
        }

        if (cableTotalOverride > 0)
            sb.Append($"\n      Cable Total Override is set to {cableTotalOverride}, so that is the " +
                      "number shown whatever was counted.");

        // The game's own bookkeeping, printed beside mine so the two can be compared directly. If
        // they disagree, the game is right and this panel is counting the wrong set.
        int restored, owed;
        if (ReadCablesRestored(out restored, out owed))
            sb.Append($"\n      PCDisassemblySetup says: {owed} cable(s) unplugged this session, " +
                      $"{restored} put back. That is what assemble_pc is counting.");

        return sb.ToString();
    }

    // ==================================================================
    // Layout
    //
    // EVERYTHING IS PINNED IN PIXELS, NOT PROPORTIONALLY, because this panel MOVES: it slides in and
    // out, and proportional children would stretch and squash as it went. The card hangs off the
    // canvas's right edge and the rows hang off the card's top-left corner, so a slide moves one
    // rectangle and nothing inside it changes shape.
    // ==================================================================

    private static RectTransform RightEdge(Transform parent, string name, float w, float h, float atHeight)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        // Anchored to the right edge at a fraction of the height, so it stays there on any screen
        // shape - a phone in landscape included.
        rect.anchorMin = new Vector2(1f, atHeight);
        rect.anchorMax = new Vector2(1f, atHeight);
        rect.pivot = new Vector2(1f, Mathf.Approximately(atHeight, 1f) ? 1f : 0.5f);
        rect.sizeDelta = new Vector2(w, h);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;

        return rect;
    }

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
        // line through the row below it. The sidebar is narrow on purpose, so this will happen.
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        return label;
    }
}
