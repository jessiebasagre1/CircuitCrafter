using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using CircuitCrafter.Repair;

namespace CircuitCrafter.COC4.EditorTools
{
    /// <summary>
    /// Builds the five phases of the repair-site monitor and wires every field on PCBootScreenView.
    ///
    /// RUN IT WITH: CircuitCrafter > Build PC Boot Screen
    ///
    /// Select the existing boot-screen panel first and the phases are built INSIDE it, leaving the
    /// POWER button, the Step back button and the PCPowerStation on it exactly where they are - this
    /// is an upgrade to a screen that already works, not a replacement for it. With nothing selected
    /// it builds a whole new panel on the canvas.
    ///
    /// It does not use the menu palette. A POST screen is white-on-black monospace and a Windows
    /// boot screen is a logo on a plain dark field, because that is what those screens look like -
    /// styling them to match the main menu would make the simulated machine look like part of the
    /// game's UI, which is the one thing it must not look like.
    /// </summary>
    public static class PCBootScreenBuilder
    {
        private static readonly Color ScreenBlack = new Color32(0x05, 0x06, 0x08, 0xFF);
        private static readonly Color PostInk = new Color32(0xDF, 0xE6, 0xEF, 0xFF);
        private static readonly Color ErrorInk = new Color32(0xFF, 0xB3, 0x3C, 0xFF);
        private static readonly Color WindowsBlack = new Color32(0x00, 0x00, 0x00, 0xFF);
        private static readonly Color WindowsInk = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
        private static readonly Color DesktopField = new Color32(0x10, 0x50, 0x8A, 0xFF);
        private static readonly Color TaskbarField = new Color32(0x1F, 0x1F, 0x1F, 0xF2);
        private static readonly Color Dim = new Color32(0x7C, 0x8A, 0xA0, 0xFF);

        private const float PostSize = 22f;
        private const float TitleSize = 34f;
        private const float BodySize = 24f;

        // ==================================================================

        [MenuItem("CircuitCrafter/Build PC Boot Screen", false, 44)]
        public static void Build()
        {
            RectTransform host = ResolveHost();
            if (host == null) return;

            RectTransform screen = NewRect("BootScreen", host);
            Stretch(screen);

            // FIRST sibling, not last, and this is not cosmetic.
            //
            // uGUI draws siblings in hierarchy order, so a new child goes ON TOP of everything
            // already in the panel - including the POWER button the learner has to press and the
            // Step back button they leave by. The screen-off phase is a full-rect opaque black
            // Image, so parenting this last hides both controls behind a black rectangle. Nothing
            // errors, nothing logs, and the learner walks up to the monitor, presses E, and sees a
            // dead screen with no way to do anything about it.
            //
            // The phases belong BEHIND the controls for the same reason a real monitor's picture is
            // behind the buttons on its bezel.
            screen.SetAsFirstSibling();

            GameObject offPanel = BuildOffPanel(screen, out Graphic led);
            GameObject splash = BuildSplash(screen, out Image logo, out TMP_Text hint);
            GameObject post = BuildPost(screen, out TMP_Text postText, out TMP_Text memoryText);
            GameObject error = BuildError(screen, out TMP_Text errTitle, out TMP_Text errBody, out TMP_Text beep);
            GameObject os = BuildOs(screen, out Image osLogo, out TMP_Text osText,
                                    out RectTransform spinner, out Slider progress);
            GameObject desktop = BuildDesktop(screen, out TMP_Text result);

            var audio = screen.gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;

            var view = screen.gameObject.AddComponent<PCBootScreenView>();
            var so = new SerializedObject(view);
            SetRef(so, "screenOffPanel", offPanel);
            SetRef(so, "powerLed", led);
            SetRef(so, "splashPanel", splash);
            SetRef(so, "vendorLogo", logo);
            SetRef(so, "splashHint", hint);
            SetRef(so, "postPanel", post);
            SetRef(so, "postText", postText);
            SetRef(so, "memoryText", memoryText);
            SetRef(so, "errorPanel", error);
            SetRef(so, "errorTitle", errTitle);
            SetRef(so, "errorBody", errBody);
            SetRef(so, "beepText", beep);
            SetRef(so, "osPanel", os);
            SetRef(so, "osLogo", osLogo);
            SetRef(so, "osText", osText);
            SetRef(so, "spinner", spinner);
            SetRef(so, "progressBar", progress);
            SetRef(so, "desktopPanel", desktop);
            SetRef(so, "resultText", result);
            SetRef(so, "audioSource", audio);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Hooked up to the station automatically when there is one on the host, because a boot
            // screen nobody assigned is a boot screen that never runs - the station silently falls
            // back to its old text mode and it looks like the build did nothing.
            PCPowerStation station = host.GetComponentInParent<PCPowerStation>();
            if (station == null) station = host.GetComponentInChildren<PCPowerStation>(true);

            if (station != null)
            {
                var stationSo = new SerializedObject(station);
                SetRef(stationSo, "bootScreen", view);
                stationSo.ApplyModifiedPropertiesWithoutUndo();
            }

            Undo.RegisterCreatedObjectUndo(screen.gameObject, "Build PC Boot Screen");
            Selection.activeGameObject = screen.gameObject;
            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);

            Debug.Log(
                "[PCBootScreenBuilder] Built 'BootScreen' under '" + host.name + "'.\n\n" +
                (station != null
                    ? "Wired into PCPowerStation on '" + station.name + "'. "
                    : "NO PCPowerStation was found near this object - drop the BootScreen into that " +
                      "component's Boot Screen field yourself, or the station keeps using its old " +
                      "text mode. ") +
                "\n\nNEXT:\n" +
                "  1. Give Post/PostText and Post/MemoryText a MONOSPACE font. The POST output is " +
                "laid out in dot-leader columns and a proportional font turns it into mush.\n" +
                "  2. Drop your art on Splash/VendorLogo, Os/OsLogo, Desktop/Wallpaper and " +
                "Desktop/Taskbar/StartButton. All four work as plain colour until you do - the " +
                "Windows logo on Os/OsLogo is the one that matters most.\n" +
                "  3. TO MATCH COC1 EXACTLY: make your COC1 install panel a prefab, drop it in here, " +
                "and point Os Panel / Os Text / Progress Bar / Desktop Panel at ITS objects instead " +
                "of the ones built here, then delete this Os and Desktop. Those four fields line up " +
                "one-for-one with OSInstallView's Install Panel, Stage Label, Progress Bar and " +
                "Desktop Panel.\n" +
                "  4. Assign Post Ok Beep and Post Error Beep on the view for sound, and Beep Code " +
                "on any TicketFault that stops the boot.\n\n" +
                "The station's old Screen Text is left alone. Clear the Boot Screen field at any " +
                "time and it goes straight back to the way it worked before.",
                screen.gameObject);
        }

        // ==================================================================

        private static RectTransform ResolveHost()
        {
            if (Selection.activeGameObject != null)
            {
                var rect = Selection.activeGameObject.GetComponent<RectTransform>();
                if (rect != null) return rect;
            }

            Canvas[] all = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None);
            foreach (Canvas c in all)
            {
                if (c != null && c.rootCanvas == c && c.renderMode != RenderMode.WorldSpace)
                {
                    Debug.LogWarning("[PCBootScreenBuilder] Nothing was selected, so this was built " +
                                     "on canvas '" + c.name + "' at full screen. Select your monitor " +
                                     "panel and build again to put it inside that instead.", c);
                    return c.transform as RectTransform;
                }
            }

            Debug.LogWarning("[PCBootScreenBuilder] No canvas in this scene and nothing selected. " +
                             "Select the monitor panel you want the boot screen built inside.");
            return null;
        }

        // ==================================================================
        // The five phases
        // ==================================================================

        private static GameObject BuildOffPanel(RectTransform parent, out Graphic led)
        {
            RectTransform rect = NewRect("ScreenOff", parent);
            Stretch(rect);
            AddImage(rect, ScreenBlack);

            // A small lamp in the corner rather than anything clever. It has to be visible while the
            // screen itself shows nothing, which rules out putting it on any of the phase panels.
            RectTransform lamp = NewRect("PowerLed", rect);
            lamp.anchorMin = new Vector2(1f, 0f);
            lamp.anchorMax = new Vector2(1f, 0f);
            lamp.pivot = new Vector2(1f, 0f);
            lamp.anchoredPosition = new Vector2(-18f, 18f);
            lamp.sizeDelta = new Vector2(14f, 14f);
            led = AddImage(lamp, new Color32(0x30, 0x30, 0x30, 0xFF));

            return rect.gameObject;
        }

        private static GameObject BuildSplash(RectTransform parent, out Image logo, out TMP_Text hint)
        {
            RectTransform rect = NewRect("Splash", parent);
            Stretch(rect);
            AddImage(rect, ScreenBlack);

            RectTransform logoRect = NewRect("VendorLogo", rect);
            logoRect.anchorMin = new Vector2(0.5f, 0.5f);
            logoRect.anchorMax = new Vector2(0.5f, 0.5f);
            logoRect.pivot = new Vector2(0.5f, 0.5f);
            logoRect.anchoredPosition = new Vector2(0f, 40f);
            logoRect.sizeDelta = new Vector2(320f, 160f);
            logo = AddImage(logoRect, new Color(1f, 1f, 1f, 0.10f));
            logo.preserveAspect = true;

            hint = AddText(rect, "SplashHint", "DEL = SETUP      F12 = Boot Menu", PostSize, Dim);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.offsetMin = new Vector2(40f, 40f);
            hint.rectTransform.offsetMax = new Vector2(-40f, 80f);
            hint.alignment = TextAlignmentOptions.Center;

            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        private static GameObject BuildPost(RectTransform parent, out TMP_Text postText,
                                            out TMP_Text memoryText)
        {
            RectTransform rect = NewRect("Post", parent);
            Stretch(rect);
            AddImage(rect, ScreenBlack);

            postText = AddText(rect, "PostText", "", PostSize, PostInk);
            Stretch(postText.rectTransform, 36f);
            postText.alignment = TextAlignmentOptions.TopLeft;

            // Its own label, anchored under the text block, because a real POST counts memory IN
            // PLACE rather than printing a new line per megabyte.
            memoryText = AddText(rect, "MemoryText", "", PostSize, PostInk);
            memoryText.rectTransform.anchorMin = new Vector2(0f, 0f);
            memoryText.rectTransform.anchorMax = new Vector2(1f, 0f);
            memoryText.rectTransform.pivot = new Vector2(0.5f, 0f);
            memoryText.rectTransform.offsetMin = new Vector2(36f, 36f);
            memoryText.rectTransform.offsetMax = new Vector2(-36f, 70f);
            memoryText.alignment = TextAlignmentOptions.BottomLeft;

            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        private static GameObject BuildError(RectTransform parent, out TMP_Text title,
                                             out TMP_Text body, out TMP_Text beep)
        {
            RectTransform rect = NewRect("PostError", parent);
            Stretch(rect);
            AddImage(rect, ScreenBlack);

            RectTransform column = NewRect("Column", rect);
            Stretch(column, 56f);

            VerticalLayoutGroup v = column.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 18f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.MiddleLeft;

            title = AddText(column, "ErrorTitle", "The computer did not start.", TitleSize, ErrorInk,
                            FontStyles.Bold);
            Fixed(title.rectTransform, TitleSize * 1.6f);

            body = AddText(column, "ErrorBody", "", BodySize, PostInk);
            body.enableWordWrapping = true;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.overflowMode = TextOverflowModes.Overflow;
            Fixed(body.rectTransform, BodySize * 6f);

            beep = AddText(column, "BeepText", "", BodySize, ErrorInk, FontStyles.Bold);

            TMP_Text advice = AddText(column, "Advice",
                                      "Switch it off at the wall and open the system unit to look inside.",
                                      BodySize, Dim);
            advice.enableWordWrapping = true;
            Fixed(advice.rectTransform, BodySize * 2.6f);

            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        /// <summary>
        /// The Windows boot screen: black field, logo, and a ring of dots chasing round under it.
        ///
        /// WHY BLACK AND NOT THE DARK BLUE IT WAS
        /// A real Windows boot screen is pure black - #000000, not a dark navy - and the eye picks
        /// that up immediately even though nobody could tell you the hex. The near-black used before
        /// read as "a game menu with a dark theme"; this reads as a computer.
        ///
        /// THE DOTS ARE SIZED AND FADED AROUND THE RING, not drawn as one spinning square. Windows'
        /// boot spinner is a ring of small dots of graded opacity turning together, and that graded
        /// trail is the thing that makes it recognisable - a single rotating shape reads as a
        /// loading widget from any piece of software.
        /// </summary>
        private static GameObject BuildOs(RectTransform parent, out Image logo, out TMP_Text text,
                                          out RectTransform spinner, out Slider progress)
        {
            RectTransform rect = NewRect("Os", parent);
            Stretch(rect);
            AddImage(rect, WindowsBlack);

            RectTransform logoRect = NewRect("OsLogo", rect);
            logoRect.anchorMin = new Vector2(0.5f, 0.5f);
            logoRect.anchorMax = new Vector2(0.5f, 0.5f);
            logoRect.pivot = new Vector2(0.5f, 0.5f);
            logoRect.anchoredPosition = new Vector2(0f, 96f);
            logoRect.sizeDelta = new Vector2(150f, 150f);
            logo = AddImage(logoRect, new Color(1f, 1f, 1f, 0.92f));
            logo.preserveAspect = true;

            spinner = NewRect("Spinner", rect);
            spinner.anchorMin = new Vector2(0.5f, 0.5f);
            spinner.anchorMax = new Vector2(0.5f, 0.5f);
            spinner.pivot = new Vector2(0.5f, 0.5f);
            spinner.anchoredPosition = new Vector2(0f, -40f);
            spinner.sizeDelta = new Vector2(56f, 56f);

            // Six dots, each a little smaller and fainter than the one before it, so the ring has a
            // head and a tail. That trail is what your eye reads as Windows rather than as a generic
            // spinner - and it costs six Images.
            const int dots = 6;
            for (int i = 0; i < dots; i++)
            {
                RectTransform dot = NewRect("Dot" + i, spinner);
                dot.anchorMin = new Vector2(0.5f, 0.5f);
                dot.anchorMax = new Vector2(0.5f, 0.5f);
                dot.pivot = new Vector2(0.5f, 0.5f);

                float t = i / (float)dots;
                float angle = t * Mathf.PI * 2f;
                dot.anchoredPosition = new Vector2(Mathf.Sin(angle) * 24f, Mathf.Cos(angle) * 24f);

                float size = Mathf.Lerp(7f, 3.5f, t);
                dot.sizeDelta = new Vector2(size, size);
                AddImage(dot, new Color(1f, 1f, 1f, Mathf.Lerp(0.95f, 0.18f, t)));
            }

            text = AddText(rect, "OsText", "Starting Windows", BodySize, WindowsInk);
            text.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            text.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.rectTransform.anchoredPosition = new Vector2(0f, -104f);
            text.rectTransform.sizeDelta = new Vector2(0f, 40f);
            text.alignment = TextAlignmentOptions.Center;

            // Same three parts OSInstallView's bar has - background, fill area, fill - so the
            // component this drives is interchangeable with the one on the COC1 install screen.
            RectTransform barRect = NewRect("ProgressBar", rect);
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -152f);
            barRect.sizeDelta = new Vector2(360f, 6f);
            AddImage(barRect, new Color(1f, 1f, 1f, 0.14f));

            RectTransform fillArea = NewRect("Fill Area", barRect);
            Stretch(fillArea);

            RectTransform fill = NewRect("Fill", fillArea);
            Stretch(fill);
            AddImage(fill, WindowsInk);

            progress = barRect.gameObject.AddComponent<Slider>();
            progress.fillRect = fill;
            progress.targetGraphic = null;
            progress.handleRect = null;
            progress.transition = Selectable.Transition.None;
            progress.interactable = false;
            progress.minValue = 0f;
            progress.maxValue = 1f;
            progress.value = 0f;

            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        /// <summary>
        /// The desktop: wallpaper, and a taskbar along the bottom with a Start button, a search box
        /// and a clock.
        ///
        /// THE TASKBAR IS THE WHOLE POINT. A plain blue rectangle is not a desktop - it is a blue
        /// rectangle, and a learner looking at one has to be TOLD the machine booted. A strip along
        /// the bottom with a Start button on the left and a time on the right is read as Windows
        /// instantly and without a caption, by anyone who has ever used a PC. It is four Images and
        /// two labels, and it does more for "the machine works" than any amount of text.
        /// </summary>
        private static GameObject BuildDesktop(RectTransform parent, out TMP_Text result)
        {
            RectTransform rect = NewRect("Desktop", parent);
            Stretch(rect);

            RectTransform wallpaper = NewRect("Wallpaper", rect);
            Stretch(wallpaper);
            AddImage(wallpaper, DesktopField);          // <- desktop wallpaper sprite goes here

            // ---- taskbar ------------------------------------------------
            RectTransform bar = NewRect("Taskbar", rect);
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = new Vector2(0f, 44f);
            AddImage(bar, TaskbarField);

            RectTransform start = NewRect("StartButton", bar);
            start.anchorMin = new Vector2(0f, 0.5f);
            start.anchorMax = new Vector2(0f, 0.5f);
            start.pivot = new Vector2(0f, 0.5f);
            start.anchoredPosition = new Vector2(10f, 0f);
            start.sizeDelta = new Vector2(24f, 24f);
            AddImage(start, new Color(1f, 1f, 1f, 0.85f));   // <- Start glyph sprite goes here

            RectTransform search = NewRect("SearchBox", bar);
            search.anchorMin = new Vector2(0f, 0.5f);
            search.anchorMax = new Vector2(0f, 0.5f);
            search.pivot = new Vector2(0f, 0.5f);
            search.anchoredPosition = new Vector2(46f, 0f);
            search.sizeDelta = new Vector2(200f, 28f);
            AddImage(search, new Color(1f, 1f, 1f, 0.10f));

            TMP_Text searchLabel = AddText(search, "SearchLabel", "Type here to search", 15f,
                                           new Color(1f, 1f, 1f, 0.55f));
            Stretch(searchLabel.rectTransform, 8f);
            searchLabel.alignment = TextAlignmentOptions.MidlineLeft;

            // A fixed time, not DateTime.Now. A clock that reads the real time is the one detail
            // that would pull the learner out of the simulation and back into their own afternoon.
            TMP_Text clock = AddText(bar, "Clock", "9:41 AM\n21/08/2026", 14f,
                                     new Color(1f, 1f, 1f, 0.85f));
            clock.rectTransform.anchorMin = new Vector2(1f, 0f);
            clock.rectTransform.anchorMax = new Vector2(1f, 1f);
            clock.rectTransform.pivot = new Vector2(1f, 0.5f);
            clock.rectTransform.offsetMin = new Vector2(-120f, 0f);
            clock.rectTransform.offsetMax = new Vector2(-12f, 0f);
            clock.alignment = TextAlignmentOptions.Center;
            clock.enableWordWrapping = true;

            // ---- the verdict --------------------------------------------
            // A plate over the wallpaper rather than plain text on it, so the verdict stays readable
            // whatever screenshot you drop in behind it.
            RectTransform plate = NewRect("ResultPlate", rect);
            plate.anchorMin = new Vector2(0.5f, 0.5f);
            plate.anchorMax = new Vector2(0.5f, 0.5f);
            plate.pivot = new Vector2(0.5f, 0.5f);
            plate.sizeDelta = new Vector2(660f, 260f);
            plate.anchoredPosition = Vector2.zero;
            AddImage(plate, new Color(0.02f, 0.03f, 0.06f, 0.88f));

            result = AddText(plate, "ResultText", "", BodySize, WindowsInk);
            Stretch(result.rectTransform, 28f);
            result.alignment = TextAlignmentOptions.Center;
            result.enableWordWrapping = true;
            result.overflowMode = TextOverflowModes.Overflow;

            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        // ==================================================================
        // Small builders
        // ==================================================================

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;                // nothing on the monitor is clickable
            return image;
        }

        private static TMP_Text AddText(RectTransform parent, string name, string value, float size,
                                        Color color, FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = NewRect(name, parent);

            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;

            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            return text;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Fixed(RectTransform rect, float height)
        {
            LayoutElement le = rect.GetComponent<LayoutElement>();
            if (le == null) le = rect.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.objectReferenceValue = value;
        }
    }
}