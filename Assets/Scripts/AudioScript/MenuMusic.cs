using System.Collections;
using UnityEngine;

/// <summary>
/// Background music for the Main Menu, and nowhere else.
///
/// ATTACH THIS TO: an empty GameObject on the Main Menu scene - "MenuMusic" beside your Managers is
/// fine. It adds its own AudioSource.
///
/// IT IS SCENE-BOUND ON PURPOSE, and that is the whole of "main menu only". No DontDestroyOnLoad,
/// no singleton, nothing static: when the learner picks a COC, Unity destroys this scene and the
/// music stops with it. A menu track that followed them into a PC assembly would be the kind of
/// thing you only notice in a lab full of students with the volume up.
///
/// THE SETTINGS SLIDER ALREADY CONTROLS IT. SettingsApplier sets AudioListener.volume from
/// GameSettings.MasterVolume01, and that is a global multiplier over every AudioSource. So Volume
/// below is this track's own level - how loud the music is RELATIVE to everything else - and the
/// learner's Master Volume scales it. Nothing here needs to read the setting.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class MenuMusic : MonoBehaviour
{
    [Header("Track")]
    [SerializeField] private AudioClip clip;

    [Tooltip("This track's own level, before the learner's Master Volume is applied over it.\n\n" +
             "Menu music wants to sit UNDER the interface, not in front of it - 0.3 to 0.5 is " +
             "usually right. Anything a learner has to turn down before they can think is too loud.")]
    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.4f;

    [Tooltip("Leave on. A menu track that stops after two minutes and leaves silence reads as the " +
             "game having crashed.")]
    [SerializeField] private bool loop = true;

    [Header("Fade")]
    [Tooltip("Seconds to fade up from silence. A track that starts at full volume the instant the " +
             "scene loads is a jolt, especially with headphones on. 1 to 2 seconds is enough; 0 " +
             "starts it at full.")]
    [Min(0f)]
    [SerializeField] private float fadeInSeconds = 1.5f;

    private AudioSource source;

    // ==================================================================

    private void Awake()
    {
        source = GetComponent<AudioSource>();

        source.clip = clip;
        source.loop = loop;
        source.playOnAwake = false;     // started here, so the fade is not skipped

        // 2D. A menu has no world for the sound to sit in, and a spatialised track would pan and
        // fade as the camera moved - which on a menu with any camera animation at all sounds like a
        // fault rather than an effect.
        source.spatialBlend = 0f;

        // Above the default 128, so the track is never the voice Unity drops when several sounds
        // are competing. On a menu it is usually the only one, which makes this cheap insurance.
        source.priority = 64;
    }

    private void Start()
    {
        if (clip == null)
        {
            // Said out loud, because silence is exactly what a working-but-unassigned music player
            // sounds like, and there is nothing else to notice.
            Debug.LogWarning("[MenuMusic] No Clip assigned, so the menu is silent. Drop an audio " +
                             "file into the Clip field.", this);
            return;
        }

        if (FindAnyObjectByType<AudioListener>() == null)
        {
            // The other way to get silence with everything apparently set up. One AudioListener is
            // required per scene and it normally lives on the camera; a menu built from scratch,
            // with a camera added by hand, often has none.
            Debug.LogWarning("[MenuMusic] There is no AudioListener in this scene, so nothing can be " +
                             "heard. Add one to the Main Camera.", this);
        }

        source.volume = fadeInSeconds > 0f ? 0f : volume;
        source.Play();

        if (fadeInSeconds > 0f) StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        float elapsed = 0f;

        while (elapsed < fadeInSeconds)
        {
            // Unscaled, because a menu can sit at timeScale 0 - the pause menu and the exit
            // confirmation both do it - and a fade driven by scaled time would stop halfway and
            // leave the track quiet for as long as the panel is open.
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(0f, volume, elapsed / fadeInSeconds);
            yield return null;
        }

        source.volume = volume;
    }

    /// <summary>Sets this track's level while it is playing. For a music slider, if you ever add one
    /// separate from Master Volume.</summary>
    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (source != null) source.volume = volume;
    }
}
