using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The monitor at a repair site, drawn as a machine actually starting up rather than as a block
    /// of text: dead screen, vendor splash, POST with the memory counting up, then either beep codes
    /// or Windows loading, then the desktop.
    ///
    /// ATTACH THIS TO: the boot screen panel, and drop it into PCPowerStation's Boot Screen field.
    /// Menu: CircuitCrafter > Build PC Boot Screen builds and wires the whole thing.
    ///
    /// IT DECIDES NOTHING. Every judgement - which faults block the boot, what counts as a test,
    /// what the learner is allowed to do next - stays in PCPowerStation, which was already right
    /// about all of it. This only draws what it is told. That split is why the change is safe: the
    /// logic that gates a ticket never moved.
    ///
    /// EVERY FIELD IS OPTIONAL, and that is deliberate rather than defensive. Assign nothing and the
    /// station falls back to printing to its old Screen Text exactly as before. Assign the splash
    /// and nothing else and you get a splash and then the old text. A site can be upgraded one panel
    /// at a time, and a site you never touch keeps working.
    ///
    /// WHY THE PHASES ARE SEPARATE PANELS AND NOT ONE RELABELLED ONE
    /// A real POST screen and a real Windows boot screen look nothing alike - white-on-black
    /// monospace against a centred logo on a plain field - and that difference is information. A
    /// learner who sees the machine get past POST and hang on the Windows logo has learned something
    /// different from one whose screen never lit up at all. Relabelling one panel throws that away.
    /// </summary>
    public class PCBootScreenView : MonoBehaviour
    {
        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("Screen off")]
        [Tooltip("Black Image covering the screen while the machine is off, so a dead monitor looks " +
                 "dead rather than blank. The same trick OSInstallView uses with its reboot overlay.")]
        [SerializeField] private GameObject screenOffPanel;

        [Tooltip("Optional. The power LED on the case. Lit from the moment POWER is pressed - " +
                 "including on a machine that never displays anything, which is exactly the symptom " +
                 "'the fans spin and the light is on but the screen stays black' describes.")]
        [SerializeField] private Graphic powerLed;

        [SerializeField] private Color powerLedOff = new Color32(0x30, 0x30, 0x30, 0xFF);
        [SerializeField] private Color powerLedOn = new Color32(0x5A, 0xE0, 0xA8, 0xFF);

        [Header("Vendor splash")]
        [SerializeField] private GameObject splashPanel;

        [Tooltip("Your BIOS/vendor logo. Left empty the panel still works - it is the text under it " +
                 "that carries the meaning.")]
        [SerializeField] private Image vendorLogo;

        [SerializeField] private TMP_Text splashHint;

        [Header("POST")]
        [SerializeField] private GameObject postPanel;

        [Tooltip("Where the POST lines are printed. USE A MONOSPACE FONT - the output is laid out " +
                 "in dot-leader columns and a proportional font turns it into ragged mush.")]
        [SerializeField] private TMP_Text postText;

        [Tooltip("Optional. The memory figure counting up in its own label, the way a real POST " +
                 "screen counts it in place rather than printing a new line per megabyte.")]
        [SerializeField] private TMP_Text memoryText;

        [Header("POST failure")]
        [Tooltip("Shown instead of the OS phases when something stops the machine starting. Its own " +
                 "panel so it can be amber-on-black like a real error screen.")]
        [SerializeField] private GameObject errorPanel;

        [SerializeField] private TMP_Text errorTitle;
        [SerializeField] private TMP_Text errorBody;

        [Tooltip("Optional. The beep pattern, e.g. \"1 long, 3 short\". Reading beep codes is a real " +
                 "CSS NC II diagnostic skill and costs nothing to show here.")]
        [SerializeField] private TMP_Text beepText;

        [Header("OS loading")]
        [SerializeField] private GameObject osPanel;
        [SerializeField] private Image osLogo;
        [SerializeField] private TMP_Text osText;

        [Tooltip("Optional. Spun while the OS panel is up. Any RectTransform - a ring of dots, an " +
                 "arc, a plain square. It is the MOTION that says 'working', not the shape.")]
        [SerializeField] private RectTransform spinner;

        [SerializeField] private float spinnerDegreesPerSecond = 220f;

        [Tooltip("Optional 0-1 bar filled while the OS loads.\n\n" +
                 "THIS IS THE FIELD THAT LETS YOU REUSE COC1's INSTALL SCREEN. Os Panel, Os Text and " +
                 "this line up one-for-one with OSInstallView's Install Panel, Stage Label and " +
                 "Progress Bar, so the very same panel - your art, your layout - can be dropped into " +
                 "a repair site and driven by this instead. Then the machine the learner repairs in " +
                 "COC4 boots into the identical Windows they installed in COC1, because it IS the " +
                 "identical screen.")]
        [SerializeField] private Slider progressBar;

        [Header("Result")]
        [Tooltip("The desktop, shown when the machine starts normally. A wallpaper Image is enough.")]
        [SerializeField] private GameObject desktopPanel;

        [Tooltip("Where the verdict is written - what passed, and whether the ticket can be closed. " +
                 "Shown over the desktop on a pass, and under the symptom on a boot that came up but " +
                 "is still not right.")]
        [SerializeField] private TMP_Text resultText;

        [Header("Sound (optional)")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("The single short beep a healthy machine makes when POST passes.")]
        [SerializeField] private AudioClip postOkBeep;

        [Tooltip("The beep used for an error pattern. Played once per beep in the pattern.")]
        [SerializeField] private AudioClip postErrorBeep;

        [SerializeField] private float errorBeepGap = 0.22f;

        // ==================================================================

        private Coroutine beeping;

        private void Awake()
        {
            // Everything off, including the phases, so a screen left half-open in the Scene view
            // does not flash on for a frame when the site loads.
            ShowOnly(null);
            ShowOff();
        }

        private void Update()
        {
            if (spinner == null || osPanel == null || !osPanel.activeSelf) return;

            // Unscaled, because a dialog can set timeScale to 0 while this is on screen and a
            // spinner frozen mid-turn reads as the machine having hung rather than as a paused game.
            spinner.Rotate(0f, 0f, -spinnerDegreesPerSecond * Time.unscaledDeltaTime);
        }

        // ==================================================================
        // Phases - all called by PCPowerStation
        // ==================================================================

        /// <summary>The machine is off. Black screen, dark LED.</summary>
        public void ShowOff()
        {
            StopBeeping();
            ShowOnly(null);
            if (screenOffPanel != null) screenOffPanel.SetActive(true);
            SetLed(false);
        }

        /// <summary>POWER has been pressed. The LED lights BEFORE anything appears on screen,
        /// because on a machine that never displays anything that light is the only evidence the
        /// press did something - and telling those two states apart is the first thing a technician
        /// does.</summary>
        public void ShowPowerOn()
        {
            SetLed(true);
            if (screenOffPanel != null) screenOffPanel.SetActive(true);
        }

        public void ShowSplash(string hint)
        {
            if (screenOffPanel != null) screenOffPanel.SetActive(false);
            ShowOnly(splashPanel);
            if (splashHint != null) splashHint.text = hint;
        }

        public void ShowPost()
        {
            if (screenOffPanel != null) screenOffPanel.SetActive(false);
            ShowOnly(postPanel);
            if (postText != null) postText.text = "";
            if (memoryText != null) memoryText.text = "";
        }

        public void SetPostText(string text)
        {
            if (postText != null) postText.text = text;
        }

        /// <summary>The memory figure, counted in place. Shown in MB because that is what a POST
        /// screen shows.</summary>
        public void SetMemoryCount(int countedMb, int totalMb)
        {
            if (memoryText == null) return;
            memoryText.text = countedMb >= totalMb
                ? $"Memory Test : {totalMb}MB  OK"
                : $"Memory Test : {countedMb}MB";
        }

        public void PlayPostOkBeep()
        {
            if (audioSource != null && postOkBeep != null) audioSource.PlayOneShot(postOkBeep);
        }

        /// <summary>The machine did not start. Symptom only - never the fault's name.</summary>
        public void ShowPostFailure(string symptom, string beepCode)
        {
            if (screenOffPanel != null) screenOffPanel.SetActive(false);
            ShowOnly(errorPanel);

            if (errorTitle != null) errorTitle.text = "The computer did not start.";
            if (errorBody != null) errorBody.text = symptom;

            bool hasCode = !string.IsNullOrWhiteSpace(beepCode);
            if (beepText != null)
            {
                beepText.gameObject.SetActive(hasCode);
                if (hasCode) beepText.text = "Beep code:  " + beepCode;
            }

            StopBeeping();
            if (hasCode) beeping = StartCoroutine(BeepPattern(beepCode));
        }

        /// <summary>POST passed, the OS is loading.</summary>
        public void ShowOsLoading(string message)
        {
            if (screenOffPanel != null) screenOffPanel.SetActive(false);
            ShowOnly(osPanel);
            if (osText != null) osText.text = message;

            if (progressBar != null)
            {
                progressBar.gameObject.SetActive(true);
                progressBar.minValue = 0f;
                progressBar.maxValue = 1f;
                progressBar.value = 0f;
            }
        }

        /// <summary>0-1 while the OS loads. Driven exactly the way OSInstallManager drives
        /// OSInstallView's bar, so a COC1 install panel dropped in here behaves as it always did.</summary>
        public void SetProgress(float t)
        {
            if (progressBar != null) progressBar.value = Mathf.Clamp01(t);
        }

        /// <summary>Started normally. The desktop, with the verdict over it.</summary>
        public void ShowDesktop(string result)
        {
            StopBeeping();
            if (screenOffPanel != null) screenOffPanel.SetActive(false);
            ShowOnly(desktopPanel);
            SetResult(result);
        }

        /// <summary>Windows came up but the machine is still not right - a network fault, say. The
        /// OS panel stays, because that is what the learner would be looking at; the verdict is
        /// written over it.</summary>
        public void ShowBootedWithFaults(string result)
        {
            StopBeeping();
            if (screenOffPanel != null) screenOffPanel.SetActive(false);
            ShowOnly(desktopPanel != null ? desktopPanel : osPanel);
            SetResult(result);
        }

        // ==================================================================
        // Small helpers
        // ==================================================================

        private void SetResult(string result)
        {
            if (resultText == null) return;

            bool has = !string.IsNullOrWhiteSpace(result);
            resultText.gameObject.SetActive(has);
            if (has) resultText.text = result;
        }

        /// <summary>One phase visible at a time. Passing null switches them all off.</summary>
        private void ShowOnly(GameObject wanted)
        {
            Set(splashPanel, wanted);
            Set(postPanel, wanted);
            Set(errorPanel, wanted);
            Set(osPanel, wanted);
            Set(desktopPanel, wanted);

            // The bar belongs to the OS phase. Left on over the desktop it reads as the machine
            // still doing something, which is the opposite of what a finished boot should say.
            if (progressBar != null && wanted != osPanel)
                progressBar.gameObject.SetActive(false);

            if (resultText != null && wanted != desktopPanel && wanted != osPanel)
                resultText.gameObject.SetActive(false);
        }

        private static void Set(GameObject panel, GameObject wanted)
        {
            if (panel != null && panel.activeSelf != (panel == wanted))
                panel.SetActive(panel == wanted);
        }

        private void SetLed(bool on)
        {
            if (powerLed != null) powerLed.color = on ? powerLedOn : powerLedOff;
        }

        /// <summary>
        /// Plays the written pattern - "1 long, 3 short" becomes one long beep then three short
        /// ones, on a loop, the way a machine that will not POST keeps beeping at you until it is
        /// switched off.
        ///
        /// Parsed from the same string the learner is reading rather than from a separate pair of
        /// numbers, so the sound and the label cannot disagree.
        /// </summary>
        private IEnumerator BeepPattern(string code)
        {
            if (audioSource == null || postErrorBeep == null) yield break;

            int longs = CountBefore(code, "long");
            int shorts = CountBefore(code, "short");
            if (longs + shorts == 0) longs = 1;

            while (true)
            {
                for (int i = 0; i < longs; i++)
                {
                    audioSource.PlayOneShot(postErrorBeep);
                    yield return new WaitForSecondsRealtime(errorBeepGap * 3f);
                }
                for (int i = 0; i < shorts; i++)
                {
                    audioSource.PlayOneShot(postErrorBeep);
                    yield return new WaitForSecondsRealtime(errorBeepGap);
                }
                yield return new WaitForSecondsRealtime(1.6f);
            }
        }

        private void StopBeeping()
        {
            if (beeping == null) return;
            StopCoroutine(beeping);
            beeping = null;
        }

        /// <summary>The number written immediately before a word, so "1 long, 3 short" gives 1 and
        /// 3. A word with no number in front of it counts as one.</summary>
        private static int CountBefore(string text, string word)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int at = text.IndexOf(word, System.StringComparison.OrdinalIgnoreCase);
            if (at < 0) return 0;

            int i = at - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i])) i--;

            int end = i;
            while (i >= 0 && char.IsDigit(text[i])) i--;

            if (i == end) return 1;                       // no digits, e.g. "long beep"
            return int.TryParse(text.Substring(i + 1, end - i), out int n) ? Mathf.Max(1, n) : 1;
        }
    }
}