// UiClickSound.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.UI
{
    /// <summary>
    /// Gives every button in the scene a click sound, without touching a single one of them.
    ///
    /// WHY NOT WIRE THE SOUND INTO EACH BUTTON'S ON CLICK
    ///
    /// That is the obvious way and it goes wrong in three predictable places. A menu has a couple
    /// of dozen buttons across several panels, so it is two dozen chances to forget one - and the
    /// one that gets forgotten is always on a panel nobody opens during testing. Changing the
    /// sound later means visiting all of them again. And every new button somebody adds starts off
    /// silent, with nothing to say so.
    ///
    /// One component that finds the buttons instead has none of those problems: a button is either
    /// in the scene or it is not, and if it is, it clicks. Adding a button later needs no thought
    /// at all.
    ///
    /// IT KEEPS LOOKING
    ///
    /// Menus are full of panels that start switched off, and their buttons do not exist as far as
    /// a one-off search at startup is concerned. So this searches again every so often - a cheap
    /// thing to do for a screen that is doing nothing else - and buttons that appear later get
    /// picked up within a second of appearing. Ones already hooked are remembered and skipped, so
    /// nothing ends up clicking twice.
    ///
    /// VOLUME IS ALREADY HANDLED
    ///
    /// Two sliders reach this without anything being wired. SettingsApplier sets
    /// AudioListener.volume from Master, a global gain over everything; and the AudioCategory this
    /// adds to itself puts the click on the Effects bus, so it follows that slider too. The Clip
    /// Volume field below sits underneath both and exists only to balance the click against the
    /// music - set it once by ear and leave the sliders to the learner.
    ///
    /// SETUP
    ///   1. Put this on an always-active object in the menu scene - the same one holding your
    ///      other managers is ideal.
    ///   2. Drop a short click sound into Click Clip.
    ///   3. That is all. It says in the Console how many buttons it found.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class UiClickSound : MonoBehaviour
    {
        [Header("The sound")]
        [Tooltip("Played when any button in this scene is clicked or tapped. Keep it SHORT - under " +
                 "a fifth of a second. A menu is clicked quickly and repeatedly, and a click that " +
                 "outlasts the next click stacks into a mush.")]
        public AudioClip clickClip;

        [Range(0f, 1f)]
        [Tooltip("How loud, before the master volume slider is applied on top.")]
        public float volume = 0.6f;

        [Tooltip("Vary the pitch slightly on each click, up and down by this much.\n\n" +
                 "Small but worth having. A sound repeated at exactly the same pitch starts to " +
                 "sound mechanical after the fourth or fifth press, and a few percent of wobble is " +
                 "enough to stop that without the clicks sounding like different buttons.")]
        [Range(0f, 0.3f)]
        public float pitchVariation = 0.06f;

        [Tooltip("Left empty, an AudioSource is made on this object and set up correctly. Only " +
                 "assign one if you have a reason to.")]
        public AudioSource source;

        [Tooltip("Put the click on the Effects volume slider.\n\n" +
                 "Leave it ON. A click is feedback, not atmosphere, so it belongs with the tools " +
                 "and the cable tester rather than with the music. This adds the AudioCategory " +
                 "component for you - there is nothing to set up.")]
        public bool putOnEffectsBus = true;

        [Header("What counts as a button")]
        [Tooltip("Also click when a toggle is flipped. Leave ON - a tickbox in a settings panel " +
                 "that makes no sound while the buttons beside it do feels broken rather than " +
                 "quiet.")]
        public bool alsoCoverToggles = true;

        [Tooltip("Buttons that should stay silent, or want their own sound wired into their On " +
                 "Click instead. Usually empty.")]
        public Selectable[] dontCover = new Selectable[0];

        [Header("Keeping up with the scene")]
        [Tooltip("Seconds between searches for buttons that have appeared since the last one. " +
                 "Panels that start switched off are the reason this is not a one-off search at " +
                 "startup.\n\n" +
                 "Set to 0 to search only once, if the scene never creates buttons at runtime.")]
        public float rescanEverySeconds = 1f;

        [Header("Diagnostics")]
        [Tooltip("Say how many buttons were found, and warn about the things that make a click " +
                 "silently do nothing.")]
        public bool logSetup = true;

        readonly HashSet<Object> hooked = new HashSet<Object>();
        float nextScan;
        int buttonCount;
        int toggleCount;

        void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;

            // Flat 2D, whatever it was set to. A 3D AudioSource is positioned in the world and
            // gets quieter with distance from the listener - which for a menu means a click that
            // is inaudible for no visible reason, because the thing that was clicked is a piece of
            // screen-space UI with no position in the world at all.
            source.spatialBlend = 0f;

            // Menus often pause the listener. A click that goes silent the moment the game is
            // paused is the one place a click is most needed.
            source.ignoreListenerPause = true;

            // A click is a sound EFFECT, not music - it is feedback, and it belongs on the same
            // slider as the tools and the cable tester. Added here rather than left as a setup
            // step, because a UI click is the one sound whose category is never in doubt.
            if (putOnEffectsBus && GetComponent<CircuitCrafter.Audio.AudioCategory>() == null)
            {
                CircuitCrafter.Audio.AudioCategory bus =
                    gameObject.AddComponent<CircuitCrafter.Audio.AudioCategory>();
                bus.channel = CircuitCrafter.Audio.AudioCategory.Channel.Effects;
            }
        }

        void Start()
        {
            Scan();
            if (logSetup) ReportSetup();
        }

        void Update()
        {
            if (rescanEverySeconds <= 0f || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + rescanEverySeconds;
            Scan();
        }

        /// <summary>Finds anything clickable that is not hooked up yet. Safe to call any time.</summary>
        public void Scan()
        {
            Button[] buttons = Object.FindObjectsByType<Button>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Button b in buttons)
            {
                if (b == null || hooked.Contains(b) || Excluded(b)) continue;
                hooked.Add(b);
                buttonCount++;

                // Added at runtime, so the button's own On Click list in the Inspector is left
                // exactly as it was - this cannot disturb whatever the button already does, and
                // removing this component removes the sound with it.
                b.onClick.AddListener(Play);
            }

            if (!alsoCoverToggles) return;

            Toggle[] toggles = Object.FindObjectsByType<Toggle>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Toggle t in toggles)
            {
                if (t == null || hooked.Contains(t) || Excluded(t)) continue;
                hooked.Add(t);
                toggleCount++;
                t.onValueChanged.AddListener(PlayOnToggle);
            }
        }

        bool Excluded(Selectable s)
        {
            foreach (Selectable other in dontCover)
                if (other == s) return true;
            return false;
        }

        void PlayOnToggle(bool _) { Play(); }

        /// <summary>Plays one click. Public, so anything else can use the same sound.</summary>
        public void Play()
        {
            if (clickClip == null || source == null) return;

            // PlayOneShot rather than Play, because a fast double-click should sound like two
            // clicks. Play would cut the first one off partway through to start the second, which
            // is heard as a stutter rather than as two presses.
            source.pitch = pitchVariation > 0f
                ? 1f + Random.Range(-pitchVariation, pitchVariation)
                : 1f;

            source.PlayOneShot(clickClip, volume);
        }

        // ---------------------------------------------------------------- self-reporting

        void ReportSetup()
        {
            List<string> problems = new List<string>();

            if (clickClip == null)
                problems.Add("Click Clip is empty, so every button is silent. Drop a short audio " +
                             "file in.");
            else if (clickClip.length > 0.5f)
                problems.Add("Click Clip '" + clickClip.name + "' is " +
                             clickClip.length.ToString("0.00") + "s long. A menu gets clicked " +
                             "faster than that, so presses will overlap into a mush - use " +
                             "something under 0.2s.");

            // The one that catches everybody out. A menu scene often has no 3D camera, and without
            // a listener the game has no ears: every AudioSource plays correctly into nothing, and
            // there is no error of any kind to go looking for.
            AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
            if (listener == null)
                problems.Add("There is no AudioListener in this scene, so NOTHING can be heard - " +
                             "not this, not the music. Add one to the menu camera, or to any " +
                             "always-active object. Nothing errors when it is missing; the game " +
                             "simply has no ears.");

            if (buttonCount == 0 && toggleCount == 0)
                problems.Add("No buttons found at all. Either this scene has none, or the ones it " +
                             "has are Images with a click handler rather than actual Button " +
                             "components - only real Buttons and Toggles can be found this way.");

            if (problems.Count == 0)
            {
                Debug.Log("[UiClickSound] Clicking " + buttonCount + " button(s)" +
                          (alsoCoverToggles ? " and " + toggleCount + " toggle(s)" : "") +
                          " with '" + clickClip.name + "'. Master volume is currently " +
                          AudioListener.volume.ToString("0.00") + " - the settings slider scales " +
                          "this along with everything else.", this);
                return;
            }

            string msg = "[UiClickSound] Setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>Plays the click, so it can be judged without entering Play mode twice.</summary>
        [ContextMenu("Play the click")]
        public void TestSound()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UiClickSound] Press Play first. You can also click the clip " +
                                 "itself in the Project window and preview it there.", this);
                return;
            }
            Play();
        }

        /// <summary>Lists everything currently wired for sound.</summary>
        [ContextMenu("Report covered buttons")]
        public void LogCovered()
        {
            string msg = "[UiClickSound] " + hooked.Count + " control(s) wired:";
            foreach (Object o in hooked)
            {
                if (o == null) continue;
                Component c = o as Component;
                msg += "\n    " + o.name + "   (" + o.GetType().Name + ")" +
                       (c != null && !c.gameObject.activeInHierarchy ? "   [on a hidden panel]" : "");
            }
            msg += "\n    ---- listener volume: " + AudioListener.volume.ToString("0.00");
            Debug.LogWarning(msg, this);
        }
    }
}