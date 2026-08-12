using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on any object the player can click/interact with (e.g. the motherboard).
/// If a tutorialDialog is assigned, interacting shows it (once, by default).
/// </summary>
[RequireComponent(typeof(Collider))]
public class InteractableObject : MonoBehaviour
{
    [SerializeField] private string interactableId = "motherboard";
    [SerializeField] private DialogSequence tutorialDialog;
    [SerializeField] private bool showOnlyOnce = true;

    [Header("Extra logic (optional)")]
    [Tooltip("Hook up removal animation, sound, opening a panel, etc.")]
    [SerializeField] private UnityEvent onInteract;

    private bool hasShownTutorial;



    public void Interact()
    {
        if (tutorialDialog != null && (!showOnlyOnce || !hasShownTutorial))
        {
            hasShownTutorial = true;
            DialogManager.Instance.ShowDialog(tutorialDialog);
        }

        onInteract?.Invoke();
    }
}