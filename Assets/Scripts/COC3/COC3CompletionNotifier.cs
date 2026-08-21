using UnityEngine;

/// <summary>
/// Put this on the COMPLETION SCREEN GameObject (the one assigned to
/// COC3Manager.completionScreen).
///
/// COC3Manager has no "activity finished" event, and the goal was to build COC3
/// without editing the scripts you already have. So instead of an event, this
/// component uses the one signal that already exists: COC3Manager calls
/// completionScreen.SetActive(true) when the last step is done, which fires this
/// component's OnEnable().
///
/// That single line is the whole trick - it turns "the report screen appeared"
/// into "record COC3 as finished".
/// </summary>
[DisallowMultipleComponent]
public class COC3CompletionNotifier : MonoBehaviour
{
    [Tooltip("The COC3CompletionManager, normally on the same GameObject as COC3Manager. Found automatically if left empty.")]
    public COC3CompletionManager completionManager;

    private void OnEnable()
    {
        if (completionManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            // FindAnyObjectByType, not FindFirstObjectByType: the "First" variant was
            // deprecated because it depends on instance-ID ordering, and there is only
            // ever one COC3CompletionManager in the scene, so "any" is what we mean.
            completionManager = Object.FindAnyObjectByType<COC3CompletionManager>();
#else
            completionManager = Object.FindObjectOfType<COC3CompletionManager>();
#endif
        }

        if (completionManager == null)
        {
            Debug.LogWarning("[COC3CompletionNotifier] No COC3CompletionManager found - COC3 completion will not be saved.", this);
            return;
        }

        completionManager.MarkCompleted();
    }
}