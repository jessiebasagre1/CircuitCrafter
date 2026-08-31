using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Extends PCDisassemblySetup's "assemble_pc" completion check to also
/// require:
///   - Every cable in Cables To Require Plugged is actually plugged back in.
///   - The CPU's retention locks are actually CLOSED again -- CPUPartItem
///     only requires them OPEN to remove the CPU, it never requires them
///     closed again afterward.
///
/// WHY THIS EXISTS: PCPartItem.OnReturnedToSocket is the ONLY place that
/// currently reports progress toward AssembleTaskId, one tick per
/// PCPartItem reinstalled (however many make up PCDisassemblySetup's
/// Total Parts). CableConnector and CPULock are both separate mechanisms
/// that never report to this task at all, so a PC missing a plugged cable
/// or left with open CPU locks could previously still count as "fully
/// assembled." This script adds exactly those two conditions as additional
/// required ticks toward the SAME task, rather than inventing a second
/// parallel completion check elsewhere.
///
/// WHY EACH CONDITION TRACKS "WAS IT EVER UNDONE FIRST": a normal healthy PC
/// starts with cables plugged and CPU locks closed. Without tracking that a
/// cable was actually unplugged (and the locks actually opened) at some
/// point first, this script would immediately report both ticks on the very
/// first frame -- before the player has disassembled anything at all.
///
/// SETUP:
///   1. On PCDisassemblySetup, increase Total Parts by however many entries
///      you add here: +1 per cable in Cables To Require Plugged, +1 for the
///      CPU lock check (both locks count as ONE combined condition, not
///      two separate ones).
///   2. Attach this anywhere in the scene (e.g. alongside PCDisassemblySetup).
///   3. Assign Cables To Require Plugged -- every CableConnector that must
///      be reconnected for the PC to count as assembled.
///   4. Assign CPU Main Lock / CPU Case Lock -- both must be CLOSED
///      (IsOpen == false) for the combined CPU-secured tick to report.
///      Leave both empty to skip this check entirely.
/// </summary>
public class AssemblyCompletionExtras : MonoBehaviour
{
    [Tooltip("Every cable that must be reconnected for the PC to count as fully assembled.")]
    [SerializeField] private CableConnector[] cablesToRequirePlugged;

    [Tooltip("Both must be closed again for the combined CPU-secured tick to count. Leave both empty to skip this check.")]
    [SerializeField] private CPULock cpuMainLock;
    [SerializeField] private CPULock cpuCaseLock;

    private readonly Dictionary<CableConnector, bool> everUnplugged = new Dictionary<CableConnector, bool>();
    private readonly HashSet<CableConnector> reportedCables = new HashSet<CableConnector>();

    private bool mainEverOpen;
    private bool caseEverOpen;
    private bool reportedCpuSecured;

    private void Update()
    {
        if (TaskManager.Instance == null) return;
        if (TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.AssembleTaskId))
        {
            enabled = false; // nothing left to watch once the task is already done
            return;
        }

        TrackCables();
        TrackCpuLocks();
    }

    private void TrackCables()
    {
        if (cablesToRequirePlugged == null) return;

        foreach (var cable in cablesToRequirePlugged)
        {
            if (cable == null || reportedCables.Contains(cable)) continue;

            if (!cable.isPlugged)
            {
                everUnplugged[cable] = true;
                continue;
            }

            if (everUnplugged.TryGetValue(cable, out bool wasUnplugged) && wasUnplugged)
            {
                reportedCables.Add(cable);
                TaskManager.Instance.ReportProgress(PCDisassemblySetup.AssembleTaskId);
                Debug.Log($"[AssemblyCompletionExtras] '{cable.name}' reconnected -- counted toward assembly.", this);
            }
        }
    }

    private void TrackCpuLocks()
    {
        if (reportedCpuSecured || cpuMainLock == null || cpuCaseLock == null) return;

        if (cpuMainLock.IsOpen) mainEverOpen = true;
        if (cpuCaseLock.IsOpen) caseEverOpen = true;

        if (mainEverOpen && caseEverOpen && !cpuMainLock.IsOpen && !cpuCaseLock.IsOpen)
        {
            reportedCpuSecured = true;
            TaskManager.Instance.ReportProgress(PCDisassemblySetup.AssembleTaskId);
            Debug.Log("[AssemblyCompletionExtras] CPU locks closed -- counted toward assembly.", this);
        }
    }
}
