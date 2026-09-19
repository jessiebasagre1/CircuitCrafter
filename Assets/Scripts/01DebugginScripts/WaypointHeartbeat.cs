using UnityEngine;
using CSSSimulation.Networking;

/// <summary>
/// Answers "is anything on this object running, and if not, who switched it off".
///
/// ATTACH THIS TO: Managers/TaskWaypoint - the same object as TaskWaypointArrow and
/// COC3WaypointBridge. Delete it once the waypoint works.
///
/// WHY IT IS SEPARATE FROM EVERY OTHER CHECK
/// Every diagnostic so far lived inside the thing being diagnosed, and that is exactly the
/// arrangement that cannot report the failure we have: a component that never runs cannot say it
/// never ran. The Editor audit got past that by running outside Play - but it reads the scene as
/// SAVED, so an object that is ticked on in the Hierarchy and switched off a frame into Play audits
/// perfectly clean while never running at all.
///
/// This sits in the gap between those two. It runs INSIDE Play, on the object in question, and its
/// most important message is the one it prints from OnDisable - the moment something deactivates it.
///
/// THE SUSPECT IT WAS WRITTEN FOR
/// COC3StationController.Awake calls SetStepViewActive(false, null), which does:
///
///     foreach (var obj in mobileStationControls) obj.SetActive(active);   // active == false
///
/// Anything listed in Mobile Station Controls is therefore switched OFF on the first frame of every
/// run, before it has done anything. If TaskWaypoint is in that array - or is a child of something
/// in it - then ticking it on in the Hierarchy works right up until you press Play, which is
/// precisely the symptom that has been so hard to pin down. So this names that array directly.
/// </summary>
[DefaultExecutionOrder(1000)]   // last, so the controller's Awake has already had its say
public class WaypointHeartbeat : MonoBehaviour
{
    [Tooltip("Seconds to watch before printing the all-clear, if nothing switches this object off.")]
    [SerializeField] private float reportAfter = 2f;

    private bool reported;
    private float age;

    private void Awake()
    {
        Debug.Log($"[WaypointHeartbeat] Awake ran on '{name}', frame {Time.frameCount}. " +
                  "So this object was active at the start of the run.", this);
    }

    private void Start()
    {
        Debug.Log($"[WaypointHeartbeat] Start ran, frame {Time.frameCount}. " +
                  "Anything on this object that did NOT print its own setup line by now is either " +
                  "unticked as a component, or throwing in its own Awake.", this);
    }

    private bool quitting;

    // OnDisable also fires when Play mode ends and when the object is destroyed, and an alarming
    // red error at that moment is just noise. This tells the two apart.
    private void OnApplicationQuit() { quitting = true; }
    private void OnDestroy() { quitting = true; }

    private void OnDisable()
    {
        if (quitting) return;

        // THE MESSAGE THIS WHOLE FILE EXISTS FOR - AND THE USEFUL PART IS THE STACK TRACE.
        //
        // OnDisable is called SYNCHRONOUSLY from inside SetActive(false). That means the call that
        // switched this object off is still on the stack when this line runs, so Unity's own trace
        // under the message names it - read the lines BELOW 'UnityEngine.GameObject:SetActive (bool)'
        // and the script and line number that did it are right there.
        //
        // That is worth more than any list of suspects this file could hard-code, because it is the
        // actual caller rather than a guess.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[WaypointHeartbeat] '{name}' was SWITCHED OFF on frame {Time.frameCount}.");
        sb.AppendLine("Everything on it stops here: TaskWaypointArrow's Start never runs, its " +
                      "LateUpdate never runs, and no arrow is ever placed.");
        sb.AppendLine();
        sb.AppendLine("WHO DID IT: read the stack trace printed below this message. OnDisable runs " +
                      "inside the SetActive call, so the culprit is still on the stack - look for the " +
                      "lines under 'UnityEngine.GameObject:SetActive (bool)'.");
        sb.AppendLine();

        // Own box or a parent's? Different fix, and indistinguishable in the Hierarchy at a glance.
        sb.AppendLine($"    this object's own box    : {(gameObject.activeSelf ? "still TICKED - so a PARENT was switched off, not this object" : "now unticked - this object itself was switched off")}");

        Transform p = transform.parent;
        while (p != null)
        {
            if (!p.gameObject.activeSelf)
            {
                sb.AppendLine($"    the parent switched off  : '{p.name}'");
                break;
            }
            p = p.parent;
        }

        sb.AppendLine(NameTheCulprit());
        Debug.LogError(sb.ToString(), this);
    }

    private void LateUpdate()
    {
        if (reported) return;

        age += Time.unscaledDeltaTime;
        if (age < reportAfter) return;
        reported = true;

        TaskWaypointArrow arrow = GetComponent<TaskWaypointArrow>();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[WaypointHeartbeat] Still alive after {reportAfter:0.#}s - nothing switched " +
                      "this object off, so the problem is not deactivation.");
        sb.AppendLine($"    TaskWaypointArrow here   : {(arrow != null ? "yes" : "NO")}");

        if (arrow != null)
        {
            sb.AppendLine($"    its component ticked     : {arrow.enabled}");
            sb.AppendLine($"    registered as Instance   : {(TaskWaypointArrow.Instance == arrow ? "yes" : TaskWaypointArrow.Instance == null ? "NO - nothing registered" : "no - a DIFFERENT arrow holds it")}");
            sb.AppendLine($"    active step index        : {arrow.ActiveIndex}" +
                          (arrow.ActiveIndex < 0 ? "   <- -1 means every step reads as finished" : ""));
            sb.AppendLine($"    World Arrow              : {(arrow.worldArrow != null ? $"'{arrow.worldArrow.name}', currently {(arrow.worldArrow.gameObject.activeSelf ? "SHOWN" : "hidden")}" : "not assigned")}");
            sb.AppendLine($"    Assessment mode          : {AssessmentMode.IsActive}" +
                          (AssessmentMode.IsActive ? "   <- the arrow is locked off for the whole run, on purpose" : ""));

            if (arrow.worldArrow != null && arrow.worldArrow.gameObject.activeSelf)
                sb.AppendLine("    The arrow object IS switched on, so it is being placed - if you " +
                              "cannot see it, it is somewhere you are not looking. Select it in the " +
                              "Hierarchy while playing and press F in the Scene view.");
        }

        Debug.LogWarning(sb.ToString(), this);
    }

    /// <summary>
    /// Looks for this object - or any ancestor of it - in the two arrays COC3StationController
    /// switches off, and names the one that matches. Guessing which array is tedious by hand
    /// because both are usually several entries long and the names all look plausible.
    /// </summary>
    private string NameTheCulprit()
    {
        COC3StationController c = COC3StationController.Instance;
        if (c == null)
            return "COC3StationController has not registered, so this was something else. Search the " +
                   "project for SetActive calls on this object.";

        string hit = Find(c.mobileStationControls, "Mobile Station Controls");
        if (hit != null)
            return hit + "\nThat array is switched OFF whenever the learner is NOT at a machine - " +
                   "including in Awake, on frame one. Remove this object from it: the waypoint is " +
                   "meant to be visible while WALKING, which is the exact opposite of what that " +
                   "array is for.";

        hit = Find(c.mobileControlsToHide, "Mobile Controls To Hide");
        if (hit != null)
            return hit + "\nThat array is switched off while the learner IS at a machine. The " +
                   "waypoint hides itself in that situation already, through Hide While Using A " +
                   "Machine, so it does not belong here either - remove it.";

        return "    It is NOT in COC3StationController's Mobile Station Controls or Mobile Controls\n" +
               "    To Hide, so something else did it. The stack trace above names what.";
    }

    private string Find(GameObject[] list, string arrayName)
    {
        if (list == null) return null;

        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null) continue;

            if (list[i] == gameObject)
                return $"FOUND IT: this object is element {i} of COC3StationController's '{arrayName}'.";

            // An ancestor counts too - deactivating a parent takes this object with it, and that is
            // harder to spot by eye than the object being listed directly.
            if (transform.IsChildOf(list[i].transform))
                return $"FOUND IT: '{list[i].name}' is element {i} of COC3StationController's " +
                       $"'{arrayName}', and this object is a child of it.";
        }
        return null;
    }
}
