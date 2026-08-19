using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on any transparent Button GameObject that sits on top of a screenshot
/// (e.g. an invisible button positioned exactly over the "Properties" button
/// in a Windows screenshot). It fires a UnityEvent when clicked, so you can
/// wire it up in the Inspector without writing new code for every button.
///
/// Typical use: hook the Button's own OnClick() to this script's OnClicked(),
/// then hook onInteract (in the Inspector) to whatever should happen -
/// e.g. COC3Manager.SubmitCurrentStep(), or revealing another panel.
/// </summary>
public class ScreenshotInteraction : MonoBehaviour
{
    [Tooltip("A label purely for your own reference in the Inspector/console (e.g. 'PropertiesButton').")]
    public string interactionId = "UnnamedInteraction";

    [Tooltip("What should happen when this transparent button is clicked. Assign in the Inspector.")]
    public UnityEvent onInteract;

    /// <summary>
    /// Wire the Button component's OnClick() event to call this method.
    /// </summary>
    public void OnClicked()
    {
        onInteract?.Invoke();
        Debug.Log($"[ScreenshotInteraction] '{interactionId}' was clicked.");
    }
}