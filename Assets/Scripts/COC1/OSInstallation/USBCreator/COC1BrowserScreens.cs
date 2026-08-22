#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the seven screens of the Rufus acquisition sequence in Unity UI, so the whole thing
/// needs no screenshots at all.
///
/// DESIGN SPACE
/// Everything is authored in pixels on a 1920 x 1080 canvas - the usual Unity reference
/// resolution, so the fixed font sizes land about right - and converted to NORMALISED ANCHORS
/// on the way in. The screens then stretch with the panel, and a hotspot rect is the same
/// pixel numbers divided by 1920 and 1080.
///
/// -------------------------------------------------------------------------------------
/// EVERYTHING IS PARENTED TO THE SCREEN ROOT. NOTHING IS NESTED. THIS MATTERS.
///
/// The first version of this file nested children inside their container - the dialog title
/// inside the dialog box, the ad button inside the ad block - which quietly broke every one
/// of them. Anchors are FRACTIONS OF THE PARENT, so a title placed at "x = 32 / 1920" inside
/// a 600px dialog landed at 1.6% of 600px = 10px, and a 536-wide label became 167 wide. Text
/// wrapped where it should not have, rows collapsed into each other, and the hotspots - which
/// were computed in screen space and were correct - no longer sat on the shrunken buttons.
/// That is the overlapping text and the mis-aimed clicks in one bug.
///
/// The fix is the Frame struct below: containers are authored in LOCAL coordinates for
/// readability, but every element is parented to the screen root and positioned in ABSOLUTE
/// design space. Draw order comes from creation order, which is why a container is always
/// created before its contents.
///
/// The same arithmetic produces the hotspot rects (see Hot()), so a drawn button and the
/// invisible hotspot on top of it cannot disagree - they are literally the same numbers.
/// -------------------------------------------------------------------------------------
///
/// A NOTE ON WHAT THIS IS
/// A teaching mock-up, not a copy of anyone's website: the generic shape of a software
/// download page, with plain wording written for this simulation. The advertisement block is
/// a deliberately generic stand-in, labelled as one, because "the biggest green button on a
/// download page is usually an advert" is the most useful habit this screen can teach.
/// </summary>
public static class COC1BrowserScreens
{
    public const float DW = 1920f;
    public const float DH = 1080f;

    private const string UndoLabel = "COC1 Build Screens";

    /// <summary>Every rect the step assets need, in normalised 0-1 panel space.</summary>
    public struct Rects
    {
        public Rect addressBar;
        public Rect go;
        public Rect siteDownload;
        public Rect siteAd;
        public Rect downloadingBar;
        public Rect downloadsFile;
        public Rect uacYes;
        public Rect uacNo;
        public Rect updateYes;
        public Rect splashBar;
    }

    public const string NewTab = "Browser_NewTab";
    public const string Site = "RufusSite";
    public const string Downloading = "Downloading";
    public const string DownloadsBar = "DownloadsBar";
    public const string Uac = "UAC";
    public const string UpdatePolicy = "UpdatePolicy";
    public const string Splash = "Splash";

    public static TMP_FontAsset Font;

    // ==================================================================
    // Palette
    // ==================================================================

    private static readonly Color ChromeTabStrip = Hex(0xDEE1E6);
    private static readonly Color ChromeToolbar = Hex(0xFFFFFF);
    private static readonly Color ChromeOmnibox = Hex(0xF1F3F4);
    private static readonly Color ChromeGlyph = Hex(0x5F6368);
    private static readonly Color PageBg = Hex(0xFFFFFF);
    private static readonly Color PageText = Hex(0x202124);
    private static readonly Color PageMuted = Hex(0x5F6368);
    private static readonly Color LinkBlue = Hex(0x1A73E8);
    private static readonly Color Rule = Hex(0xDADCE0);
    private static readonly Color TableHead = Hex(0xF1F3F4);
    private static readonly Color AdBg = Hex(0xFFF8E1);
    private static readonly Color AdBorder = Hex(0xE0C97F);
    private static readonly Color AdLabelText = Hex(0x8A7A3F);
    private static readonly Color AdButton = Hex(0x2E9E4F);
    private static readonly Color Dim = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color UacBg = Hex(0x1F2A37);
    private static readonly Color UacHeader = Hex(0x0F62A8);
    private static readonly Color UacText = Hex(0xEDEFF2);
    private static readonly Color UacMuted = Hex(0xB9C2CC);
    private static readonly Color UacLink = Hex(0x7FB4E8);
    private static readonly Color DialogBg = Hex(0xF0F0F0);
    private static readonly Color DialogBorder = Hex(0xB4B4B4);
    private static readonly Color ButtonFace = Hex(0xE1E1E1);
    private static readonly Color GoBlue = Hex(0x1A73E8);
    private static readonly Color Faded = Hex(0xBDC1C6);

    // ==================================================================
    // Frame - local authoring, absolute placement
    // ==================================================================

    /// <summary>
    /// A container's design-space origin. Children are written in coordinates local to the
    /// container, but placed on the screen root in absolute design space - which is what stops
    /// the anchor-fraction bug described at the top of this file from ever coming back.
    /// </summary>
    private struct Frame
    {
        public Transform root;
        public float ox, oy;

        public Frame At(float x, float y)
        {
            return new Frame { root = root, ox = ox + x, oy = oy + y };
        }
    }

    // ==================================================================
    // Entry point
    // ==================================================================

    public static Rects BuildAll(Transform screensRoot)
    {
        for (int i = screensRoot.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(screensRoot.GetChild(i).gameObject);

        var r = new Rects();

        BuildNewTab(screensRoot, ref r);
        BuildSite(screensRoot, ref r);
        BuildDownloading(screensRoot, ref r);
        BuildDownloadsBar(screensRoot, ref r);
        BuildUac(screensRoot, ref r);
        BuildUpdatePolicy(screensRoot, ref r);
        BuildSplash(screensRoot, ref r);

        return r;
    }

    // ==================================================================
    // Screens
    // ==================================================================

    private static void BuildNewTab(Transform root, ref Rects r)
    {
        var s = Screen(root, NewTab);

        Box(s, "Page", 0, 100, DW, DH - 100, Hex(0xF8F9FA));

        // Empty omnibox: the live AddressBar sits exactly on top of it on this step, so a URL
        // drawn here would show through behind the real input.
        BrowserChrome(s, "");

        Text(s, "NewTabTitle", 0, 320, DW, 60, "New Tab", 40, PageMuted, TextAlignmentOptions.Center);
        Text(s, "NewTabHint", 0, 396, DW, 40,
             "Type an address above and press Enter.", 22, PageMuted, TextAlignmentOptions.Center);

        r.addressBar = Hot(s, 160, 52, 1480, 40);
        r.go = Hot(s, 1660, 52, 120, 40);
    }

    private static void BuildSite(Transform root, ref Rects r)
    {
        var s = Screen(root, Site);

        Box(s, "Page", 0, 100, DW, DH - 100, PageBg);
        BrowserChrome(s, "rufus.ie");

        Text(s, "Title", 360, 150, 1200, 60, "Rufus", 46, PageText, TextAlignmentOptions.Left, true);
        Text(s, "Tagline", 360, 216, 1200, 34,
             "Create bootable USB drives the easy way", 22, PageMuted);
        Text(s, "Blurb", 360, 258, 1200, 60,
             "A small utility that formats a USB flash drive and writes a bootable disc image to it.\n" +
             "Free and open source.", 18, PageMuted, TextAlignmentOptions.TopLeft);

        Text(s, "DownloadHeader", 360, 350, 400, 40, "Download", 28, PageText,
             TextAlignmentOptions.Left, true);
        Box(s, "HeaderRule", 360, 394, 1200, 2, Rule);

        Box(s, "TableHead", 360, 406, 1200, 34, TableHead);
        Text(s, "ColFile", 374, 406, 400, 34, "File", 16, PageMuted);
        Text(s, "ColVersion", 820, 406, 180, 34, "Version", 16, PageMuted);
        Text(s, "ColDate", 1030, 406, 200, 34, "Date", 16, PageMuted);
        Text(s, "ColSize", 1270, 406, 180, 34, "Size", 16, PageMuted);

        Row(s, 1, 446, "rufus-4.4.exe", "4.4", "2024-04-12", "1.4 MB");
        Row(s, 2, 498, "rufus-4.4p.exe (Portable)", "4.4", "2024-04-12", "1.4 MB");
        Text(s, "Row3File", 374, 550, 400, 40, "Other versions", 19, LinkBlue);

        Text(s, "Publisher", 360, 606, 1200, 30,
             "Published by Akeo Consulting - check that name on the permission prompt later.",
             16, PageMuted);

        // The advertisement block. Generic and labelled as one, because the trap it teaches -
        // the biggest download button on the page is an advert - is the real lesson here.
        var ad = BorderBox(s, "AdBlock", 360, 660, 1200, 220, AdBg, AdBorder);
        Text(ad, "AdLabel", 14, 8, 300, 24, "Advertisement", 15, AdLabelText);
        Box(ad, "AdButton", 470, 68, 260, 64, AdButton);
        Text(ad, "AdButtonLabel", 470, 68, 260, 64, "DOWNLOAD", 24, Color.white,
             TextAlignmentOptions.Center, true);
        Text(ad, "AdSub", 300, 148, 600, 30, "Start your free download now", 16, AdLabelText,
             TextAlignmentOptions.Center);

        // Same arithmetic as the drawing above, so the hotspot cannot land anywhere else.
        r.siteDownload = Hot(s, 374, 446, 400, 40);
        r.siteAd = Hot(ad, 470, 68, 260, 64);
    }

    private static void Row(Frame s, int n, float y, string file, string ver, string date, string size)
    {
        Text(s, "Row" + n + "File", 374, y, 420, 40, file, 19, LinkBlue);
        Text(s, "Row" + n + "Version", 820, y, 180, 40, ver, 17, PageText);
        Text(s, "Row" + n + "Date", 1030, y, 200, 40, date, 17, PageText);
        Text(s, "Row" + n + "Size", 1270, y, 180, 40, size, 17, PageText);
        Box(s, "Row" + n + "Rule", 360, y + 44, 1200, 1, Rule);
    }

    private static void BuildDownloading(Transform root, ref Rects r)
    {
        var s = Screen(root, Downloading);

        Box(s, "Page", 0, 100, DW, DH - 100, PageBg);
        BrowserChrome(s, "rufus.ie");
        Text(s, "FadedTitle", 360, 150, 1200, 60, "Rufus", 46, Faded, TextAlignmentOptions.Left, true);

        var b = BorderBox(s, "DownloadBubble", 1420, 108, 460, 180, PageBg, Rule);
        Text(b, "BubbleTitle", 16, 10, 300, 28, "Downloads", 18, PageText,
             TextAlignmentOptions.Left, true);
        Text(b, "FileName", 16, 50, 428, 28, "rufus-4.4.exe", 17, PageText);
        Text(b, "Status", 16, 124, 428, 24, "Downloading...", 14, PageMuted);

        r.downloadingBar = Hot(b, 30, 92, 400, 24);
    }

    private static void BuildDownloadsBar(Transform root, ref Rects r)
    {
        var s = Screen(root, DownloadsBar);

        Box(s, "Page", 0, 100, DW, DH - 100, PageBg);
        BrowserChrome(s, "rufus.ie");
        Text(s, "FadedTitle", 360, 150, 1200, 60, "Rufus", 46, Faded, TextAlignmentOptions.Left, true);

        var b = BorderBox(s, "DownloadBubble", 1420, 108, 460, 180, PageBg, Rule);
        Text(b, "BubbleTitle", 16, 10, 300, 28, "Downloads", 18, PageText,
             TextAlignmentOptions.Left, true);

        Box(b, "FileRow", 12, 44, 436, 60, Hex(0xF1F3F4));
        Text(b, "FileName", 26, 50, 400, 26, "rufus-4.4.exe", 18, PageText);
        Text(b, "FileMeta", 26, 78, 400, 24, "1.4 MB - Done", 14, PageMuted);
        Text(b, "OpenHint", 16, 120, 428, 26, "Click the file to run it", 14, LinkBlue);

        r.downloadsFile = Hot(b, 12, 44, 436, 60);
    }

    private static void BuildUac(Transform root, ref Rects r)
    {
        var s = Screen(root, Uac);

        // The dimmed desktop is part of how you recognise a UAC prompt: Windows switches to the
        // secure desktop and nothing else on screen responds.
        Box(s, "Dim", 0, 0, DW, DH, Dim);

        var d = FrameBox(s, "Dialog", 660, 300, 600, 460, UacBg);
        Box(d, "HeaderStrip", 0, 0, 600, 6, UacHeader);

        Text(d, "Title", 32, 36, 536, 36, "User Account Control", 22, UacText,
             TextAlignmentOptions.Left, true);
        Text(d, "Question", 32, 86, 536, 84,
             "Do you want to allow this app to make changes to your device?", 20, UacText,
             TextAlignmentOptions.TopLeft);

        Text(d, "AppName", 32, 196, 536, 30, "Rufus 4.4.2103", 18, UacText,
             TextAlignmentOptions.Left, true);
        Text(d, "Publisher", 32, 232, 536, 28, "Verified publisher: Akeo Consulting", 16, UacMuted);
        Text(d, "Origin", 32, 264, 536, 28, "File origin: Downloaded from the Internet", 16, UacMuted);
        Text(d, "More", 32, 304, 300, 28, "Show more details", 16, UacLink);

        Box(d, "YesBtn", 140, 360, 200, 48, ButtonFace);
        Text(d, "YesLabel", 140, 360, 200, 48, "Yes", 19, PageText, TextAlignmentOptions.Center);

        Box(d, "NoBtn", 370, 360, 200, 48, ButtonFace);
        Text(d, "NoLabel", 370, 360, 200, 48, "No", 19, PageText, TextAlignmentOptions.Center);

        r.uacYes = Hot(d, 140, 360, 200, 48);
        r.uacNo = Hot(d, 370, 360, 200, 48);
    }

    private static void BuildUpdatePolicy(Transform root, ref Rects r)
    {
        var s = Screen(root, UpdatePolicy);
        Box(s, "Dim", 0, 0, DW, DH, Dim);

        var d = BorderBox(s, "Dialog", 720, 390, 480, 270, DialogBg, DialogBorder);

        Box(d, "TitleBar", 0, 0, 480, 34, Color.white);
        Text(d, "Title", 14, 0, 400, 34, "Rufus update policy", 16, PageText);

        Text(d, "Body", 24, 56, 432, 110,
             "Do you want to allow Rufus to check for application updates?\n\n" +
             "Checking for updates keeps the tool compatible with newer disc images.",
             17, PageText, TextAlignmentOptions.TopLeft);

        Box(d, "YesBtn", 140, 196, 150, 42, ButtonFace);
        Text(d, "YesLabel", 140, 196, 150, 42, "Yes", 17, PageText, TextAlignmentOptions.Center);

        Box(d, "NoBtn", 310, 196, 150, 42, ButtonFace);
        Text(d, "NoLabel", 310, 196, 150, 42, "No", 17, PageText, TextAlignmentOptions.Center);

        r.updateYes = Hot(d, 140, 196, 150, 42);
    }

    private static void BuildSplash(Transform root, ref Rects r)
    {
        var s = Screen(root, Splash);
        Box(s, "Backdrop", 0, 0, DW, DH, Hex(0x1B1F24));

        var w = BorderBox(s, "Window", 710, 400, 500, 280, DialogBg, DialogBorder);
        Box(w, "TitleBar", 0, 0, 500, 34, Color.white);
        Text(w, "Title", 14, 0, 400, 34, "Rufus 4.4.2103", 16, PageText);
        Text(w, "Status", 0, 100, 500, 40, "Starting Rufus...", 20, PageText,
             TextAlignmentOptions.Center);

        r.splashBar = Hot(w, 50, 200, 400, 24);
    }

    // ==================================================================
    // Shared chrome
    // ==================================================================

    private static void BrowserChrome(Frame s, string url)
    {
        Box(s, "TabStrip", 0, 0, DW, 44, ChromeTabStrip);
        Box(s, "Tab", 12, 8, 340, 36, ChromeToolbar);
        Text(s, "TabTitle", 32, 8, 300, 36,
             string.IsNullOrEmpty(url) ? "New Tab" : url, 15, PageText);

        Box(s, "Toolbar", 0, 44, DW, 56, ChromeToolbar);

        Text(s, "NavBack", 40, 52, 40, 40, "<", 22, ChromeGlyph, TextAlignmentOptions.Center);
        Text(s, "NavFwd", 84, 52, 40, 40, ">", 22, ChromeGlyph, TextAlignmentOptions.Center);

        Box(s, "Omnibox", 160, 52, 1480, 40, ChromeOmnibox);
        if (!string.IsNullOrEmpty(url))
            Text(s, "OmniboxUrl", 182, 52, 1400, 40, url, 18, PageText);

        Box(s, "GoBtn", 1660, 52, 120, 40, GoBlue);
        Text(s, "GoLabel", 1660, 52, 120, 40, "Go", 18, Color.white, TextAlignmentOptions.Center);

        Box(s, "ToolbarRule", 0, 99, DW, 1, Rule);
    }

    // ==================================================================
    // Primitives
    // ==================================================================

    private static Frame Screen(Transform root, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(root, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        go.SetActive(false);           // the view turns exactly one on

        return new Frame { root = rt, ox = 0f, oy = 0f };
    }

    /// <summary>Design pixels to normalised panel space.</summary>
    public static Rect N(float x, float y, float w, float h)
    {
        return new Rect(x / DW, y / DH, w / DW, h / DH);
    }

    /// <summary>A hotspot rect, from the same absolute coordinates the drawing used.</summary>
    private static Rect Hot(Frame f, float x, float y, float w, float h)
    {
        return N(f.ox + x, f.oy + y, w, h);
    }

    private static RectTransform Place(Frame f, string name, float x, float y, float w, float h)
    {
        float ax = f.ox + x;
        float ay = f.oy + y;

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(f.root, false);          // always the screen root, never a nested box

        rt.anchorMin = new Vector2(ax / DW, 1f - (ay + h) / DH);
        rt.anchorMax = new Vector2((ax + w) / DW, 1f - ay / DH);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    private static Image Box(Frame f, string name, float x, float y, float w, float h, Color c)
    {
        var rt = Place(f, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;      // drawn scenery must never intercept a hotspot click
        return img;
    }

    /// <summary>A filled box that also returns a Frame, so its contents can be written in
    /// coordinates local to it.</summary>
    private static Frame FrameBox(Frame f, string name, float x, float y, float w, float h, Color c)
    {
        Box(f, name, x, y, w, h, c);
        return f.At(x, y);
    }

    private static Frame BorderBox(Frame f, string name, float x, float y, float w, float h,
                                   Color fill, Color border)
    {
        Box(f, name, x, y, w, h, border);
        Box(f, name + "_Fill", x + 1f, y + 1f, w - 2f, h - 2f, fill);
        return f.At(x, y);
    }

    private static TextMeshProUGUI Text(Frame f, string name, float x, float y, float w, float h,
                                        string text, float size, Color color,
                                        TextAlignmentOptions align = TextAlignmentOptions.Left,
                                        bool bold = false)
    {
        var rt = Place(f, name, x, y, w, h);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();

        // Resolve a font asset explicitly. A TMP text added from an Editor script has not run
        // its own Awake, so anything that parses the text before then throws.
        if (Font != null) label.font = Font;
        else if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.text = text;
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        if (bold) label.fontStyle = FontStyles.Bold;

        return label;
    }

    private static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
#endif