/// <summary>
/// PSU version of PCPartItem — the PSU can only be picked up once every cable
/// running out of it has been unplugged. All the actual logic now lives in
/// CableGatedPartItem (shared with DataCablePartItem and any future
/// cable-gated part); this class exists just for a clear name in the
/// Inspector/Add Component search.
///
/// Assign every CableConnector whose cable originates from this PSU (24-pin
/// ATX, 8-pin EPS, PCIe, etc) in the inherited Cables field.
/// </summary>
public class PSUPartItem : CableGatedPartItem { }