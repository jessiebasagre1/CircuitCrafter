using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-facing counterpart to ServiceReportPanel. Renders one
/// ChecklistStatusRowView per required task (plus a "Diagnostics passed" row
/// if configured) and wires the Submit button. Not a singleton, same as
/// PartitionSelectView -- there's exactly one Service Report panel in the
/// scene, referenced directly.
/// </summary>
public class ServiceReportView : MonoBehaviour
{
    [SerializeField] private ServiceReportPanel report;

    [Header("List")]
    [SerializeField] private Transform rowsContent;
    [SerializeField] private ChecklistStatusRowView rowPrefab;

    [Header("Controls")]
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject submittedBanner;

    private bool _submitted;

    // Same reason as ChecklistToolView: Start() runs once, but OnDisable unsubscribes on every
    // close. Reopening the report after going back to fix something used to call Refresh() with
    // nobody listening, so the checklist kept showing the crosses from the first time it was opened.
    private void OnEnable()
    {
        Subscribe();

        // Panel may be re-opened after the player goes back and fixes
        // something -- refresh every time it's shown, not just on Start().
        if (report != null) report.Refresh();
    }

    private void Start()
    {
        if (report == null)
        {
            Debug.LogWarning("[ServiceReportView] No ServiceReportPanel assigned.");
            return;
        }

        Subscribe();   // does nothing new if OnEnable already did

        if (submitButton != null)
            submitButton.onClick.AddListener(HandleSubmitClicked);

        report.Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (report == null) return;

        // Unsubscribe first so calling this twice can never double up the handlers.
        Unsubscribe();
        report.OnChecklistRefreshed += HandleChecklistRefreshed;
        report.OnReportSubmitted += HandleReportSubmitted;
    }

    private void Unsubscribe()
    {
        if (report == null) return;
        report.OnChecklistRefreshed -= HandleChecklistRefreshed;
        report.OnReportSubmitted -= HandleReportSubmitted;
    }

    private void HandleChecklistRefreshed(IReadOnlyList<(string label, bool complete)> checklist)
    {
        foreach (Transform child in rowsContent)
            Destroy(child.gameObject);

        foreach (var (label, complete) in checklist)
        {
            var row = Instantiate(rowPrefab, rowsContent);
            row.Setup(label, complete);
        }

        if (submitButton != null)
            submitButton.interactable = report.IsReadyToSubmit && !_submitted;

        if (statusText != null)
            statusText.text = _submitted
                ? "Report submitted."
                : report.IsReadyToSubmit
                    ? "Everything checks out - ready to submit."
                    : "Finish the items above before submitting.";
    }

    private void HandleSubmitClicked()
    {
        report.SubmitReport();
    }

    private void HandleReportSubmitted()
    {
        _submitted = true;
        if (submittedBanner != null) submittedBanner.SetActive(true);
        if (submitButton != null) submitButton.interactable = false;
        if (statusText != null) statusText.text = "Report submitted.";
    }
}