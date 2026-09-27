using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Starts the OS install without the hardware prerequisites, so the install flow can be tested
/// without assembling the PC first - and can jump straight to any stage, so the last screens can
/// be reached in seconds instead of ten minutes.
///
/// ATTACH THIS TO: any object in the COC1 scene. One per scene. Then either tick Start On Play, or
/// right-click the component and pick "Start install (bypass checks)".
///
/// ---------------------------------------------------------------------------------------------
/// IT IS OFF IN A REAL BUILD, AND THAT IS THE POINT
///
/// A switch that skips the prerequisites is the opposite of what this project is for: the checks
/// ARE the competency. A learner who installs an operating system on a machine with no RAM, no
/// boot drive and no power has demonstrated nothing, and a graded run that allowed it would be
/// evidence of nothing.
///
/// So it refuses to do anything unless this is the Editor or a Development Build. Allow In Builds
/// exists for the rare case of testing on a phone, and it says in the Console every time it fires
/// that the run is not valid evidence. Leave it off for anything anyone is assessed on.
/// ---------------------------------------------------------------------------------------------
///
/// WHY IT USES REFLECTION
/// OSInstallManager.BeginInstall always runs CanStartInstall, and both the check and the sequence
/// that follows it are private. Adding a bypass flag would mean editing that file, and a testing
/// convenience is not worth a change to the script that runs the real assessment - one stray
/// keystroke in it costs far more than this does. So this reaches the private sequence directly
/// and leaves OSInstallManager untouched.
///
/// Reflection into private members is brittle by nature, so every lookup here is checked and names
/// the member it could not find. A rename shows up as a sentence in the Console rather than a
/// bypass that silently stops working.
/// </summary>
[DisallowMultipleComponent]
public class OSInstallTestBypass : MonoBehaviour
{
    [Header("When to fire")]
    [Tooltip("Runs the bypass shortly after the scene starts.")]
    [SerializeField] private bool startOnPlay;

    [Tooltip("Seconds to wait before Start On Play fires, so the install view and the manager have " +
             "finished their own Start().")]
    [SerializeField] private float startDelay = 0.5f;

    [Tooltip("Press this at any time to start the install. None disables the shortcut.")]
    [SerializeField] private KeyCode hotkey = KeyCode.F9;

    [Header("Where to start")]
    [Tooltip("Stage index to jump to, or -1 to begin from the start (or wherever a loaded save " +
             "left off). Use \"List the stages\" below to see the index of each one - that is how " +
             "you get to the service report without doing the whole install.")]
    [SerializeField] private int jumpToStageIndex = -1;

    [Header("Safety")]
    [Tooltip("OFF (correct): the bypass does nothing outside the Editor and Development Builds.\n\n" +
             "ON: it also works in a normal build. Only for testing on a device, and never for a " +
             "build anyone is assessed on - skipping the hardware checks makes the run worthless " +
             "as evidence.")]
    [SerializeField] private bool allowInBuilds;

    // ==================================================================

    private void Start()
    {
        if (startOnPlay) Invoke(nameof(StartBypassingChecks), Mathf.Max(0f, startDelay));
    }

    private void Update()
    {
        if (hotkey != KeyCode.None && Input.GetKeyDown(hotkey)) StartBypassingChecks();
    }

    // ==================================================================

    /// <summary>Starts the install sequence, skipping the hardware checks.</summary>
    [ContextMenu("Start install (bypass checks)")]
    public void StartBypassingChecks()
    {
        if (!Allowed()) return;

        OSInstallManager manager = FindManager();
        if (manager == null) return;

        if (manager.IsRunning)
        {
            Debug.Log("[OSInstallTestBypass] The install is already running - nothing to do.", this);
            return;
        }

        // The stage to begin at, resolved BEFORE anything is started. RestoreToStage sets
        // CurrentStageIndex and raises OnStageChanged, so the screen is already showing the right
        // stage by the time the sequence picks it up.
        int start;

        if (jumpToStageIndex >= 0)
        {
            manager.RestoreToStage(jumpToStageIndex);
            start = jumpToStageIndex;
        }
        else
        {
            start = manager.CurrentStageIndex >= 0 ? manager.CurrentStageIndex : 0;
        }

        if (!SetIsRunning(manager, true)) return;

        MethodInfo runSequence = typeof(OSInstallManager).GetMethod(
            "RunSequence", BindingFlags.Instance | BindingFlags.NonPublic);

        if (runSequence == null)
        {
            SetIsRunning(manager, false);
            Debug.LogError("[OSInstallTestBypass] OSInstallManager has no private method called " +
                           "'RunSequence' any more - it has been renamed. Update this script to " +
                           "match, or start the install normally.", this);
            return;
        }

        var routine = runSequence.Invoke(manager, new object[] { start }) as IEnumerator;

        if (routine == null)
        {
            SetIsRunning(manager, false);
            Debug.LogError("[OSInstallTestBypass] RunSequence did not return an IEnumerator - its " +
                           "signature has changed. Update this script.", this);
            return;
        }

        Coroutine handle = manager.StartCoroutine(routine);

        // Handed to the manager's own field so StopInstall() can still stop it. Without this the
        // sequence would be un-stoppable and a second bypass would run two copies of it at once,
        // racing each other through the stages.
        StoreCoroutineHandle(manager, handle);

        Debug.LogWarning($"[OSInstallTestBypass] Install started from stage {start} WITH THE " +
                         "HARDWARE CHECKS SKIPPED. This run is for testing and is not valid " +
                         "evidence of competency.", this);
    }

    /// <summary>Stops a bypassed run. The manager's own StopInstall, reachable from the menu.</summary>
    [ContextMenu("Stop install")]
    public void StopInstall()
    {
        OSInstallManager manager = FindManager();
        if (manager == null) return;

        manager.StopInstall();
        Debug.Log("[OSInstallTestBypass] Install stopped.", this);
    }

    /// <summary>
    /// Prints every stage with its index, so Jump To Stage Index can be set to something
    /// meaningful rather than guessed at.
    /// </summary>
    [ContextMenu("List the stages")]
    public void ListStages()
    {
        OSInstallManager manager = FindManager();
        if (manager == null) return;

        FieldInfo field = typeof(OSInstallManager).GetField(
            "stages", BindingFlags.Instance | BindingFlags.NonPublic);

        var stages = field != null
            ? field.GetValue(manager) as System.Collections.Generic.List<OSInstallStageData>
            : null;

        if (stages == null)
        {
            Debug.LogError("[OSInstallTestBypass] Couldn't read the manager's 'stages' list - the " +
                           "field has been renamed.", this);
            return;
        }

        var sb = new StringBuilder("[OSInstallTestBypass] Stages on this manager:\n");

        for (int i = 0; i < stages.Count; i++)
        {
            OSInstallStageData stage = stages[i];

            sb.AppendLine(stage == null
                ? $"  {i,2}  (empty slot)"
                : $"  {i,2}  {stage.stage}" +
                  (stage.requiresUserInput ? "   [asks the learner]" : ""));
        }

        Debug.Log(sb.ToString(), this);
    }

    // ==================================================================

    private bool Allowed()
    {
        if (Application.isEditor || Debug.isDebugBuild || allowInBuilds) return true;

        Debug.LogWarning("[OSInstallTestBypass] Ignored - this is a normal build, and skipping the " +
                         "hardware checks here would let a run count as evidence when it should " +
                         "not. Tick Allow In Builds only for testing on a device.", this);
        return false;
    }

    private OSInstallManager FindManager()
    {
        OSInstallManager manager = OSInstallManager.Instance;

        if (manager == null)
            manager = FindAnyObjectByType<OSInstallManager>(FindObjectsInactive.Include);

        if (manager == null)
            Debug.LogError("[OSInstallTestBypass] No OSInstallManager in this scene.", this);

        return manager;
    }

    /// <summary>
    /// Sets the manager's IsRunning, which has a private setter.
    ///
    /// The property's own setter is tried first and the compiler-generated backing field only as a
    /// fallback: "&lt;IsRunning&gt;k__BackingField" is a name the C# compiler chooses and is not
    /// promised to stay, whereas the setter is part of the property as written.
    /// </summary>
    private bool SetIsRunning(OSInstallManager manager, bool value)
    {
        PropertyInfo property = typeof(OSInstallManager).GetProperty(
            "IsRunning", BindingFlags.Instance | BindingFlags.Public);

        MethodInfo setter = property != null ? property.GetSetMethod(nonPublic: true) : null;

        if (setter != null)
        {
            setter.Invoke(manager, new object[] { value });
            return true;
        }

        FieldInfo backing = typeof(OSInstallManager).GetField(
            "<IsRunning>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        if (backing != null)
        {
            backing.SetValue(manager, value);
            return true;
        }

        Debug.LogError("[OSInstallTestBypass] Couldn't set OSInstallManager.IsRunning - neither " +
                       "its private setter nor its backing field could be found. The install was " +
                       "not started.", this);
        return false;
    }

    /// <summary>
    /// Puts the coroutine handle where StopInstall expects it. A warning rather than an error if
    /// the field has been renamed: the install still runs, it just cannot be stopped, and that is
    /// worth saying without implying nothing happened.
    /// </summary>
    private void StoreCoroutineHandle(OSInstallManager manager, Coroutine handle)
    {
        FieldInfo field = typeof(OSInstallManager).GetField(
            "_runningCoroutine", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field != null)
        {
            field.SetValue(manager, handle);
            return;
        }

        Debug.LogWarning("[OSInstallTestBypass] Couldn't find '_runningCoroutine' on " +
                         "OSInstallManager, so Stop Install will not stop this run. The install " +
                         "itself is running normally.", this);
    }
}
