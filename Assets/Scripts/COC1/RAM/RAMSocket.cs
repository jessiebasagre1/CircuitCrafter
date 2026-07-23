using UnityEngine;

/// <summary>
/// A RAM slot's socket, extended with references to that slot's own
/// retention clips. Use this in place of plain PartSocket on RAM sockets —
/// everything else (accepted type, occupant, installed offset) is inherited.
/// </summary>
public class RAMSocket : PartSocket
{
    [Tooltip("Every retention clip on this socket (RAM slots commonly have one at each end).")]
    public RAMLock[] locks;
}