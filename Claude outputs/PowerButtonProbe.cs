using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A TEMPORARY, READ-ONLY PROBE for "the power button is there the first time and gone the second".
///
/// ATTACH THIS TO: any object in the COC1 scene. It changes nothing - every line either reads a
/// value or writes to the Console. Delete it once the bug is found.
///
/// ---------------------------------------------------------------------------------------------
/// WHY A PROBE RATHER THAN A FIX
///
/// PowerButtonInput decides visibility from four conditions:
///
///     shouldShow = inStationView && !alreadyRunning && !hasPoweredOn && unlocked;
///
/// Three of them are private state and the fourth is on another object, so from the outside a
/// hidden button looks the same whichever one turned false. Each has a completely different fix -
/// hasPoweredOn latching means the button is behaving correctly and the real bug is upstream;
/// unlocked going false points at the TaskGate; alreadyRunning points at an install that never
/// stopped. Guessing between them wastes a rebuild each time.
///
/// IT ALSO SEPARATES "HIDDEN" FROM "HIDDEN BY A PARENT". If shouldShow is true and the button's
/// own activeSelf is true but activeInHierarchy is false, then PowerButtonInput is doing its job
/// and something above it in the hierarchy is switched off - which would point at
/// StationCameraSwitch's enableOnStationView / disableOnStationView restore, not at this script
/// at all. That distinction is the whole reason the last line of the report exists.
/// ---------------------------------------------------------------------------------------------
///
/// WHAT TO DO: press Play, walk into the station, walk out, walk back in, then read the Console.
/// Every line is one change of state, so the entry that goes wrong is the one to read.
/// </summary>
[DisallowMultipleComponent]
public class PowerButtonProbe : MonoBehaviour
{
    [Tooltip("Leave empty to find the PowerButtonInput in the scene automatically.")]
    [SerializeField] private PowerButtonInput target;

    [Tooltip("Also print a line every few seconds even when nothing changed, so a frozen state " +
             "still shows up in the log.")]
    [SerializeField] private bool heartbeat;

    [SerializeField] private float heartbeatSeconds = 5f;

    private FieldInfo hasPoweredOnField;
    private FieldInfo cameraField;
    private FieldInfo buttonField;
    private FieldInfo gateField;

    private string lastLine = "";
    private float nextHeartbeat;

    // ==================================================================

    private void Start()
    {
        if (target == null) target = FindAnyObjectByType<PowerButtonInput>(FindObjectsInactive.Include);

        if (target == null)
        {
            Debug.LogError("[PowerButtonProbe] No PowerButtonInput in this scene.", this);
            enabled = false;
            return;
        }

        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        hasPoweredOnField = typeof(PowerButtonInput).GetField("hasPoweredOn", Hidden);
        cameraField = typeof(PowerButtonInput).GetField("targetStationCamera", Hidden);
        buttonField = typeof(PowerButtonInput).GetField("mobilePowerButton", Hidden);
        gateField = typeof(PowerButtonInput).GetField("gate", Hidden);

        // Named individually rather than as one "something is missing": a rename in the learner's
        // file should say which field, not send them reading this script to find out.
        Complain(hasPoweredOnField, "hasPoweredOn");
        Complain(cameraField, "targetStationCamera");
        Complain(buttonField, "mobilePowerButton");
        Complain(gateField, "gate");

        Debug.Log($"[PowerButtonProbe] Watching '{target.name}'. Walk into the station, out, and " +
                  "back in, then read the lines below.", this);
    }

    private void Complain(FieldInfo field, string name)
    {
        if (field == null)
            Debug.LogWarning($"[PowerButtonProbe] PowerButtonInput has no field called '{name}' - " +
                             "it has been renamed, so that row of the report will read 'unknown'.", this);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        string line = Compose();

        bool due = heartbeat && Time.unscaledTime >= nextHeartbeat;

        if (line == lastLine && !due) return;

        lastLine = line;
        nextHeartbeat = Time.unscaledTime + Mathf.Max(1f, heartbeatSeconds);

        Debug.Log("[PowerButtonProbe] " + line, this);
    }

    // ==================================================================

    private string Compose()
    {
        var camera = cameraField != null ? cameraField.GetValue(target) as GameObject : null;
        var button = buttonField != null ? buttonField.GetValue(target) as Button : null;

        bool inStationView = camera == null || camera.activeInHierarchy;
        bool hasPoweredOn = hasPoweredOnField != null && (bool)hasPoweredOnField.GetValue(target);

        bool running = OSInstallManager.Instance != null && OSInstallManager.Instance.IsRunning;
        string stage = OSInstallManager.Instance != null
            ? OSInstallManager.Instance.CurrentStage.ToString()
            : "no manager";

        bool unlocked = ReadGate();

        bool shouldShow = inStationView && !running && !hasPoweredOn && unlocked;

        var sb = new StringBuilder();

        sb.Append(shouldShow ? "SHOW  " : "hide  ");
        sb.Append($"inStationView={inStationView}  ");
        sb.Append($"running={running}  ");
        sb.Append($"hasPoweredOn={hasPoweredOn}  ");
        sb.Append($"unlocked={unlocked}  ");
        sb.Append($"stage={stage}");

        if (button == null)
        {
            sb.Append("  |  NO BUTTON ASSIGNED on PowerButtonInput");
            return sb.ToString();
        }

        GameObject go = button.gameObject;

        sb.Append($"  |  button activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy}");

        // THE LINE THAT SPLITS THE TWO POSSIBLE BUGS. Own flag on, hierarchy flag off, means a
        // PARENT is switched off - PowerButtonInput is doing exactly what it should and the
        // problem is whatever turned that parent off, almost certainly StationCameraSwitch's
        // enableOnStationView / disableOnStationView restore.
        if (go.activeSelf && !go.activeInHierarchy)
            sb.Append($"  <-- A PARENT IS OFF: {FirstInactiveParent(go.transform)}");

        return sb.ToString();
    }

    private bool ReadGate()
    {
        if (gateField == null) return true;

        object gate = gateField.GetValue(target);
        if (gate == null) return true;

        MethodInfo isUnlocked = gate.GetType().GetMethod("IsUnlocked",
            BindingFlags.Instance | BindingFlags.Public);

        if (isUnlocked == null) return true;

        object result = isUnlocked.Invoke(gate, null);
        return result is bool ok && ok;
    }

    /// <summary>Names the nearest switched-off ancestor, which is the object to go and look at.</summary>
    private static string FirstInactiveParent(Transform from)
    {
        Transform walk = from.parent;

        while (walk != null)
        {
            if (!walk.gameObject.activeSelf) return walk.name;
            walk = walk.parent;
        }

        return "none found - the object itself may have been destroyed";
    }
}
