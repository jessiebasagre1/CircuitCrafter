using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks progress toward named tasks (e.g. "disassemble_pc" needs 6 parts removed)
/// and fires an event when each task is completed.
/// </summary>
public class TaskManager : MonoBehaviour, ISaveable
{
    public static TaskManager Instance { get; private set; }
    public event Action<string> OnTaskCompleted;
    private readonly Dictionary<string, TaskProgress> tasks = new Dictionary<string, TaskProgress>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>Call once at the start of a level/scene to define a task and how many steps it needs.
    /// If the task is already registered (e.g. a save was restored before this ran), the
    /// required step count is updated in place instead of being ignored -- otherwise a task's
    /// required count could get permanently stuck at whatever value an old save captured,
    /// even after the scene's own setup script asks for a different one. isComplete is
    /// re-derived from the (unchanged) current progress against the new required count, so a
    /// task that only *looked* finished under the old count correctly reopens if it isn't
    /// actually finished under the new one.</summary>
    public void RegisterTask(string taskId, int requiredSteps)
    {
        if (tasks.TryGetValue(taskId, out TaskProgress existing))
        {
            existing.required = requiredSteps;
            existing.isComplete = existing.current >= existing.required;
            tasks[taskId] = existing;
            return;
        }
        tasks[taskId] = new TaskProgress(requiredSteps);
    }

    /// <summary>Call this whenever the player makes progress (e.g. removes one part).</summary>
    public void ReportProgress(string taskId, int amount = 1)
    {
        if (!tasks.TryGetValue(taskId, out TaskProgress progress) || progress.isComplete)
            return;
        progress.current += amount;
        Debug.Log($"[TaskManager] '{taskId}' progress: {progress.current}/{progress.required}"); // temporary
        if (progress.current >= progress.required)
        {
            progress.isComplete = true;
            tasks[taskId] = progress;
            Debug.Log($"[TaskManager] '{taskId}' COMPLETED � firing OnTaskCompleted"); // temporary
            OnTaskCompleted?.Invoke(taskId);
        }
        else
        {
            tasks[taskId] = progress;
        }
    }

    public bool IsTaskComplete(string taskId) =>
        tasks.TryGetValue(taskId, out TaskProgress p) && p.isComplete;

    /// <summary>Read-only progress for a HUD ("3 of 6 parts removed"). False for an unknown task.
    /// Added for the guidance layer's Current Task panel; changes nothing about how tasks work.</summary>
    public bool TryGetProgress(string taskId, out int current, out int required)
    {
        current = 0;
        required = 0;
        if (string.IsNullOrEmpty(taskId) || !tasks.TryGetValue(taskId, out TaskProgress p)) return false;
        current = p.current;
        required = p.required;
        return true;
    }

    // ---------------------------------------------------------------
    // ISaveable � lets SaveManager persist/restore task progress with no
    // changes needed on its end.
    // ---------------------------------------------------------------

    public string SaveKey => "TaskManager";

    public string CaptureState()
    {
        var wrapper = new TaskSaveWrapper();
        foreach (var kvp in tasks)
        {
            wrapper.items.Add(new TaskSaveEntry
            {
                taskId = kvp.Key,
                current = kvp.Value.current,
                required = kvp.Value.required,
                isComplete = kvp.Value.isComplete
            });
        }
        return JsonUtility.ToJson(wrapper);
    }

    public void RestoreState(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        var wrapper = JsonUtility.FromJson<TaskSaveWrapper>(data);
        if (wrapper?.items == null) return;

        foreach (var entry in wrapper.items)
        {
            // Rebuilds the entry directly (including required) rather than
            // relying on RegisterTask having already run first � this way
            // restore works correctly regardless of whether RegisterTask
            // calls happen before or after LoadSave() in your scene's
            // startup order.
            tasks[entry.taskId] = new TaskProgress(entry.required)
            {
                current = entry.current,
                isComplete = entry.isComplete
            };

            if (entry.isComplete)
                OnTaskCompleted?.Invoke(entry.taskId);
        }
    }

    [Serializable]
    private struct TaskProgress
    {
        public int current;
        public int required;
        public bool isComplete;
        public TaskProgress(int required)
        {
            this.required = required;
            current = 0;
            isComplete = false;
        }
    }

    [Serializable]
    private struct TaskSaveEntry
    {
        public string taskId;
        public int current;
        public int required;
        public bool isComplete;
    }

    [Serializable]
    private class TaskSaveWrapper
    {
        public List<TaskSaveEntry> items = new List<TaskSaveEntry>();
    }
}