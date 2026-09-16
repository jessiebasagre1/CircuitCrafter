using UnityEngine;
using CircuitCrafter.Settings;

/// <summary>
/// One sound event: the clip, how loud it is, and any alternates to pick between.
///
/// A plain AudioClip field would have been enough to SWAP a sound, but not to BALANCE one.
/// Clips from different sources arrive at wildly different levels, so without a per-sound
/// volume the only way to stop one being twice as loud as the rest is to re-edit the audio
/// file. This way every sound is tuned from the Inspector, while the module is running.
/// </summary>
[System.Serializable]
public class SfxEntry
{
    [Tooltip("The sound to play. Drop ANY AudioClip here and it takes effect immediately - " +
             "including while the game is running, so you can audition sounds without stopping.\n\n" +
             "Leave empty and this event is simply silent. Nothing breaks.")]
    public AudioClip clip;

    [Tooltip("Loudness of THIS sound relative to the others, before the player's Master Volume " +
             "is applied over everything. Use this to balance a clip that arrived louder or " +
             "quieter than the rest instead of re-editing the file.")]
    [Range(0f, 1f)]
    public float volume = 1f;

    [Tooltip("Fine pitch trim for this sound only, on top of the shared random jitter. Negative " +
             "is deeper and heavier, positive is lighter. Useful for making one part feel " +
             "weightier than another without a second recording.")]
    [Range(-0.5f, 0.5f)]
    public float pitchOffset = 0f;

    [Tooltip("Leave OFF for short clicks and thuds, where random pitch variation stops a repeated " +
             "action sounding mechanical.\n\n" +
             "Turn ON for anything MUSICAL - a completion jingle, a fanfare. A tune played back a " +
             "few percent sharp or flat each time sounds out of tune, not varied.")]
    public bool ignorePitchJitter = false;

    [Tooltip("OPTIONAL extra clips for the same event. Leave EMPTY and only Clip is used.\n\n" +
             "Fill it and one of Clip plus these is chosen at random each time, which is the " +
             "cheapest way to stop a repeated action sounding mechanical.")]
    public AudioClip[] alternates;

    /// <summary>Clip, or one of clip+alternates at random when alternates are filled in.</summary>
    public AudioClip Resolve()
    {
        if (alternates == null || alternates.Length == 0) return clip;

        int total = alternates.Length + (clip != null ? 1 : 0);
        if (total == 0) return null;

        int index = Random.Range(0, total);
        if (clip != null)
            return index == 0 ? clip : alternates[index - 1];

        return alternates[index];
    }

    public bool HasSound => clip != null || (alternates != null && alternates.Length > 0);
}

/// <summary>
/// One place for the small interaction sounds: a part coming free, a part seating into its
/// socket, something going into or out of a hotbar slot, and the two failure sounds.
///
/// ATTACH THIS TO: the Managers GameObject in the COC1 scene, beside TaskManager and
/// DialogManager. It adds its own AudioSource.
///
/// EVERY SOUND IS AN INSPECTOR FIELD. Swap a clip, change its volume, trim its pitch, or add
/// alternates - all without touching code, and all live while the game is running. Clearing a
/// clip just makes that event silent.
///
/// EVERY ENTRY POINT IS STATIC AND NULL-SAFE. If no SfxPlayer exists in the scene - COC2,
/// COC3, a test scene, a scene you have not got round to yet - every call below quietly does
/// nothing. That is deliberate: the callers in DragController and PCPartItem are one line each
/// and can never throw, so adding sound cannot break a scene that has none.
///
/// TWO SETTINGS REACH THIS, AND ONLY ONE OF THEM IS READ HERE.
///   * SFX Volume is read on every play, so the learner can turn the equipment sounds down
///     without losing the music.
///   * Master Volume is NOT read here. It goes through AudioListener.volume, which already
///     multiplies every AudioSource in the game; reading it here as well would apply it twice
///     and make the slider feel far more aggressive than it is.
/// The volumes in this component are the DEVELOPER'S balance, underneath both of those.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]   // ready before anything that might play a sound on its own Start
public class SfxPlayer : MonoBehaviour
{
    public static SfxPlayer Instance { get; private set; }

    [Header("Parts")]
    [Tooltip("A part coming free of its socket - a light snick.")]
    public SfxEntry partPickup = new SfxEntry();

    [Tooltip("A part seating into a socket. The most important sound here: it is the learner's " +
             "confirmation that the step actually took, so give it more weight than the rest.")]
    public SfxEntry partSeated = new SfxEntry();

    [Header("Hotbar")]
    [Tooltip("Dropping a part into a hotbar slot.")]
    public SfxEntry hotbarStore = new SfxEntry();

    [Tooltip("Dragging a part back out of a hotbar slot.")]
    public SfxEntry hotbarTake = new SfxEntry();

    [Header("Failures")]
    [Tooltip("Released over nothing, so the part went back where it came from. Keep this dull " +
             "and quiet - it is not an error, just a non-event.")]
    public SfxEntry dropFail = new SfxEntry();

    [Tooltip("Refused: a part that cannot be removed yet, or cannot be installed yet. Played " +
             "alongside the warning dialog. Students hear this one a lot, so it should read as " +
             "'not yet' rather than as a buzzer.")]
    public SfxEntry blocked = new SfxEntry();

    [Header("Mix")]
    [Range(0f, 1f)]
    [Tooltip("Level of this WHOLE system, multiplied over each sound's own volume. Drag to 0 to " +
             "mute every interaction sound at once without clearing any clips.\n\n" +
             "This is the DEVELOPER'S balance. The learner's SFX Volume setting multiplies on top " +
             "of it, and their Master Volume on top of that.\n\n" +
             "Unticking this component also mutes it.")]
    public float masterVolume = 0.75f;

    [Range(0f, 0.3f)]
    [Tooltip("Random pitch variation applied to every sound, up and down. 0 disables it.\n\n" +
             "A disassembly is a dozen near-identical actions in a row, and a sample played back " +
             "at exactly the same pitch a dozen times reads as a machine gun rather than as work. " +
             "0.06 is subtle and enough to stop that.")]
    public float pitchJitter = 0.06f;

    [Tooltip("Ignore a repeat of the SAME clip within this many seconds. Stops a sound that fires " +
             "twice in one frame from doubling into a flam.")]
    [Min(0f)]
    public float minRetriggerSeconds = 0.04f;

    [Header("Diagnostics")]
    [Tooltip("Logs every sound played, and every call that found no clip assigned.")]
    public bool logPlays = false;

    private AudioSource source;
    private AudioClip lastClip;
    private float lastPlayTime = -99f;

    // ==================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[SfxPlayer] Duplicate on '{name}' - one already exists on " +
                             $"'{Instance.name}'. Destroying this one.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        source = GetComponent<AudioSource>();
        source.playOnAwake = false;

        // 2D. These are interface and feedback sounds, not things happening at a place in the
        // world - a click that pans away as the station camera moves reads as a fault.
        source.spatialBlend = 0f;
        source.priority = 128;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==================================================================
    // Entry points. Each takes the part where one is available, so a specific part can
    // override the shared clip - a PSU should land with more weight than a stick of RAM.
    // ==================================================================

    public static void PartPickedUp(PCPartItem part)
    {
        if (Instance == null) return;
        Instance.Play(Instance.partPickup, part != null ? part.pickupSound : null, part);
    }

    public static void PartSeated(PCPartItem part)
    {
        if (Instance == null) return;
        Instance.Play(Instance.partSeated, part != null ? part.installSound : null, part);
    }

    public static void HotbarStored()
    {
        if (Instance == null) return;
        Instance.Play(Instance.hotbarStore, null, null);
    }

    public static void HotbarTaken()
    {
        if (Instance == null) return;
        Instance.Play(Instance.hotbarTake, null, null);
    }

    public static void DropFailed()
    {
        if (Instance == null) return;
        Instance.Play(Instance.dropFail, null, null);
    }

    public static void Blocked()
    {
        if (Instance == null) return;
        Instance.Play(Instance.blocked, null, null);
    }

    /// <summary>
    /// Plays a sound owned by some OTHER component, through this mixer.
    ///
    /// Lets a script keep its own clips in its own Inspector - TaskCompleteSfx holds one per
    /// stage - while master volume, the retrigger guard and the pitch rules all still come from
    /// here. Without it, every new sound would need another field on this class and another
    /// static method, and the two would drift.
    /// </summary>
    /// <param name="bypassRetriggerGuard">For a sound that is MEANT to repeat rapidly - a
    /// typewriter blip fires many times a second, and the guard exists to stop one action being
    /// heard twice, which is the opposite case. Leave false for everything else.</param>
    public static void PlayCustom(SfxEntry entry, bool bypassRetriggerGuard = false)
    {
        if (Instance == null || entry == null) return;
        Instance.Play(entry, null, null, bypassRetriggerGuard);
    }

    // ==================================================================

    /// <summary>
    /// The one place a sound actually gets played.
    /// </summary>
    /// <param name="entry">The shared settings for this event - volume and pitch come from here
    /// even when the clip does not.</param>
    /// <param name="overrideClip">A specific part's own clip, which wins over the entry's.</param>
    private void Play(SfxEntry entry, AudioClip overrideClip, PCPartItem part,
                      bool bypassRetriggerGuard = false)
    {
        // Unticking the component is a mute switch. Checked here rather than by relying on
        // Update, because nothing in this class runs per-frame.
        if (!isActiveAndEnabled || source == null) return;
        if (entry == null) return;

        AudioClip clip = overrideClip != null ? overrideClip : entry.Resolve();

        if (clip == null)
        {
            if (logPlays)
                Debug.Log("[SfxPlayer] Nothing to play - no clip assigned for this event" +
                          (part != null ? $" (part '{part.name}')" : "") + ".", this);
            return;
        }

        // Same clip, same instant: almost always one action reported twice rather than two
        // real actions. Playing it twice just smears the transient.
        if (!bypassRetriggerGuard &&
            clip == lastClip && Time.unscaledTime - lastPlayTime < minRetriggerSeconds) return;

        lastClip = clip;
        lastPlayTime = Time.unscaledTime;

        float jitter = (pitchJitter > 0f && !entry.ignorePitchJitter)
            ? Random.Range(-pitchJitter, pitchJitter)
            : 0f;
        source.pitch = Mathf.Clamp(1f + entry.pitchOffset + jitter, 0.2f, 3f);

        // The learner's SFX setting, but NOT their Master Volume: master already goes through
        // AudioListener.volume, which multiplies every AudioSource in the game. Reading it here
        // too would apply it twice and make the slider feel far more aggressive than it is.
        float level = Mathf.Clamp01(entry.volume)
                    * Mathf.Clamp01(masterVolume)
                    * Mathf.Clamp01(GameSettings.SfxVolume01);

        source.PlayOneShot(clip, level);

        if (logPlays)
            Debug.Log($"[SfxPlayer] {clip.name} at pitch {source.pitch:F2}, " +
                      $"volume {entry.volume:F2} x {masterVolume:F2} x sfx {GameSettings.SfxVolume01:F2}" +
                      (part != null ? $" for '{part.name}'" : "") + ".", this);
    }

    // ==================================================================
    // Inspector helpers
    // ==================================================================

    /// <summary>Plays every assigned sound in turn so you can balance the mix by ear without
    /// playing through the module. Right-click the component header in Play mode.</summary>
    [ContextMenu("Test all sounds")]
    public void TestAllSounds()
    {
        StartCoroutine(TestRoutine());
    }

    private System.Collections.IEnumerator TestRoutine()
    {
        SfxEntry[] entries = { partPickup, partSeated, hotbarStore, hotbarTake, dropFail, blocked };
        string[] names = { "Part Pickup", "Part Seated", "Hotbar Store", "Hotbar Take", "Drop Fail", "Blocked" };

        float savedRetrigger = minRetriggerSeconds;
        minRetriggerSeconds = 0f;                 // the test is meant to repeat

        for (int i = 0; i < entries.Length; i++)
        {
            if (!entries[i].HasSound)
            {
                Debug.LogWarning($"[SfxPlayer] '{names[i]}' has no clip assigned.", this);
                continue;
            }

            Debug.Log($"[SfxPlayer] Testing '{names[i]}'...", this);
            Play(entries[i], null, null);
            yield return new WaitForSecondsRealtime(0.7f);
        }

        minRetriggerSeconds = savedRetrigger;
    }

    /// <summary>Lists which events have a sound and which are silent. Works outside Play mode.</summary>
    [ContextMenu("Report sound setup")]
    public void ReportSoundSetup()
    {
        SfxEntry[] entries = { partPickup, partSeated, hotbarStore, hotbarTake, dropFail, blocked };
        string[] names = { "Part Pickup", "Part Seated", "Hotbar Store", "Hotbar Take", "Drop Fail", "Blocked" };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[SfxPlayer] on '{name}' - master volume {masterVolume:F2}, jitter {pitchJitter:F2}");

        for (int i = 0; i < entries.Length; i++)
        {
            SfxEntry e = entries[i];
            string clipName = e.clip != null ? e.clip.name : "<none>";
            int alts = e.alternates != null ? e.alternates.Length : 0;

            sb.AppendLine($"  {names[i],-13}: {clipName}" +
                          (alts > 0 ? $" (+{alts} alternate{(alts == 1 ? "" : "s")})" : "") +
                          $"  vol {e.volume:F2}  pitch {e.pitchOffset:+0.00;-0.00; 0.00}" +
                          (e.HasSound ? "" : "   <-- SILENT"));
        }

        Debug.Log(sb.ToString(), this);
    }
}