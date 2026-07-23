using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Central manager that displays tutorial/dialog popups.
/// Anything in the game (interaction, task completion, etc.) can call
/// DialogManager.Instance.ShowDialog(sequence) to pop up a tutorial box.
/// Put this on a persistent "Managers" GameObject in your scene.
/// </summary>
public class DialogManager : MonoBehaviour
{
    public static DialogManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject dialogPanel;
    [SerializeField] private TMP_Text speakerNameText;
    [SerializeField] private TMP_Text dialogBodyText;

    [Header("Settings")]
    [SerializeField] private float typewriterSpeed = 0.02f;
    [SerializeField] private bool pauseGameWhileOpen = false;

    private readonly Queue<DialogLine> queuedLines = new Queue<DialogLine>();
    private Coroutine typingRoutine;
    private int openedFrame = -1;
    private int closedFrame = -1;

    /// <summary>Invoked when the currently-open sequence finishes (all lines advanced through).</summary>
    private Action currentOnComplete;

    /// <summary>True while a tutorial popup is showing. Other scripts (movement,
    /// interaction, drag-and-drop) should check this and ignore input while true.</summary>
    public bool IsDialogOpen { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        dialogPanel.SetActive(false);
    }

    private void Update()
    {
        if (!IsDialogOpen) return;

        // Ignore input on the exact frame the dialog opened, so the same click/key
        // that triggered the tutorial (e.g. clicking the motherboard) doesn't also
        // immediately skip the first line.
        if (Time.frameCount == openedFrame) return;

        if (Input.GetKeyDown(KeyCode.Space))
            AdvanceDialog();
    }

    /// <summary>Call this from anywhere to show a tutorial popup.</summary>
    public void ShowDialog(DialogSequence sequence)
    {
        ShowDialog(sequence, null);
    }

    /// <summary>Show a tutorial popup and invoke a callback once the player finishes it
    /// (e.g. to spawn a waypoint arrow after the sequence closes).</summary>
    public void ShowDialog(DialogSequence sequence, Action onComplete)
    {
        if (sequence == null || sequence.lines.Count == 0)
        {
            // Nothing to show — fire the callback immediately so callers don't stall.
            onComplete?.Invoke();
            return;
        }

        queuedLines.Clear();
        foreach (var line in sequence.lines)
            queuedLines.Enqueue(line);

        currentOnComplete = onComplete;

        IsDialogOpen = true;
        openedFrame = Time.frameCount;
        dialogPanel.SetActive(true);

        if (pauseGameWhileOpen) Time.timeScale = 0f;

        AdvanceDialog();
    }

    /// <summary>Called by pressing any key/button. Also called automatically to show the first line.</summary>
    private void AdvanceDialog()
    {
        if (typingRoutine != null)
        {
            // Text is still typing out - pressing a key skips straight to the full
            // line instead of moving on immediately.
            StopCoroutine(typingRoutine);
            typingRoutine = null;
            dialogBodyText.text = lastFullLine;
            return;
        }

        if (queuedLines.Count == 0)
        {
            CloseDialog();
            return;
        }

        DialogLine line = queuedLines.Dequeue();
        speakerNameText.text = line.speakerName;

        typingRoutine = StartCoroutine(TypeText(line.text));
    }

    private string lastFullLine;

    private IEnumerator TypeText(string text)
    {
        lastFullLine = text;
        dialogBodyText.text = "";
        foreach (char c in text)
        {
            dialogBodyText.text += c;
            yield return pauseGameWhileOpen
                ? new WaitForSecondsRealtime(typewriterSpeed)
                : new WaitForSeconds(typewriterSpeed);
        }
        typingRoutine = null;
    }

    private void CloseDialog()
    {
        IsDialogOpen = false;
        closedFrame = Time.frameCount;
        dialogPanel.SetActive(false);
        if (pauseGameWhileOpen) Time.timeScale = 1f;

        // Fire and clear the completion callback for this sequence.
        Action callback = currentOnComplete;
        currentOnComplete = null;
        callback?.Invoke();
    }

    /// <summary>True only on the exact frame the dialog just closed — lets other
    /// scripts ignore the same key/click press that closed it.</summary>
    public bool WasDialogClosedThisFrame => closedFrame == Time.frameCount;
}