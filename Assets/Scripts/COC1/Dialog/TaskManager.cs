using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks progress toward named tasks (e.g. "disassemble_pc" needs 6 parts removed)
/// and fires an event when each task is completed.
/// </summary>
public class TaskManager : MonoBehaviour
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
}