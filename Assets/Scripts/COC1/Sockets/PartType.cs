/// <summary>
/// Identifies what kind of PC component a part/socket is, so a part can only
/// be dropped into a socket of the matching type (RAM into a RAM slot, etc).
/// Extend this list to match the components in your CircuitCrafter parts library.
/// </summary>
public enum PartType
{
    CPU,
    RAM,
    GPU,
    PSU,
    Storage,
    Cooler,
    Motherboard,
    SSD,
    DataCable,
    Tool,
}