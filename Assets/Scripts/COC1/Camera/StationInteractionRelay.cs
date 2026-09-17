using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Drives the on-screen station button(s) for every station in the game, by routing through
/// StationCameraSwitch - the component that owns entering and leaving a station view.
///
/// TWO LAYOUTS, AND YOU PICK BY WHICH FIELDS YOU FILL IN.
///
/// ONE SHARED BUTTON (Button Root). It sits in one place and its caption changes - "Enter
/// Assembly Station" while outside, "Exit Assembly Station" while inside. The thumb learns one
/// position and the label carries the meaning.
///
/// SEPARATE BUTTONS (Enter Button Root + Exit Button Root). Two buttons in two places, only
/// ever one on screen at a time. Worth it when the two actions want different corners - Enter
/// under the right thumb where the player is walking, Exit somewhere it cannot be hit by
/// accident while working.
///
/// Fill in either set, or both while you are deciding.
/// </summary>
public class StationInteractionRelay : MonoBehaviour
{
    [Header("One shared button")]
    [Tooltip("A single button whose caption changes between Enter and Exit. Leave empty if you " +
             "are using two separate buttons below.")]
    [SerializeField] private GameObject buttonRoot;
    [SerializeField] private TMP_Text buttonLabel; // optional

    [Header("Or two separate buttons")]
    [Tooltip("Shown only when the player is NEAR a station and not inside it.")]
    [SerializeField] private GameObject enterButtonRoot;

    [Tooltip("Optional caption for the Enter button.")]
    [SerializeField] private TMP_Text enterButtonLabel;

    [Tooltip("Shown only when the player is INSIDE a station.\n\n" +
             "This is the one that matters on a phone. If it is missing, or hidden, the learner " +
             "is stuck in the station.")]
    [SerializeField] private GameObject exitButtonRoot;

    [Tooltip("Optional caption for the Exit button.")]
    [SerializeField] private TMP_Text exitButtonLabel;

    [Header("Labels")]
    [Tooltip("Write the station's own name into the captions - \"Enter Assembly Station\" rather " +
             "than just \"Enter\".\n\n" +
             "Turn this OFF when your buttons carry a fixed word or an icon you do not want " +
             "overwritten. With two separate buttons that is reasonable: their position already " +
             "says which is which.")]
    [SerializeField] private bool updateLabels = true;

    [Header("Diagnostics")]
    [Tooltip("Say when a button shows and hides, and name the reason when it cannot appear. " +
             "Leave ON until the button behaves - a missing button gives you nothing to search for.")]
    [SerializeField] private bool logVisibility = true;

    [Tooltip("At startup, check whether any assigned button is parented under anything a station " +
             "hides while its view is up. That is the one fault that makes the exit button vanish " +
             "precisely when it is needed, and it cannot be seen in the Inspector.")]
    [SerializeField] private bool auditParentsAtStart = true;

    bool lastSharedShown, lastEnterShown, lastExitShown;
    bool warnedAboutHiddenParent;
    bool warnedNothingAssigned;
    float nextScan;
    StationCameraSwitch[] known = new StationCameraSwitch[0];

    // ==================================================================

    void Start()
    {
        Rescan();
        if (auditParentsAtStart) AuditButtonParents();
    }

    void Update()
    {
        if (buttonRoot == null && enterButtonRoot == null && exitButtonRoot == null)
        {
            if (!warnedNothingAssigned)
            {
                warnedNothingAssigned = true;
                Debug.LogWarning("[StationInteractionRelay] No button is assigned - not Button Root, " +
                                 "not Enter Button Root, not Exit Button Root. The Enter/Exit button " +
                                 "can never show. On a touch build that means no way into or out of " +
                                 "any station.\n\nFill in EITHER Button Root (one button whose " +
                                 "caption changes) OR Enter Button Root and Exit Button Root.", this);
            }
            return;
        }

        if (Time.unscaledTime >= nextScan) Rescan();

        StationCameraSwitch station = Target();
        bool near = station != null;
        bool inside = near && station.IsUsingStationCamera;

        Apply(buttonRoot, near, ref lastSharedShown, "Enter/Exit button", station, inside);
        Apply(enterButtonRoot, near && !inside, ref lastEnterShown, "Enter button", station, inside);
        Apply(exitButtonRoot, inside, ref lastExitShown, "Exit button", station, inside);

        if (near && updateLabels)
        {
            string label = station.DisplayName;

            if (buttonLabel != null)
                buttonLabel.text = inside ? $"Exit {label}" : $"Enter {label}";

            if (enterButtonLabel != null) enterButtonLabel.text = $"Enter {label}";
            if (exitButtonLabel != null) exitButtonLabel.text = $"Exit {label}";
        }
    }

    /// <summary>
    /// Shows or hides one root, and reports the one failure the Inspector cannot show: switched
    /// on and still not drawn.
    /// </summary>
    void Apply(GameObject root, bool shouldShow, ref bool lastShown, string what,
               StationCameraSwitch station, bool inside)
    {
        if (root == null) return;

        if (root.activeSelf != shouldShow)
        {
            root.SetActive(shouldShow);

            if (logVisibility && shouldShow != lastShown)
                Debug.Log("[StationInteractionRelay] " + what + " " +
                          (shouldShow ? "SHOWN for '" + station.name + "'"
                                      : "hidden - no station near or active"), this);

            lastShown = shouldShow;
        }

        // Switched on, and still not drawn. The only way that happens is a parent being off, and
        // it is worth an error rather than a warning: the player is inside a station with no way
        // out, which on a phone is the end of the run.
        if (shouldShow && !root.activeInHierarchy && !warnedAboutHiddenParent)
        {
            warnedAboutHiddenParent = true;
            Debug.LogError("[StationInteractionRelay] The " + what + " '" + root.name + "' is " +
                           "switched ON but still not drawn, because " + NameHiddenAncestor(root) +
                           " is switched off." +
                           (station != null && inside
                               ? " The player is INSIDE '" + station.name + "' right now and has no " +
                                 "way out on a touch device."
                               : "") +
                           "\n\nMove the button out from under that object - give it its own small " +
                           "canvas that nothing disables - and this cannot happen again.", this);
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

    // ------------------------------------------------------------------ what the buttons call

    /// <summary>Wire this to the SHARED Enter/Exit button's OnClick. Enters if outside, exits
    /// if inside.</summary>
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

    /// <summary>
    /// Wire this to the ENTER button's OnClick. Does nothing if the player is already inside.
    ///
    /// The check is not redundant. HandleInteractPressed TOGGLES, so an Enter button that
    /// reached the player's thumb while it was still on screen - a tap landing in the same frame
    /// the state flipped, or a button left visible by something else - would throw them straight
    /// back OUT of the station they just entered. A button named Enter must only ever enter.
    /// </summary>
    public void RequestEnter()
    {
        StationCameraSwitch station = Target();
        if (station == null || station.IsUsingStationCamera) return;

        station.HandleInteractPressed();
    }

    /// <summary>Wire this to the EXIT button's OnClick. Does nothing if the player is not
    /// inside a station.</summary>
    public void RequestExit()
    {
        StationCameraSwitch station = Target();
        if (station == null || !station.IsUsingStationCamera) return;

        station.HandleInteractPressed();
    }

    // ------------------------------------------------------------------ self-reporting

    /// <summary>
    /// Warns when a button lives under something a station switches off.
    ///
    /// Checked at startup rather than waiting for it to happen, because when it does happen the
    /// player is already inside the station and already stuck - and the Console is the last place
    /// anybody looks at that point.
    /// </summary>
    void AuditButtonParents()
    {
        StationCameraSwitch[] stations = Object.FindObjectsByType<StationCameraSwitch>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        List<string> clashes = new List<string>();

        AuditOne(buttonRoot, "Enter/Exit button", stations, clashes);
        AuditOne(enterButtonRoot, "Enter button", stations, clashes);
        AuditOne(exitButtonRoot, "Exit button", stations, clashes);

        if (clashes.Count == 0)
        {
            if (logVisibility)
                Debug.Log("[StationInteractionRelay] " + DescribeLayout() + " covering " +
                          stations.Length + " station(s), and nothing hides them while a station " +
                          "view is up.", this);
            return;
        }

        string msg = "[StationInteractionRelay] A station button is parented under something that " +
                     "gets switched off while a station view is up, so it will DISAPPEAR at " +
                     "exactly the moment it is the only way out:";
        foreach (string c in clashes) msg += "\n  - " + c;
        msg += "\n\nOn a keyboard this is survivable - E still works. On a phone the learner is " +
               "stuck in the station.\n\nFix: give the button its own canvas, outside the touch " +
               "controls, and leave that canvas out of every station's Disable On Station View list.";

        Debug.LogError(msg, this);
    }

    void AuditOne(GameObject root, string what, StationCameraSwitch[] stations, List<string> clashes)
    {
        if (root == null) return;

        HashSet<Transform> ancestors = new HashSet<Transform>();
        Transform t = root.transform;
        while (t != null) { ancestors.Add(t); t = t.parent; }

        foreach (StationCameraSwitch s in stations)
        {
            if (s == null || s.disableOnStationView == null) continue;

            foreach (GameObject hidden in s.disableOnStationView)
            {
                if (hidden == null || !ancestors.Contains(hidden.transform)) continue;
                clashes.Add(what + " '" + root.name + "': '" + s.name + "' hides '" + hidden.name + "'");
            }
        }
    }

    string DescribeLayout()
    {
        bool shared = buttonRoot != null;
        bool split = enterButtonRoot != null || exitButtonRoot != null;

        if (shared && split) return "One shared button AND a separate Enter/Exit pair";
        if (shared) return "One shared Enter/Exit button '" + buttonRoot.name + "'";

        string e = enterButtonRoot != null ? "'" + enterButtonRoot.name + "'" : "NONE";
        string x = exitButtonRoot != null ? "'" + exitButtonRoot.name + "'" : "NONE";
        return "Separate buttons - Enter " + e + ", Exit " + x;
    }

    string NameHiddenAncestor(GameObject root)
    {
        Transform t = root != null ? root.transform.parent : null;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) return "its parent '" + t.name + "'";
            t = t.parent;
        }
        return "one of its parents";
    }

    /// <summary>Prints which station the button would operate, and why each button is or is not
    /// showing.</summary>
    [ContextMenu("Why is the station button hidden?")]
    public void LogState()
    {
        StationCameraSwitch target = Target();
        StationCameraSwitch active = null;
        foreach (StationCameraSwitch s in known)
            if (s != null && s.IsUsingStationCamera) { active = s; break; }

        Debug.LogWarning("[StationInteractionRelay]" +
            "\n    layout             : " + DescribeLayout() +
            "\n    Button Root        : " + Describe(buttonRoot) +
            "\n    Enter Button Root  : " + Describe(enterButtonRoot) +
            "\n    Exit Button Root   : " + Describe(exitButtonRoot) +
            "\n    station in view    : " + (active != null ? "'" + active.name + "'" : "none") +
            "\n    NearbyOrActive     : " + (StationCameraSwitch.NearbyOrActive != null
                ? "'" + StationCameraSwitch.NearbyOrActive.name + "'" : "null") +
            "\n    button would operate: " + (target != null ? "'" + target.name + "'" : "nothing"),
            this);
    }

    string Describe(GameObject root)
    {
        if (root == null) return "not assigned";

        string line = "'" + root.name + "'  activeSelf=" + root.activeSelf +
                      "  activeInHierarchy=" + root.activeInHierarchy;

        if (root.activeSelf && !root.activeInHierarchy)
            line += "   BLOCKED BY " + NameHiddenAncestor(root);

        var button = root.GetComponentInChildren<UnityEngine.UI.Button>(true);
        if (button == null)
            line += "   <-- no Button on it or under it: it will show and hide and do nothing when tapped";
        else if (button.onClick.GetPersistentEventCount() == 0)
            line += "   <-- its Button has an EMPTY On Click";

        return line;
    }
}