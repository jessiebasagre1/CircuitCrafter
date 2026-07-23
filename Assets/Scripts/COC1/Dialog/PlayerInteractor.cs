using UnityEngine;

/// <summary>
/// Detects clicks on InteractableObjects and calls Interact() on them.
/// Attach to your main camera (or an object holding a camera reference).
/// Works for both a fixed/orbit camera (point-and-click) and first-person setups.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private float maxInteractDistance = 10f;
    [SerializeField] private LayerMask interactableLayer = ~0;

    private void Reset()
    {
        interactionCamera = Camera.main;
    }

    private void Update()
    {
        // Don't let clicks reach the world while a tutorial popup is open.
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

        if (Input.GetMouseButtonDown(0))
            TryInteract();
    }

    private void TryInteract()
    {
        Ray ray = interactionCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxInteractDistance, interactableLayer))
        {
            if (hit.collider.TryGetComponent(out InteractableObject interactable))
                interactable.Interact();
        }
    }
}