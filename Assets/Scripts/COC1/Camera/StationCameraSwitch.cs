using UnityEngine;

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

    [Header("Task Gate (optional)")]
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

        Cursor.lockState = useStation ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = useStation;

        foreach (var obj in enableOnStationView) obj.SetActive(useStation);
        foreach (var obj in disableOnStationView) obj.SetActive(!useStation);
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