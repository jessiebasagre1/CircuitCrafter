using UnityEngine;

public enum CPULockRole
{
    Main,
    Case
}

/// <summary>
/// A single CPU retention lock/latch. Attach this to both the Main lock and
/// the Case lock, but set 'role' differently on each. Each instance now
/// tracks its own open/closed state independently — previously isOpen was a
/// single shared static bool, so two locks using this script couldn't have
/// separate states at all.
/// </summary>
public class CPULock : MonoBehaviour
{
    [Header("Identity")]
    public CPULockRole role = CPULockRole.Main;

    [Header("References")]
    public Transform openPosition; // Position when this lock is opened

    [Tooltip("Case lock only: the Main lock that must already be open before this one can open.")]
    public CPULock mainLock;

    Vector3 closedPosition;
    Quaternion closedRotation;

    public bool IsOpen { get; private set; }

    void Start()
    {
        // Save the original (closed) position and rotation
        closedPosition = transform.position;
        closedRotation = transform.rotation;
    }



    public void Toggle()
    {
        if (!IsOpen)
        {
            if (role == CPULockRole.Case && (mainLock == null || !mainLock.IsOpen))
            {
                Debug.Log($"{name}: open the main CPU lock first.", this);
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
}