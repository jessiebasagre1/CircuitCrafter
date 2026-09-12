using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach this to MobileControlsCanvas itself. Shows the whole mobile control UI (joystick, look
/// field, buttons) on a touch device and hides it everywhere else, so it doesn't sit uselessly on
/// top of a desktop build.
///
/// IT USED TO ONLY HIDE, AND THAT WAS THE BUG.
/// The first version switched the canvas off on PC and did nothing else. Which is fine right up
/// until the canvas is authored INACTIVE in a scene - then Awake and OnEnable never run at all,
/// because Unity does not run either on an inactive GameObject, and nothing else in the project is
/// looking for it. The controls are simply absent on the phone, with nothing in the log, and the
/// component that is supposed to be responsible for them is the one component that cannot help:
/// a script cannot wake its own GameObject.
///
/// So there are now two halves. The instance half handles a canvas that is already active. The
/// static half below runs after every scene load, finds canvases that start inactive, and switches
/// them on when the device wants them - which is the only place that job can be done from.
/// </summary>
public class MobileControlsVisibility : MonoBehaviour
{
    [Tooltip("Shows these controls in the Editor even though the Editor is not a touch device.\n\n" +
             "Application.isMobilePlatform is FALSE in the Editor - with the build target set to " +
             "Android, and in the Device Simulator too - so without this you cannot see the mobile " +
             "layout at all without making a build. Leave it OFF for normal desktop work; it has no " +
             "effect in a real build either way.")]
    [SerializeField] private bool showInEditor = false;

    [Tooltip("Says in the Console what this decided and why. Worth leaving on while you are " +
             "chasing missing controls - it is one line per scene load.")]
    [SerializeField] private bool logDecision = true;

    // ==================================================================

    /// <summary>Whether the touch controls belong on this device right now.</summary>
    public bool Wanted
    {
        get
        {
#if UNITY_EDITOR
            if (showInEditor) return true;
#endif
            return Application.isMobilePlatform;
        }
    }

    private void Awake() => ApplyPlatformVisibility();

    /// <summary>
    /// Re-checks every time this canvas is re-enabled, not just once at Awake.
    ///
    /// RepairStation's Disable On Enter array, StationCameraSwitch's enable/disable lists and the
    /// quiz camera hand-off all call SetActive on this canvas with no idea what platform they are
    /// running on. Awake alone cannot protect against that once the object is flipped back on
    /// later; OnEnable catches it immediately, without every script that might touch this canvas
    /// having to remember a platform check.
    /// </summary>
    private void OnEnable() => ApplyPlatformVisibility();

    private void ApplyPlatformVisibility()
    {
        bool wanted = Wanted;

        if (logDecision)
            Debug.Log($"[MobileControlsVisibility] '{name}' - touch controls {(wanted ? "SHOWN" : "hidden")} " +
                      $"(isMobilePlatform={Application.isMobilePlatform}).", this);

        // Only ever switches itself OFF here. Switching on is the static half's job, because by the
        // time this object is inactive this method is no longer being called at all.
        if (!wanted) gameObject.SetActive(false);
    }

    // ==================================================================
    // The half that can reach an object that starts switched off
    // ==================================================================

    /// <summary>
    /// Runs once at startup and after every scene load, and switches on any mobile control canvas
    /// that a scene left inactive.
    ///
    /// WHY IT HAS TO BE STATIC AND OUTSIDE THE OBJECT
    /// Unity does not run Awake, OnEnable or Start on an inactive GameObject. A canvas authored
    /// switched off - or switched off by a station's Disable On Enter and never switched back -
    /// therefore has no code of its own running to notice that this device wants it. Something
    /// outside it has to look, and FindObjectsByType with FindObjectsInactive.Include is the only
    /// way to see it at all.
    ///
    /// Cheap enough to be unconditional: it is one search per scene load, over components rather
    /// than every object, and a repair site has one of these at most.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        RevealAll();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RevealAll();

    /// <summary>Switches on every mobile control canvas this device wants. Public so a scene that
    /// hides one deliberately can put it back without holding a reference.</summary>
    public static void RevealAll()
    {
        var found = FindObjectsByType<MobileControlsVisibility>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var controls in found)
        {
            if (controls == null) continue;
            if (!controls.Wanted) continue;
            if (controls.gameObject.activeSelf) continue;

            controls.gameObject.SetActive(true);

            Debug.Log($"[MobileControlsVisibility] '{controls.name}' was switched off in this scene - " +
                      "turned on, because this device uses touch controls.", controls);
        }

        if (found.Length == 0)
        {
            // Silence here is the failure that wastes an afternoon: a scene with no mobile control
            // canvas at all looks exactly like one whose canvas is hidden, and the fixes are
            // completely different.
            Debug.Log("[MobileControlsVisibility] No mobile control canvas in this scene. If this is " +
                      "a scene the learner walks around in, the canvas is missing rather than hidden - " +
                      "copy it in from a scene that has one.");
        }
    }
}