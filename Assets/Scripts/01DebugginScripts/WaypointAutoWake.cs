using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using CSSSimulation.Networking;

/// <summary>
/// Keeps the waypoint arrow's GameObject switched on, and says who is switching it off.
///
/// THERE IS NOTHING TO ATTACH AND NOTHING TO WIRE. Put this file anywhere in the project.
///
/// WHY IT CANNOT LIVE ON THE OBJECT IT FIXES
/// Unity does not run Awake, OnEnable or Start on an inactive GameObject, so a component on a
/// switched-off object has no code running - it cannot wake itself and cannot report that it is
/// asleep. Something OUTSIDE has to look. MobileControlsVisibility in this project splits itself the
/// same way for the same reason.
///
/// WHY IT CHECKS THREE TIMES INSTEAD OF ONCE - THE PART THAT TOOK LONGEST TO FIND
/// RuntimeInitializeOnLoadMethod(AfterSceneLoad) runs after every Awake in the scene but BEFORE any
/// Start. TaskWaypointArrow carries [DefaultExecutionOrder(300)], so its own Start runs LAST of all.
/// Between those two moments sits every ordinary Start in the scene - and a single SetActive(false)
/// in any one of them switches the object off before the Start phase ever reaches order 300.
///
/// The arrow's Start therefore never runs, which means:
///   - no setup line in the Console, so it looks like the component is not there at all
///   - no validation, so nothing reports the real problem
///   - LateUpdate never runs, so the arrow is never placed
/// while a check made at AfterSceneLoad - and an Editor audit of the saved scene - both report the
/// object as perfectly active, because at the moment they looked, it was.
///
/// So: check at AfterSceneLoad, again at the end of the first frame once every Start has run, and
/// once more a frame later for anything on a delay. The second pass is the one that matters.
///
/// IT DOES NOT OVERRIDE THE ASSESSMENT LOCK. Waking the object is not the same as showing the arrow.
/// TaskWaypointArrow.Start still calls LockOffForAssessment when AssessmentMode is active, so a
/// graded run stays unguided.
///
/// IT WILL NOT SWITCH ON A PARENT. If an ancestor is the thing that is off it says so by name and
/// stops - StepUI in COC 3 is switched off on purpose whenever the learner is walking around, and
/// forcing something like that back on would break the module to fix a marker.
/// </summary>
public static class WaypointAutoWake
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        Begin();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Begin(); }

    private static void Begin()
    {
        Wake("before any Start has run");
        Runner.Get().Recheck();
    }

    // ==================================================================

    private static void Wake(string when)
    {
        TaskWaypointArrow[] arrows = Object.FindObjectsByType<TaskWaypointArrow>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        // ONE LINE EVERY PASS, WHETHER OR NOT ANYTHING WAS WRONG.
        //
        // Every other check in this project printed only when it had a complaint, which sounds tidy
        // and is exactly why an empty Console was so uninformative: "the file is not in the project",
        // "it ran and found nothing" and "it ran and all was well" looked identical. A diagnostic
        // that is silent when it works cannot be told apart from one that is not there.
        var sb = new System.Text.StringBuilder();
        sb.Append($"[WaypointAutoWake] Pass '{when}' on scene '{SceneManager.GetActiveScene().name}': " +
                  $"{arrows.Length} TaskWaypointArrow(s) found, inactive included.");

        if (arrows.Length == 0)
        {
            sb.Append("  <- NONE. Either this scene has no TaskWaypointArrow, or the one you are " +
                      "looking at is in a different scene.");
            Debug.Log(sb.ToString());
            return;
        }

        bool changedSomething = false;

        foreach (TaskWaypointArrow arrow in arrows)
        {
            if (arrow == null) continue;
            GameObject go = arrow.gameObject;

            // isActiveAndEnabled is the ONE boolean that decides whether Start and LateUpdate run.
            // The other three are printed beside it because each fails differently and they are
            // indistinguishable in the Hierarchy at a glance.
            sb.AppendLine();
            sb.Append($"    '{PathOf(go.transform)}'  ownBox={go.activeSelf}  " +
                      $"activeInHierarchy={go.activeInHierarchy}  component={arrow.enabled}  " +
                      $"willRun={arrow.isActiveAndEnabled}");

            Transform blocked = FirstInactiveAncestor(go.transform);
            if (blocked != null)
            {
                sb.AppendLine();
                sb.Append($"      CANNOT WAKE: its ancestor '{PathOf(blocked)}' is switched off, so " +
                          "switching this object on would change nothing - activeInHierarchy stays " +
                          "false while any ancestor is off. Tick that ancestor on yourself; this " +
                          "does not touch parents, because one being off is usually deliberate.");
                Debug.LogError(sb.ToString(), blocked.gameObject);
                return;
            }

            if (!go.activeSelf)
            {
                go.SetActive(true);
                changedSomething = true;
                sb.AppendLine();
                sb.Append("      SWITCHED IT BACK ON. It was inactive, which stops the arrow running " +
                          "at all.");

                // The whole point of the second pass. Something in a Start() did this, and naming
                // the window is most of naming the culprit.
                if (when != "before any Start has run")
                    sb.Append("\n      It was ACTIVE before the Start phase and OFF after it, so a " +
                              "script's Start() switched it off. TaskWaypointArrow runs at execution " +
                              "order 300 - last - so its own Start was skipped. To catch the exact " +
                              "caller, put WaypointHeartbeat on this object: its OnDisable prints a " +
                              "stack trace naming whatever called SetActive(false).");
            }

            // Separate box, separate failure. An unticked COMPONENT on an active object still runs
            // Awake - so it registers and looks present - while Start and LateUpdate never run.
            if (!arrow.enabled)
            {
                arrow.enabled = true;
                changedSomething = true;
                sb.AppendLine();
                sb.Append("      RE-ENABLED the TaskWaypointArrow component.");
            }

            COC3WaypointBridge bridge = go.GetComponent<COC3WaypointBridge>();
            if (bridge != null && !bridge.enabled)
            {
                bridge.enabled = true;
                changedSomething = true;
                sb.AppendLine();
                sb.Append("      RE-ENABLED the COC3WaypointBridge component.");
            }
        }

        if (changedSomething) Debug.LogWarning(sb.ToString());
        else Debug.Log(sb.ToString());
    }

    private static Transform FirstInactiveAncestor(Transform t)
    {
        Transform p = t.parent;
        while (p != null)
        {
            if (!p.gameObject.activeSelf) return p;
            p = p.parent;
        }
        return null;
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    // ==================================================================
    // A coroutine has to live on a MonoBehaviour, and there is no scene object to hang it on -
    // hanging it on the waypoint itself would defeat the purpose, since that is the object that
    // keeps going inactive.
    // ==================================================================

    private class Runner : MonoBehaviour
    {
        private static Runner instance;

        public static Runner Get()
        {
            if (instance != null) return instance;

            var go = new GameObject("~WaypointAutoWake");
            go.hideFlags = HideFlags.HideAndDontSave;   // stays out of the Hierarchy
            Object.DontDestroyOnLoad(go);

            instance = go.AddComponent<Runner>();
            return instance;
        }

        public void Recheck()
        {
            StopAllCoroutines();
            StartCoroutine(Passes());
        }

        private IEnumerator Passes()
        {
            // End of the first frame: every Start in the scene has now run, including any that
            // switched the object off. This is the pass that catches the real culprit.
            yield return null;
            Wake("after the first frame, once every Start has run");

            // One more, for anything that acts on a short delay or from a coroutine.
            yield return null;
            yield return null;
            Wake("after a few frames");
        }
    }
}