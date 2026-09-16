using UnityEngine;

public enum CPULockRole
{
    Main,
    Case
}

/// <summary>
/// A single CPU retention lock/latch. Attach this to both the Main lock and
/// the Case lock, but set 'role' differently on each. Each instance now
/// tracks its own open/closed state independently - previously isOpen was a
/// single shared static bool, so two locks using this script couldn't have
/// separate states at all.
///
/// IT IS ALSO PHYSICALLY BLOCKED BY THE HEATSINK. On a real board the cooler
/// sits directly over the retention lever - you cannot touch the lever until
/// the cooler is off. Assign Heatsink Socket below and this lock refuses to
/// move while a heatsink is mounted there.
/// </summary>
public class CPULock : MonoBehaviour
{
    [Header("Identity")]
    public CPULockRole role = CPULockRole.Main;

    [Header("References")]
    public Transform openPosition; // Position when this lock is opened

    [Tooltip("Case lock only: the Main lock that must already be open before this one can open.")]
    public CPULock mainLock;

    [Header("Heatsink")]
    [Tooltip("OPTIONAL. The heatsink's PartSocket. While a heatsink is seated there this lock " +
             "cannot be moved at all, in either direction - the cooler is bolted over the lever.\n\n" +
             "Leave empty on a build with no heatsink and this lock behaves exactly as before.")]
    public PartSocket heatsinkSocket;

    [Header("Warnings")]
    [Tooltip("OPTIONAL. Shown when the learner tries to move this lock while the heatsink is still " +
             "mounted, or tries to open the case lock before the main lock.")]
    public DialogSequence blockedDialog;

    Vector3 closedPosition;
    Quaternion closedRotation;

    public bool IsOpen { get; private set; }

    /// <summary>
    /// True while a heatsink is mounted over this lock, so it cannot be operated.
    ///
    /// Read by PartClickDetector, which uses it to let a click FALL THROUGH to the
    /// heatsink sitting on top rather than being swallowed by the lever underneath.
    /// Without that, the heatsink would be unclickable: the lock's collider is in
    /// the way, the lock refuses to move, and the learner is stuck with a cooler
    /// they cannot remove.
    /// </summary>
    public bool IsCovered => heatsinkSocket != null && !heatsinkSocket.IsEmpty;

    void Start()
    {
        // Save the original (closed) position and rotation
        closedPosition = transform.position;
        closedRotation = transform.rotation;
    }



    public void Toggle()
    {
        // Checked FIRST, and for both directions. Opening and closing are equally
        // impossible with a cooler bolted over the lever, and allowing "close" through
        // would let a learner end up with the locks shut under a heatsink they never
        // took off - a state the real procedure cannot produce.
        if (IsCovered)
        {
            Refuse("the heatsink is still mounted over it - remove the heatsink first.");
            return;
        }

        if (!IsOpen)
        {
            if (role == CPULockRole.Case && (mainLock == null || !mainLock.IsOpen))
            {
                Refuse("open the main CPU lock first.");
                return;
            }
            // Open this lock
            transform.position = openPosition.position;
            transform.rotation = openPosition.rotation;
            IsOpen = true;
        }
        else
        {
            // Close this lock
            transform.position = closedPosition;
            transform.rotation = closedRotation;
            IsOpen = false;
        }
    }

    /// <summary>
    /// One place for every refusal, so the Console line, the popup and the assessment
    /// mistake can never drift apart between the two guards above.
    /// </summary>
    void Refuse(string reason)
    {
        Debug.Log($"{name}: {reason}", this);

        if (blockedDialog != null && DialogManager.Instance != null)
            DialogManager.Instance.ShowDialog(blockedDialog);

        // ASSESSMENT: operating the retention lever out of sequence is a procedural
        // error, graded the same way as removing a part out of order. No-ops entirely
        // outside assessment mode.
        COC1AssessmentManager.ReportMistake($"Tried to move '{name}' - {reason}", this);
    }
}