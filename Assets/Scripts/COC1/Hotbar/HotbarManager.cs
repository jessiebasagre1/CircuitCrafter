using UnityEngine;
/// <summary>Simple registry of hotbar slots. Assign all slot references in the Inspector.</summary>
public class HotbarManager : MonoBehaviour
{
    public static HotbarManager Instance { get; private set; }
    public HotbarSlot[] slots;
    void Awake() => Instance = this;
    public HotbarSlot GetFirstEmptySlot()
    {
        foreach (var slot in slots)
            if (slot.IsEmpty) return slot;
        return null; // hotbar full
    }
}