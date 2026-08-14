using UnityEngine;
using TMPro;

/// <summary>
/// A single on-screen "Enter/Exit Station" button works for EVERY station in
/// the game (assembly, bootable USB, OS install, etc) by routing through
/// StationCameraSwitch.NearbyOrActive - the static reference that always
/// points at whichever station the player is currently near or inside.
///
/// Setup:
///   1. Create ONE UI Button on your mobile controls canvas (not per station).
///   2. Add this component to it (or anywhere else - it doesn't need to be
///      on the button itself, just needs the button reference assigned).
///   3. Wire the Button's OnClick -> this component's RequestInteract().
///   4. Optionally assign a TMP_Text (child of the button, or the button's
///      own label) to buttonLabel for a dynamic "Enter Assembly Station" /
///      "Exit Assembly Station" caption using each station's DisplayName.
///
/// The button auto shows/hides every frame based on whether the player is
/// currently near or inside any station - no per-station wiring needed.
/// </summary>
public class StationInteractionRelay : MonoBehaviour
{
    [SerializeField] private GameObject buttonRoot;
    [SerializeField] private TMP_Text buttonLabel; // optional

    void Update()
    {
        if (buttonRoot == null)
        {
            Debug.LogWarning("[StationInteractionRelay] buttonRoot is NOT assigned in the Inspector - button can never show.");
            return;
        }

        var station = StationCameraSwitch.NearbyOrActive;
        bool shouldShow = station != null;

        if (buttonRoot.activeSelf != shouldShow)
        {
            Debug.Log($"[StationInteractionRelay] Changing buttonRoot '{buttonRoot.name}' active state to {shouldShow} (station: {(station != null ? station.name : "none")})");
            buttonRoot.SetActive(shouldShow);
        }

        if (shouldShow && buttonLabel != null)
        {
            buttonLabel.text = station.IsUsingStationCamera
                ? $"Exit {station.DisplayName}"
                : $"Enter {station.DisplayName}";
        }
    }

    /// <summary>Wire this to the mobile Enter/Exit Station button's OnClick.</summary>
    public void RequestInteract()
    {
        StationCameraSwitch.NearbyOrActive?.HandleInteractPressed();
    }
}