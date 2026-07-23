/// <summary>
/// SSD version of PCPartItem — the SSD can only be picked up once every cable
/// connected to it has been unplugged (SATA data cable, SATA power cable, etc).
/// All the actual logic lives in CableGatedPartItem (shared with PSUPartItem
/// and DataCablePartItem); this class exists just for a clear name in the
/// Inspector/Add Component search.
///
/// Assign every CableConnector that plugs INTO this SSD in the inherited
/// Cables field — these are the connectors sitting on the FAR end of each
/// cable (the ones your CableConnector script is on), not a fixed anchor on
/// the SSD itself.
/// </summary>
public class SSDPartItem : CableGatedPartItem { }