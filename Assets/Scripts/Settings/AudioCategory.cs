// AudioCategory.cs
using UnityEngine;
using CircuitCrafter.Settings;

namespace CircuitCrafter.Audio
{
    /// <summary>
    /// Says which volume slider an AudioSource belongs to, and keeps it obeying that slider.
    ///
    /// HOW THE THREE SLIDERS FIT TOGETHER
    ///
    /// Master is AudioListener.volume - one global gain over everything the game can hear, set by
    /// SettingsApplier. Music and Effects are applied per AudioSource, here. They multiply rather
    /// than compete: a click authored at 0.6, on the effects bus at 0.9, with master at 0.8, comes
    /// out at 0.43.
    ///
    /// That split is what lets a learner say "keep the sounds, lose the music", which in a room of
    /// twenty machines is the request that actually gets made. One slider can only answer "make it
    /// all quieter", and forces a choice between a silent game and a noisy classroom.
    ///
    /// IT DOES NOT FIGHT WHOEVER ELSE OWNS THE VOLUME
    ///
    /// MenuMusic fades itself up from silence by writing AudioSource.volume every frame. A
    /// component that simply overwrote that value would cancel the fade; one that wrote it only
    /// when a slider moved would be overwritten BY the fade a frame later. Either way the setting
    /// and the fade would be at war, and which one won would depend on script execution order.
    ///
    /// So this does not claim ownership. Each frame it checks whether the volume is still what it
    /// last wrote; if somebody else has changed it, that new value is taken as the authored level
    /// and scaled from there. A fade to 0.4 becomes a fade to 0.4 x the effects slider, and neither
    /// side has to know about the other.
    ///
    /// SETUP: add it to any GameObject with an AudioSource and pick the channel. Sources without
    /// one still obey Master - they just ignore the two finer sliders.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DefaultExecutionOrder(500)]
    public class AudioCategory : MonoBehaviour
    {
        public enum Channel
        {
            /// <summary>Menu loops, ambience, anything atmospheric.</summary>
            Music,
            /// <summary>Button clicks, tools, the cable tester - feedback rather than atmosphere.</summary>
            Effects
        }

        [Tooltip("Which slider this source answers to.\n\n" +
                 "UI clicks are Effects, not a channel of their own. A trainee who turns effects " +
                 "down has decided the game should be quieter, and the button they press most " +
                 "often staying loud through that is not respecting the decision.")]
        public Channel channel = Channel.Effects;

        [Header("Diagnostics")]
        [Tooltip("On the first one of these to start, count the AudioSources in the scene that have " +
                 "NO AudioCategory and say so. Those still obey Master, but the music and effects " +
                 "sliders will not touch them - which reads as the slider being broken.")]
        public bool reportUntaggedSources = true;

        AudioSource source;
        float authored = -1f;
        float lastWritten = -1f;

        static bool reportedThisScene;

        /// <summary>The current level for a channel, 0 to 1. Master is applied separately.</summary>
        public static float LevelOf(Channel channel)
        {
            return channel == Channel.Music ? GameSettings.MusicVolume01 : GameSettings.SfxVolume01;
        }

        /// <summary>This source's channel level right now.</summary>
        public float Level { get { return LevelOf(channel); } }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            if (source != null) authored = source.volume;
        }

        void OnEnable()
        {
            GameSettings.OnChanged += Apply;
            Apply();
        }

        void OnDisable() { GameSettings.OnChanged -= Apply; }

        void Start()
        {
            if (!reportUntaggedSources || reportedThisScene) return;
            reportedThisScene = true;
            ReportUntagged();
        }

        /// <summary>
        /// Runs late, after anything that animates its own volume has had its turn this frame.
        /// </summary>
        void LateUpdate() { Apply(); }

        void Apply()
        {
            if (source == null) return;

            // Somebody else wrote to the volume since we last did - a fade, a ducking script, an
            // Inspector edit during play. Whatever it was, that is the new authored level, and the
            // slider scales from there instead of throwing it away.
            if (lastWritten < 0f || !Mathf.Approximately(source.volume, lastWritten))
                authored = source.volume;

            float wanted = Mathf.Clamp01(authored) * Mathf.Clamp01(Level);
            source.volume = wanted;
            lastWritten = wanted;
        }

        /// <summary>
        /// Names the AudioSources that will not follow the music and effects sliders.
        ///
        /// They are not broken - Master still reaches them, because that is a global gain. But
        /// dragging the Music slider and hearing the music carry on regardless is the sort of thing
        /// that gets diagnosed as "the slider does nothing" rather than as "that source was never
        /// put on a bus", so it is worth naming them by hand once.
        /// </summary>
        void ReportUntagged()
        {
            AudioSource[] all = Object.FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            string names = "";
            int missing = 0;

            foreach (AudioSource s in all)
            {
                if (s == null || s.GetComponent<AudioCategory>() != null) continue;
                missing++;
                if (missing <= 8) names += "\n    " + Path(s.transform);
            }

            if (missing == 0)
            {
                Debug.Log("[AudioCategory] All " + all.Length + " AudioSource(s) in this scene are " +
                          "on a bus. Music " + GameSettings.MusicVolume01.ToString("0.00") +
                          ", effects " + GameSettings.SfxVolume01.ToString("0.00") +
                          ", master " + AudioListener.volume.ToString("0.00") + ".", this);
                return;
            }

            Debug.LogWarning("[AudioCategory] " + missing + " AudioSource(s) have no AudioCategory, " +
                             "so the Music and Effects sliders cannot reach them - only Master " +
                             "will:" + names + (missing > 8 ? "\n    ..." : "") +
                             "\n\nAdd an AudioCategory to each and pick Music or Effects.", this);
        }

        static string Path(Transform t)
        {
            string path = t.name;
            Transform p = t.parent;
            while (p != null) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        /// <summary>A static survives leaving Play mode, so it needs clearing between runs.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { reportedThisScene = false; }
    }
}
