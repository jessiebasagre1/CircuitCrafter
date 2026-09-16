// StationInteractionRelay.cs
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// One on-screen Enter/Exit Station button that works for EVERY station in the game - assembly,
/// bootable USB, OS install, service report - by routing through whichever station the player is
/// near or inside.
///
/// Setup:
///   1. Create ONE UI Button on your mobile controls (not one per station).
///   2. Add this component and assign Button Root.
///   3. Wire the Button's OnClick to this component's RequestInteract().
///   4. Optionally assign a TMP_Text to Button Label for an "Exit Service Report" caption.
///
/// A STATION YOU ARE INSIDE ALWAYS WINS
///
/// NearbyOrActive is written by trigger events, and a trigger event is a one-off: if the player is
/// moved, hidden or re-parented while a station view is up - which several stations do - the exit
/// event can arrive at a moment nothing is listening, and the reference is left pointing at
/// nothing. On a keyboard that is survivable, because Escape and the E key are still there. On a
/// phone this button is the ONLY way out, so the same lost event becomes a learner stuck on the
/// service report screen with no control that does anything.
///
/// So this does not trust the reference on its own. Each frame it looks for a station that is
/// actually showing its own camera right now, and prefers that over whatever the triggers last
/// said. A station that is on screen is a station you can leave, whatever physics believes.
///
/// THE OTHER WAY OUT GOES MISSING
///
/// The button can also be present, correct and invisible: stations hide the touch controls while
/// their view is up, via Disable On Station View, and a button parented under those controls is
/// switched off along with them. Nothing errors - the exit button simply is not drawn, on exactly
/// the screens it is needed on. That one is checked for at startup and named, because it is
/// invisible in the Inspector and impossible to guess at from the symptom.
/// </summary>
public class StationInteractionRelay : MonoBehaviour
{
    [SerializeField] private GameObject buttonRoot;
    [SerializeField] private TMP_Text buttonLabel; // optional

    [Header("Diagnostics")]
    [Tooltip("Say when the button shows and hides, and name the reason when it cannot appear. " +
             "Leave ON until the button behaves - a missing button gives you nothing to search for.")]
    [SerializeField] private bool logVisibility = true;

    [Tooltip("At startup, check whether Button Root is parented under anything a station hides " +
             "while its view is up. That is the one fault that makes the exit button vanish " +
             "precisely when it is needed, and it cannot be seen in the Inspector.")]
    [SerializeField] private bool auditParentsAtStart = true;

    bool lastShown;
    bool warnedAboutHiddenParent;
    float nextScan;
    StationCameraSwitch[] known = new StationCameraSwitch[0];

    void Start()
    {
        Rescan();
        if (auditParentsAtStart) AuditButtonParents();
    }

    void Update()
    {
        if (buttonRoot == null)
        {
            if (!warnedAboutHiddenParent)
            {
                warnedAboutHiddenParent = true;
                Debug.LogWarning("[StationInteractionRelay] Button Root is not assigned, so the " +
                                 "Enter/Exit button can never show. On a touch build that means no " +
                                 "way into or out of any station.", this);
            }
            return;
        }

        if (Time.unscaledTime >= nextScan) Rescan();

        StationCameraSwitch station = Target();
        bool shouldShow = station != null;

        if (buttonRoot.activeSelf != shouldShow)
        {
            buttonRoot.SetActive(shouldShow);

            if (logVisibility && shouldShow != lastShown)
                Debug.Log("[StationInteractionRelay] Enter/Exit button " +
                          (shouldShow ? "SHOWN for '" + station.name + "'" : "hidden - no station " +
                           "near or active"), this);

            lastShown = shouldShow;
        }

        // Switched on, and still not drawn. The only way that happens is a parent being off, and
        // it is worth an error rather than a warning: the player is inside a station with no way
        // out, which on a phone is the end of the run.
        if (shouldShow && !buttonRoot.activeInHierarchy && !warnedAboutHiddenParent)
        {
            warnedAboutHiddenParent = true;
            Debug.LogError("[StationInteractionRelay] The Enter/Exit button is switched ON but " +
                           "still not drawn, because " + NameHiddenAncestor() + " is switched off." +
                           (station != null && station.IsUsingStationCamera
                               ? " The player is INSIDE '" + station.name + "' right now and has no " +
                                 "way out on a touch device."
                               : "") +
                           "\n\nMove the button out from under that object - give it its own small " +
                           "canvas that nothing disables - and this cannot happen again.", this);
        }

        if (shouldShow && buttonLabel != null)
        {
            buttonLabel.text = station.IsUsingStationCamera
                ? $"Exit {station.DisplayName}"
                : $"Enter {station.DisplayName}";
        }
    }

    /// <summary>
    /// The station this button should operate: one the player is INSIDE if there is one, otherwise
    /// whatever the triggers last reported.
    ///
    /// The order matters and is the whole fix. NearbyOrActive can be null while a station view is
    /// on screen - a lost trigger event, a player object hidden for the view - and taking it at its
    /// word then hides the only exit a phone has.
    /// </summary>
    StationCameraSwitch Target()
    {
        for (int i = 0; i < known.Length; i++)
        {
            StationCameraSwitch s = known[i];
            if (s != null && s.IsUsingStationCamera) return s;
        }
        return StationCameraSwitch.NearbyOrActive;
    }

    void Rescan()
    {
        nextScan = Time.unscaledTime + 0.5f;
        known = Object.FindObjectsByType<StationCameraSwitch>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>Wire this to the mobile Enter/Exit Station button's OnClick.</summary>
    public void RequestInteract()
    {
        StationCameraSwitch station = Target();

        if (station == null)
        {
            if (logVisibility)
                Debug.Log("[StationInteractionRelay] Tapped with no station near or active - " +
                          "nothing to do.", this);
            return;
        }

        station.HandleInteractPressed();
    }

    // ------------------------------------------------------------------ self-reporting

    /// <summary>
    /// Warns when the button lives under something a station switches off.
    ///
    /// Checked at startup rather than waiting for it to happen, because when it does happen the
    /// player is already inside the station and already stuck - and the Console is the last place
    /// anybody looks at that point.
    /// </summary>
    void AuditButtonParents()
    {
        if (buttonRoot == null) return;

        HashSet<Transform> ancestors = new HashSet<Transform>();
        Transform t = buttonRoot.transform;
        while (t != null) { ancestors.Add(t); t = t.parent; }

        List<string> clashes = new List<string>();

        StationCameraSwitch[] stations = Object.FindObjectsByType<StationCameraSwitch>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (StationCameraSwitch s in stations)
        {
            if (s == null || s.disableOnStationView == null) continue;

            foreach (GameObject hidden in s.disableOnStationView)
            {
                if (hidden == null || !ancestors.Contains(hidden.transform)) continue;
                clashes.Add("'" + s.name + "' hides '" + hidden.name + "'");
            }
        }

        if (clashes.Count == 0)
        {
            if (logVisibility)
                Debug.Log("[StationInteractionRelay] Enter/Exit button '" + buttonRoot.name +
                          "' covers " + stations.Length + " station(s), and nothing hides it while " +
                          "a station view is up.", this);
            return;
        }

        string msg = "[StationInteractionRelay] The Enter/Exit button is parented under something " +
                     "that gets switched off while a station view is up, so it will DISAPPEAR at " +
                     "exactly the moment it is the only way out:";
        foreach (string c in clashes) msg += "\n  - " + c;
        msg += "\n\nOn a keyboard this is survivable - E still works. On a phone the learner is " +
               "stuck in the station.\n\nFix: give the Enter/Exit button its own canvas, outside " +
               "the touch controls, and leave that canvas out of every station's Disable On " +
               "Station View list.";

        Debug.LogError(msg, this);
    }

    string NameHiddenAncestor()
    {
        Transform t = buttonRoot != null ? buttonRoot.transform.parent : null;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) return "its parent '" + t.name + "'";
            t = t.parent;
        }
        return "one of its parents";
    }

    /// <summary>Prints which station the button would operate, and why it is or is not showing.</summary>
    [ContextMenu("Why is the station button hidden?")]
    public void LogState()
    {
        StationCameraSwitch target = Target();
        StationCameraSwitch active = null;
        foreach (StationCameraSwitch s in known)
            if (s != null && s.IsUsingStationCamera) { active = s; break; }

        Debug.LogWarning("[StationInteractionRelay]" +
            "\n    Button Root        : " + (buttonRoot != null
                ? "'" + buttonRoot.name + "'  activeSelf=" + buttonRoot.activeSelf +
                  "  activeInHierarchy=" + buttonRoot.activeInHierarchy
                : "NOT ASSIGNED") +
            "\n    station in view    : " + (active != null ? "'" + active.name + "'" : "none") +
            "\n    NearbyOrActive     : " + (StationCameraSwitch.NearbyOrActive != null
                ? "'" + StationCameraSwitch.NearbyOrActive.name + "'" : "null") +
            "\n    button would operate: " + (target != null ? "'" + target.name + "'" : "nothing") +
            (buttonRoot != null && buttonRoot.activeSelf && !buttonRoot.activeInHierarchy
                ? "\n    BLOCKED BY         : " + NameHiddenAncestor() : ""), this);
    }
}