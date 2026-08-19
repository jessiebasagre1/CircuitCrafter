using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to the ROOT of the imported UTP_Wire_Stripping_Animation prefab
/// (the object holding the Animator). That root contains BOTH the crimping
/// tool and the UTP cable as children, since they were baked together in
/// Blender as a single ~10.8s clip (260 frames @ 24fps):
///
///   Frame   1- 35  idle (tool resting away from cable)
///   Frame  35- 65  approach
///   Frame  65- 85  handles close / grip
///   Frame  90-125  twist (score the jacket)
///   Frame 125-178  slide - jacket scrap pulled off, wires revealed
///   Frame 178-200  handles open / release
///   Frame 200-260  retreat + hold on finished pose
///
/// Recommended flow: your existing hotbar/drag system moves a draggable
/// tool object (or ghost/icon) freely. On a valid drop onto the cable's
/// socket, THIS script takes over: it disables further manual dragging,
/// optionally eases the tool into the exact pose the clip was authored at
/// (animationStartPose), then plays the baked clip start-to-finish.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CrimpToolStripController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [Tooltip("Empty GameObject placed at the transform the tool object should be at when the clip starts (frame 1 / idle pose). Optional - if left null, no snap-lerp is done and the clip just plays from wherever the rig root currently is.")]
    [SerializeField] private Transform toolIdleAnchor;
    [Tooltip("The tool sub-object under this rig that your player was dragging before drop (used only for the optional snap-lerp).")]
    [SerializeField] private Transform draggedToolTransform;
    [SerializeField] private float snapDuration = 0.25f;

    [Header("Animator")]
    [SerializeField] private string playTrigger = "PlayStrip";
    [SerializeField] private string clipName = "CrimpTool_Strip_UTP"; // match whatever you renamed the clip to on import

    public bool IsStripping { get; private set; }
    public bool IsComplete { get; private set; }

    /// <summary>Fired once the clip finishes (e.g. to mark a checkpoint task complete, show a success toast, etc.)</summary>
    public System.Action OnStripComplete;

    private void Reset()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>
    /// Call this from your drop-zone / socket detection (e.g. the same place
    /// PartSocket or CableConnector calls its own "snap" logic) once the
    /// player releases the tool over the cable's strip point.
    /// </summary>
    public void TryDropOnStripSocket()
    {
        if (IsStripping || IsComplete) return;
        StartCoroutine(SnapAndPlay());
    }

    private IEnumerator SnapAndPlay()
    {
        IsStripping = true;

        // Smoothly hand off from "wherever the player dropped it" to the
        // animation's authored starting pose so there's no visual pop.
        if (toolIdleAnchor != null && draggedToolTransform != null)
        {
            Vector3 startPos = draggedToolTransform.position;
            Quaternion startRot = draggedToolTransform.rotation;
            float t = 0f;
            while (t < snapDuration)
            {
                t += Time.deltaTime;
                float f = Mathf.Clamp01(t / snapDuration);
                draggedToolTransform.position = Vector3.Lerp(startPos, toolIdleAnchor.position, f);
                draggedToolTransform.rotation = Quaternion.Slerp(startRot, toolIdleAnchor.rotation, f);
                yield return null;
            }
            draggedToolTransform.SetPositionAndRotation(toolIdleAnchor.position, toolIdleAnchor.rotation);
        }

        animator.SetTrigger(playTrigger);

        yield return new WaitForSeconds(GetClipLength());

        IsStripping = false;
        IsComplete = true;
        OnStripComplete?.Invoke();
    }

    /// <summary>Rewind and allow the sequence to be played again (e.g. "Retry" in the training game).</summary>
    public void ResetForReplay()
    {
        IsStripping = false;
        IsComplete = false;
        animator.Play(clipName, 0, 0f);
        animator.Update(0f);
    }

    private float GetClipLength()
    {
        var controller = animator.runtimeAnimatorController;
        if (controller != null)
        {
            foreach (var clip in controller.animationClips)
            {
                if (clip.name == clipName) return clip.length;
            }
        }
        // Fallback: 260 frames @ 24fps
        return 260f / 24f;
    }
}