using UnityEngine;

/// <summary>
/// Anything that can be stored in a HotbarSlot (PC parts, cable connectors, etc).
/// Lets HotbarSlot/DragController work generically instead of hard-coding PCPartItem.
/// </summary>
public interface IHotbarStorable
{
    Sprite Icon { get; }
}