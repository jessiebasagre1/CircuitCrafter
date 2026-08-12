using UnityEngine;
/// <summary>Simple registry of hotbar slots. Assign all slot references in the Inspector.</summary>
public class HotbarManager : MonoBehaviour
{
    public static HotbarManager Instance { get; private set; }
    public HotbarSlot[] slots;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[HotbarManager] Duplicate detected on '{name}' - " +
                $"an instance already exists on '{Instance.name}'. Destroying this one.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log($"[HotbarManager] Instance set to '{name}', {slots?.Length ?? 0} slots assigned.", this);
    }

    public HotbarSlot GetFirstEmptySlot()
    {
        foreach (var slot in slots)
            if (slot.IsEmpty) return slot;
        return null; // hotbar full
    }
}