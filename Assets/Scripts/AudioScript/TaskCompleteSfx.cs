using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One task id and the sound that congratulates the learner for finishing it.
/// </summary>
[System.Serializable]
public class TaskStageSound
{
    [Tooltip("The TaskManager id, spelled EXACTLY. COC1's two are:\n\n" +
             "    disassemble_pc\n" +
             "    assemble_pc\n\n" +
             "A trailing space here is the classic way to make this silently never fire - use " +
             "the Report button on this component to check what the scene actually registered.")]
    public string taskId;

    [Tooltip("What plays when that task completes.")]
    public SfxEntry sound = new SfxEntry();
}

/// <summary>
/// Plays a congratulation sound when a COC1 stage is finished.
///
/// ATTACH THIS TO: the Managers GameObject, beside SfxPlayer and TaskManager.
///
/// It listens to TaskManager.OnTaskCompleted and needs no changes to TaskManager,
/// PCDisassemblySetup or anything else - which also means it works for any task in any
/// module, not just COC1's two. Add a row, type the id, drop in a clip.
///
/// WHY IT SUBSCRIBES IN Start() AND NOT Awake() OR OnEnable(). TaskManager.RestoreState
/// re-fires OnTaskCompleted for every task that was ALREADY complete in the save file:
///
///     if (entry.isComplete) OnTaskCompleted?.Invoke(entry.taskId);
///
/// SaveManager runs that from SceneManager.sceneLoaded, which fires after Awake and OnEnable
/// but BEFORE Start. Subscribe any earlier and pressing Continue would blast the fanfare at
/// the loading screen for work the learner finished last session - congratulating them for
/// nothing, before they have done a thing. Subscribing in Start misses that burst entirely.
/// Startup Grace Seconds below is a second line of defence in case a scene ever restores late.
/// </summary>
[DisallowMultipleComponent]
public class TaskCompleteSfx : MonoBehaviour
{
    [Header("Per-stage sounds")]
    [Tooltip("One row per task you want a sound for. For COC1, two rows: disassemble_pc and " +
             "assemble_pc.")]
    public TaskStageSound[] stages = new TaskStageSound[0];

    [Header("Whole module")]
    [Tooltip("OPTIONAL, and it is the big one. Plays when EVERY task listed above is complete - " +
             "for COC1, when the learner has taken the PC apart AND put it back together.\n\n" +
             "When this fires it REPLACES that last stage's own sound rather than stacking on " +
             "top of it, because two jingles at once is noise, not celebration.\n\n" +
             "Leave the clip empty to skip this entirely.")]
    public SfxEntry allStagesComplete = new SfxEntry();

    [Header("Timing")]
    [Tooltip("Pause before the sound plays. A completion usually opens a dialog in the same " +
             "frame, and landing the jingle a moment after the panel appears reads as a reward " +
             "for the panel rather than a collision with it.")]
    [Min(0f)]
    public float delaySeconds = 0.25f;

    [Tooltip("Ignore any completion reported in the first moments after the scene loads. See the " +
             "note on this class: restoring a save replays every finished task, and none of " +
             "those deserve a fanfare.")]
    [Min(0f)]
    public float startupGraceSeconds = 0.5f;

    [Header("Diagnostics")]
    [Tooltip("Logs every task completion this hears, whether it played anything, and why not.")]
    public bool logEvents = false;

    private readonly HashSet<string> completed = new HashSet<string>();
    private bool subscribed;
    private bool firedAllComplete;

    // ==================================================================

    private void Start()
    {
        if (TaskManager.Instance == null)
        {
            Debug.LogWarning("[TaskCompleteSfx] No TaskManager in this scene, so no completion " +
                             "sounds will play.", this);
            return;
        }

        TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
        subscribed = true;

        if (SfxPlayer.Instance == null)
            Debug.LogWarning("[TaskCompleteSfx] No SfxPlayer in this scene. Completion sounds are " +
                             "routed through it, so nothing will be heard until one exists on " +
                             "Managers.", this);
    }

    private void OnDestroy()
    {
        // Unsubscribing matters here: TaskManager is a scene singleton, and a stale delegate
        // pointing at a destroyed component throws on the next completion.
        if (subscribed && TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
    }

    // ==================================================================

    private void HandleTaskCompleted(string taskId)
    {
        if (Time.timeSinceLevelLoad < startupGraceSeconds)
        {
            if (logEvents)
                Debug.Log($"[TaskCompleteSfx] Ignoring '{taskId}' - reported {Time.timeSinceLevelLoad:F2}s " +
                          "after load, which is a save being restored rather than the learner " +
                          "finishing something.", this);

            completed.Add(taskId);      // still counts as done, it just does not get a fanfare
            return;
        }

        if (!completed.Add(taskId))
        {
            if (logEvents) Debug.Log($"[TaskCompleteSfx] '{taskId}' already counted - ignoring repeat.", this);
            return;
        }

        TaskStageSound stage = FindStage(taskId);
        if (stage == null)
        {
            if (logEvents)
                Debug.Log($"[TaskCompleteSfx] '{taskId}' completed, but no row lists it - nothing " +
                          "to play. That is fine if you only want sounds on some tasks.", this);
            return;
        }

        // The finale supersedes the last stage's own sound.
        bool everythingDone = !firedAllComplete && AllStagesComplete() && allStagesComplete.HasSound;

        if (everythingDone)
        {
            firedAllComplete = true;
            if (logEvents) Debug.Log($"[TaskCompleteSfx] '{taskId}' was the last one - playing the module finale.", this);
            StartCoroutine(PlayAfterDelay(allStagesComplete, delaySeconds));
        }
        else
        {
            if (logEvents) Debug.Log($"[TaskCompleteSfx] '{taskId}' completed - playing its stage sound.", this);
            StartCoroutine(PlayAfterDelay(stage.sound, delaySeconds));
        }
    }

    private IEnumerator PlayAfterDelay(SfxEntry entry, float delay)
    {
        // Realtime, not scaled. A completion often opens a dialog, DialogManager can pause the
        // game while one is up, and a WaitForSeconds under timeScale 0 never returns - the
        // congratulation would simply never arrive.
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        SfxPlayer.PlayCustom(entry);
    }

    private TaskStageSound FindStage(string taskId)
    {
        if (stages == null) return null;

        foreach (var s in stages)
        {
            if (s == null || string.IsNullOrEmpty(s.taskId)) continue;

            // Trimmed, because a trailing space typed into the Inspector is invisible and has
            // already cost this project a silent StoryCompleteTrigger once.
            if (s.taskId.Trim() == taskId) return s;
        }
        return null;
    }

    private bool AllStagesComplete()
    {
        if (stages == null || stages.Length == 0) return false;

        foreach (var s in stages)
        {
            if (s == null || string.IsNullOrEmpty(s.taskId)) continue;
            if (!completed.Contains(s.taskId.Trim())) return false;
        }
        return true;
    }

    // ==================================================================

    /// <summary>
    /// Prints what is wired here and what TaskManager actually knows about, side by side.
    /// A task id that does not match is the one failure mode you cannot see by looking.
    /// </summary>
    [ContextMenu("Report completion sounds")]
    public void ReportSetup()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[TaskCompleteSfx] on '{name}'");
        sb.AppendLine($"  SfxPlayer present : {(SfxPlayer.Instance != null ? "yes" : "NO - nothing will be heard")}");
        sb.AppendLine($"  TaskManager       : {(TaskManager.Instance != null ? "found" : "MISSING")}");
        sb.AppendLine($"  Delay / grace     : {delaySeconds:F2}s / {startupGraceSeconds:F2}s");

        if (stages == null || stages.Length == 0)
        {
            sb.AppendLine("  Stages            : <none listed>");
        }
        else
        {
            foreach (var s in stages)
            {
                if (s == null) { sb.AppendLine("  - <empty row>"); continue; }

                string id = s.taskId ?? "";
                string spacing = id != id.Trim() ? "  <-- has leading/trailing SPACES" : "";
                string known = TaskManager.Instance != null && Application.isPlaying
                    ? (TaskManager.Instance.IsTaskComplete(id.Trim()) ? "complete" : "registered/incomplete")
                    : "unknown outside Play mode";

                sb.AppendLine($"  - '{id}'{spacing}");
                sb.AppendLine($"      clip  : {(s.sound != null && s.sound.clip != null ? s.sound.clip.name : "<none>")}");
                sb.AppendLine($"      state : {known}");
            }
        }

        sb.AppendLine($"  Module finale     : " +
                      $"{(allStagesComplete != null && allStagesComplete.clip != null ? allStagesComplete.clip.name : "<none>")}");
        Debug.Log(sb.ToString(), this);
    }

    /// <summary>Plays the finale now, so you can balance it without finishing the module.</summary>
    [ContextMenu("Test module finale")]
    public void TestFinale()
    {
        SfxPlayer.PlayCustom(allStagesComplete);
    }
}
