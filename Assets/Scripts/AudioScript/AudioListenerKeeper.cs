using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Guarantees that exactly one AudioListener exists and is alive, in every scene, at every
/// moment - including while the player is inside a station.
///
/// YOU DO NOT HAVE TO ADD THIS TO ANYTHING. It creates itself before the first scene loads
/// and survives every scene change after that. Adding it to an object by hand also works and
/// takes priority; the auto-created one stands down.
///
/// WHAT IT FIXES
/// Unity puts the AudioListener on the Main Camera by default, and that is fine right up until
/// something switches the Main Camera off. Every station in this game does exactly that: the
/// crimping view, the stripping view, the punch-down stage, the tester bench and the COC 1
/// close-ups all disable the first-person camera and enable their own. The listener goes off
/// with it, and from that moment:
///
///   - the console fills with "There are no audio listeners in the scene", once per frame,
///     burying every other message in a log you might actually need to read; and
///   - NOTHING PLAYS. Not the interaction sounds, not the tester, not the punch-down tool.
///     The AudioSources all run, SfxPlayer reports playing them, the clips are assigned and
///     the volumes are right - and there is nothing to hear them.
///
/// The second half is the one that wastes an afternoon, because every part of the sound
/// system looks correct when you check it. An AudioSource with no listener is a radio
/// transmitter in an empty room.
///
/// THE FIX IS NOT "PUT A SECOND LISTENER ON THE STATION CAMERA". Two enabled listeners is
/// its own warning and the results are undefined - Unity picks one and the other silently
/// does nothing. This component takes the opposite approach: ONE listener, on an object that
/// is never switched off, and every other listener in the scene switched off so they cannot
/// fight over it.
///
/// 3D SOUND STILL WORKS. A listener parked at the world origin would flatten the panning and
/// distance falloff of any 3D AudioSource. So this one follows whichever camera is actually
/// rendering, frame by frame - the first-person camera while walking, the station camera once
/// inside one. The listener is therefore always where the player is looking from, which is
/// what it was on the Main Camera for in the first place.
///
/// Most of this game's sounds are 2D anyway (SfxPlayer sets spatialBlend to 0 deliberately -
/// a click that pans away as a station camera moves reads as a fault), so for those the
/// following costs nothing and changes nothing. It matters for anything you place in the
/// world later.
/// </summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class AudioListenerKeeper : MonoBehaviour
{
    public static AudioListenerKeeper Instance { get; private set; }

    [Tooltip("Move this listener to whatever camera is currently rendering, every frame.\n\n" +
             "Leave this ON unless you have a reason not to. It is what keeps 3D audio " +
             "positioned correctly once a station camera takes over from the first-person one.\n\n" +
             "Turning it off parks the listener wherever this object sits. Harmless for 2D " +
             "sounds - which is nearly everything here - and wrong for anything spatial.")]
    [SerializeField] private bool followActiveCamera = true;

    [Tooltip("Switch off any OTHER AudioListener found in the scene.\n\n" +
             "Two enabled listeners is undefined behaviour in Unity and logs its own warning, " +
             "so this is not optional in practice - but it is a field rather than a hard rule " +
             "so you can see what it is doing and turn it off while investigating something.")]
    [SerializeField] private bool disableOtherListeners = true;

    [Tooltip("Name every listener it switches off, and say which camera it is following when " +
             "that changes. Worth leaving on until the scene is quiet, then off.")]
    [SerializeField] private bool verbose = true;

    private AudioListener listener;
    private Camera lastFollowed;

    // ==================================================================
    // Creating itself
    // ==================================================================

    /// <summary>
    /// BeforeSceneLoad, so the listener exists before the first AudioSource can try to play
    /// through it. Statics survive LoadScene, so this happens once per run of the game and
    /// holds for every scene afterwards.
    ///
    /// WHY AUTO-CREATE RATHER THAN ASK YOU TO ADD IT TO EVERY SCENE. The bug it fixes is
    /// present in every scene that has a station in it, which is all of them, and a fix you
    /// have to remember to apply in five places is a fix that is missing from the sixth. The
    /// same reasoning as SettingsPanelPresence spawning a settings panel where none exists.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null) return;

        var go = new GameObject("AudioListenerKeeper (auto)");
        go.AddComponent<AudioListenerKeeper>();
        DontDestroyOnLoad(go);
    }

    // ==================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A hand-placed one in the scene wins over the auto-created one, and vice versa -
            // whichever got here first keeps the job. Destroy(this), not the GameObject: this
            // component is often dropped onto Managers, and taking that object out would kill
            // everything else on it.
            if (verbose)
                Debug.Log($"[AudioListenerKeeper] A second keeper on '{name}' stood down - " +
                          $"'{Instance.name}' already has the listener.", this);
            Destroy(this);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        listener = GetComponent<AudioListener>();
        if (listener == null) listener = gameObject.AddComponent<AudioListener>();
        listener.enabled = true;
    }

    private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    private void Start()
    {
        TakeOverListeners();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A freshly loaded scene brings its own Main Camera, and that camera brings its own
        // AudioListener. Without this the new one and ours would both be enabled from the
        // moment the scene appears.
        lastFollowed = null;
        TakeOverListeners();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==================================================================

    /// <summary>Switches off every listener that is not ours. Inactive objects included - a
    /// station camera is switched off most of the time, and its listener would come back the
    /// moment the station opened.</summary>
    private void TakeOverListeners()
    {
        if (listener != null) listener.enabled = true;
        if (!disableOtherListeners) return;

        var all = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int switchedOff = 0;

        foreach (var other in all)
        {
            if (other == null || other == listener) continue;
            if (!other.enabled) continue;

            other.enabled = false;
            switchedOff++;

            if (verbose)
                Debug.Log($"[AudioListenerKeeper] Switched off the AudioListener on " +
                          $"'{PathOf(other.transform)}'. This keeper owns the only live one now, " +
                          $"so switching that camera off can no longer silence the game.", other);
        }

        if (verbose && switchedOff == 0)
            Debug.Log("[AudioListenerKeeper] Ready - no other AudioListener in this scene.", this);
    }

    private void LateUpdate()
    {
        if (!followActiveCamera) return;

        Camera cam = ActiveCamera();
        if (cam == null) return;

        transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);

        if (verbose && cam != lastFollowed)
        {
            lastFollowed = cam;
            Debug.Log($"[AudioListenerKeeper] Now hearing from '{cam.name}'.", cam);
        }
    }

    /// <summary>
    /// The camera the player is actually looking through.
    ///
    /// NOT Camera.main. That returns whichever camera is tagged MainCamera, and the station
    /// cameras are not - which is the whole reason this class exists. Camera.allCameras only
    /// contains ENABLED cameras, so the highest-depth one in it is the one drawing last, and
    /// therefore the one on screen.
    /// </summary>
    private static Camera ActiveCamera()
    {
        Camera[] cams = Camera.allCameras;
        if (cams == null || cams.Length == 0) return null;

        Camera best = null;
        foreach (var c in cams)
        {
            if (c == null || !c.isActiveAndEnabled) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        return best;
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // ==================================================================

    [ContextMenu("Report audio listeners")]
    private void ReportListeners()
    {
        var all = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"[AudioListenerKeeper] {all.Length} AudioListener(s) in the scene:");
        foreach (var l in all)
        {
            if (l == null) continue;
            sb.AppendLine($"  '{PathOf(l.transform)}'   component {(l.enabled ? "ON" : "off")}" +
                          $"   object {(l.gameObject.activeInHierarchy ? "active" : "INACTIVE")}" +
                          (l == listener ? "   <-- this keeper's" : ""));
        }

        Camera cam = ActiveCamera();
        sb.AppendLine($"  Currently rendering: {(cam != null ? cam.name : "NOTHING - no enabled camera")}");
        sb.AppendLine($"  AudioListener.volume (Master Volume): {AudioListener.volume:F2}");

        if (Mathf.Approximately(AudioListener.volume, 0f))
            sb.AppendLine("  *** Master volume is ZERO. Everything is silent regardless of clips. ***");

        Debug.Log(sb.ToString(), this);
    }
}
