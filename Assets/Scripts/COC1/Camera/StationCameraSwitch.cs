using UnityEngine;
using CSSSimulation.Networking;

public class StationCameraSwitch : MonoBehaviour
{
    /// <summary>
    /// Whichever station the player is currently near (about to enter) OR
    /// currently inside (about to exit). A single mobile "Enter/Exit" button
    /// can route through this static reference to work correctly for every
    /// station in the game - assembly, bootable USB, OS install, etc -
    /// without needing a separate button per station.
    /// </summary>
    public static StationCameraSwitch NearbyOrActive { get; private set; }

    [Header("Optional display name (for a mobile button label, e.g. \"Enter Assembly Station\")")]
    [SerializeField] private string displayName = "Station";
    public string DisplayName => displayName;

    [Header("Cameras")]
    public Camera playerCamera;
    public Camera stationCamera;

    [Header("UI/Objects to toggle when entering station view")]
    public GameObject[] enableOnStationView;   // e.g. hotbar, OS install UI
    public GameObject[] disableOnStationView;  // e.g. ssdPlane, waypoint arrow

    [Header("Entry Gate (optional)")]
    [Tooltip("If set, the player can't enter this station AT ALL - via E or the mobile button - until every listed task is complete. This is the main 'do things in order' control: gate the USB creator station on assemble_pc + disassemble_pc, gate the target PC station on create_bootable_usb, etc. Leave empty for stations that are available from the start.")]
    [SerializeField] private TaskGate entryGate = new TaskGate();

    [Header("Task Gate (optional, separate from Entry Gate above)")]
    [Tooltip("If set, this station is treated as the destination for a task/objective. " +
             "Once ALL of these task IDs are complete (see TaskManager), the station is " +
             "locked out: pressing E no longer enters it, and a debug log is printed instead. " +
             "Leave empty for stations that aren't tied to a task (e.g. purely cosmetic zoom-ins).")]
    [SerializeField] private string[] gatedTaskIds;

    private bool playerNearby = false;
    private bool usingStationCamera = false;

    public bool IsUsingStationCamera => usingStationCamera;

    /// <summary>Forces this station back to the player camera if it's currently active  used
    /// e.g. when the gated task completes while the player is still standing inside the station
    /// view, so they aren't left staring at a station they can no longer re-enter.</summary>
    public void ForceReturnToPlayerCamera()
    {
        if (usingStationCamera) ToggleCamera(false);
    }

    void Update()
    {
        if (playerNearby && Input.GetKeyDown(KeyCode.E))
        {
            HandleInteractPressed();
        }
    }

    /// <summary>
    /// Runs the enter/exit logic for this station. Called from Update() for
    /// the PC keyboard (E key), and from StationInteractionRelay for the
    /// mobile on-screen button - both paths funnel through here so the
    /// gating, waypoint-clearing, etc. only needs to exist in one place.
    /// </summary>
    public void HandleInteractPressed()
    {
        if (!playerNearby) return;

        bool enteringStation = !usingStationCamera;

        if (enteringStation && !entryGate.IsUnlocked())
        {
            Debug.Log($"[StationCameraSwitch] '{name}': entry blocked, prerequisite task(s) not complete yet.", this);
            entryGate.ShowLockedMessage();
            return;
        }

        if (enteringStation && IsGatedTaskComplete())
        {
            Debug.Log($"[StationCameraSwitch] '{name}': You've already done this step", this);
            return;
        }

        ToggleCamera(enteringStation);

        if (enteringStation)
            OnEnteredStation();
    }

    /// <summary>True once every task ID listed in gatedTaskIds is complete. False (never gated)
    /// if no task IDs were assigned, so ungated stations behave exactly as before.</summary>
    private bool IsGatedTaskComplete()
    {
        if (gatedTaskIds == null || gatedTaskIds.Length == 0) return false;
        if (TaskManager.Instance == null) return false;

        foreach (var taskId in gatedTaskIds)
        {
            if (string.IsNullOrEmpty(taskId)) continue;
            if (!TaskManager.Instance.IsTaskComplete(taskId)) return false;
        }
        return true;
    }

    /// <summary>Called the first time the player actually enters a gated station while its
    /// task(s) are still incomplete  i.e. they've "found" the objective, so the waypoint
    /// arrow (if it's currently pointing here) can be cleared. Stations with no gatedTaskIds
    /// configured are assumed to be outside the guided task flow and don't touch the arrow.</summary>
    private void OnEnteredStation()
    {
        if (gatedTaskIds == null || gatedTaskIds.Length == 0) return;
        if (WaypointIndicator.Instance != null)
            WaypointIndicator.Instance.ClearTarget();
    }

    private void ToggleCamera(bool useStation)
    {
        usingStationCamera = useStation;
        playerCamera.gameObject.SetActive(!useStation);
        stationCamera.gameObject.SetActive(useStation);

        // Hide the task arrow for as long as the close-up is open, and bring it back on the way out.
        //
        // Inside a station the player is looking through a fixed camera at a workbench; a world
        // arrow still hovering over something across the room is pure noise, and on the screen-space
        // arrow it is worse - it swings around pointing at a place the player cannot walk to.
        //
        // Null-guarded, so a scene with no TaskWaypointArrow - COC3 and COC4 today - is unaffected.
        // In an assessment run the arrow refuses to come back, which is deliberate: it is locked off
        // for the whole run and one station closing must not undo that.
        if (TaskWaypointArrow.Instance != null)
            TaskWaypointArrow.Instance.SetSuppressed(useStation);

        Cursor.lockState = useStation ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = useStation;

        ApplyStationViewObjects(useStation);
    }

    // ------------------------------------------------------------------
    // Enable/Disable On Station View
    // ------------------------------------------------------------------

    /// <summary>What each listed object was doing when the close-up opened, so leaving can put it
    /// back to that rather than to an assumption.</summary>
    private bool[] enableWasActive;
    private bool[] disableWasActive;
    private bool statesCaptured;

    /// <summary>
    /// Turns the listed objects on and off with the station view, and PUTS THEM BACK on the way out.
    ///
    /// It used to be two lines:
    ///
    ///     foreach (var obj in enableOnStationView)  obj.SetActive(useStation);
    ///     foreach (var obj in disableOnStationView) obj.SetActive(!useStation);
    ///
    /// which reads as symmetric and is not. Leaving a station did not restore anything - it ASSERTED
    /// a state it had never observed, switching every Disable On Station View object ON whether or
    /// not it had been on beforehand. Anything legitimately hidden for other reasons came back the
    /// first time the learner stepped into a close-up and out again: the assessment HUD appearing
    /// mid Story Mode is exactly that, and the mobile controls on a desktop build would be next.
    ///
    /// So the states are captured on the way in and restored on the way out. An object hidden
    /// before the station stays hidden after it; one that was visible comes back. The station no
    /// longer has an opinion about anything except while the learner is standing in it - which is
    /// the only window it knows anything about.
    /// </summary>
    private void ApplyStationViewObjects(bool useStation)
    {
        if (useStation)
        {
            CaptureStationViewStates();

            SetAll(enableOnStationView, true);
            SetAll(disableOnStationView, false);
            return;
        }

        // Never entered - initial setup, or a forced exit before any entry. Nothing was captured,
        // so there is nothing to restore and the safest thing is to leave every object exactly as
        // the scene author left it.
        if (!statesCaptured) return;

        RestoreAll(enableOnStationView, enableWasActive);
        RestoreAll(disableOnStationView, disableWasActive);
        statesCaptured = false;
    }

    private void CaptureStationViewStates()
    {
        // Only the FIRST call of an entry captures. ToggleCamera can be called again with the same
        // value - a re-entry, a settings apply - and a second capture would record the states this
        // method itself just imposed, which is how a restore ends up restoring nothing.
        if (statesCaptured) return;

        enableWasActive = Snapshot(enableOnStationView);
        disableWasActive = Snapshot(disableOnStationView);
        statesCaptured = true;
    }

    private static bool[] Snapshot(GameObject[] objects)
    {
        if (objects == null) return new bool[0];

        var states = new bool[objects.Length];
        for (int i = 0; i < objects.Length; i++)
            states[i] = objects[i] != null && objects[i].activeSelf;

        return states;
    }

    /// <summary>Null-guarded, unlike the two lines this replaced. An empty slot in either array
    /// threw inside ToggleCamera, which aborted the switch halfway and left the learner in a
    /// half-entered station with a NullReferenceException and no camera.</summary>
    private static void SetAll(GameObject[] objects, bool active)
    {
        if (objects == null) return;

        foreach (var obj in objects)
            if (obj != null && obj.activeSelf != active) obj.SetActive(active);
    }

    private static void RestoreAll(GameObject[] objects, bool[] states)
    {
        if (objects == null || states == null) return;

        for (int i = 0; i < objects.Length && i < states.Length; i++)
            if (objects[i] != null && objects[i].activeSelf != states[i])
                objects[i].SetActive(states[i]);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = true;
            NearbyOrActive = this;
            Debug.Log($"[StationCameraSwitch] ENTER trigger: '{name}' - NearbyOrActive is now '{NearbyOrActive.name}'", this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = false;
            if (usingStationCamera) ToggleCamera(false);
            if (NearbyOrActive == this)
            {
                NearbyOrActive = null;
                Debug.Log($"[StationCameraSwitch] EXIT trigger: '{name}' - NearbyOrActive cleared to null", this);
            }
            else
            {
                Debug.Log($"[StationCameraSwitch] EXIT trigger: '{name}' - but NearbyOrActive was '{(NearbyOrActive != null ? NearbyOrActive.name : "null")}', not this one, so left untouched", this);
            }
        }
    }
}