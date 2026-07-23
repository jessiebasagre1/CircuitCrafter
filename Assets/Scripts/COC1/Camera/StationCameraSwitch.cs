using UnityEngine;

public class StationCameraSwitch : MonoBehaviour
{
    [Header("Cameras")]
    public Camera playerCamera;
    public Camera stationCamera;

    [Header("UI/Objects to toggle when entering station view")]
    public GameObject[] enableOnStationView;   // e.g. hotbar, OS install UI
    public GameObject[] disableOnStationView;  // e.g. ssdPlane, waypoint arrow

    private bool playerNearby = false;
    private bool usingStationCamera = false;

    void Update()
    {
        if (playerNearby && Input.GetKeyDown(KeyCode.E))
        {
            ToggleCamera(!usingStationCamera);
        }
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
        if (other.CompareTag("Player")) playerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = false;
            if (usingStationCamera) ToggleCamera(false);
        }
    }
}