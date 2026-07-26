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

    /// <summary>Call once at the start of a level/scene to define a task and how many steps it needs.</summary>
    public void RegisterTask(string taskId, int requiredSteps)
    {
        if (tasks.ContainsKey(taskId)) return;
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
            Debug.Log($"[TaskManager] '{taskId}' COMPLETED — firing OnTaskCompleted"); // temporary
            OnTaskCompleted?.Invoke(taskId);
        }
        else
        {
            tasks[taskId] = progress;
        }
    }

    public bool IsTaskComplete(string taskId) =>
        tasks.TryGetValue(taskId, out TaskProgress p) && p.isComplete;

    // ---------------------------------------------------------------
    // ISaveable — lets SaveManager persist/restore task progress with no
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
            // relying on RegisterTask having already run first — this way
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