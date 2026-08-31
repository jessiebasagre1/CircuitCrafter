using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The player-facing "diagnostic tool" for a COC4 repair ticket -- framed as
/// a POST/diagnostic readout for COC1, but generic enough to reuse for a
/// cable tester (COC2) or a ping/ipconfig console (COC3) later: it only
/// reads ActiveTicket.symptomText / diagnosisOptions and doesn't know or
/// care which module it's in.
///
/// ATTACH THIS TO: a UI panel in the COC1/2/3 scene. That panel's GameObject
/// MUST start ACTIVE in the Hierarchy -- Unity skips Awake()/Start() entirely
/// for any component on a GameObject that begins disabled, which would
/// silently prevent the auto-popup below from ever running. Awake() hides it
/// in code instead (see its comment) when there's nothing to auto-diagnose,
/// achieving the same "starts hidden" result without that trap.
///
/// OPENS AUTOMATICALLY: Start() opens this the instant the scene loads, if
/// there's an active ticket that still needs diagnosing (RepairTicketMode.
/// IsActive && !IsDiagnosed) -- no click-to-open station needed. If you still
/// have an in-scene "diagnostic station" object from an earlier setup, its
/// InteractableObject.onInteract -> OpenForActiveTicket() wiring is now just
/// a manual "reopen" fallback, useful if the player closes this before
/// finishing and wants to bring it back.
///
/// PLAYER MOVEMENT/CAMERA/CURSOR: locked via PlayerInputLock + Cursor while
/// this is open (see AcquireInputLockIfNeeded/ReleaseInputLockIfHeld),
/// exactly like this project's other full-screen station panels
/// (COC3StationController). Movement resumes on whichever comes first: the
/// player picks the correct diagnosis (released the instant that happens,
/// even before closing), or the player clicks Close.
///
/// Does nothing useful outside RepairTicketMode.IsActive -- Open() simply
/// won't find a ticket to show and logs a warning, so it's safe to leave
/// wired up permanently.
/// </summary>
public class TicketDiagnosticPanelView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text symptomText;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Options")]
    [Tooltip("One button per possible diagnosis option, in the same order as TicketData.diagnosisOptions. Unused buttons (if a ticket has fewer options) are hidden automatically.")]
    [SerializeField] private List<Button> optionButtons = new List<Button>();
    [SerializeField] private List<TMP_Text> optionLabels = new List<TMP_Text>();

    [Tooltip("Optional -- shown once IsDiagnosed is true, e.g. a green checkmark or 'Diagnosis confirmed' banner.")]
    [SerializeField] private GameObject diagnosedBanner;

    [Tooltip("Optional -- close button.")]
    [SerializeField] private Button closeButton;

    private TicketData _ticket;
    private bool hasInputLock;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);

        // IMPORTANT: this GameObject (and panelRoot, if it's the same object) must start ACTIVE
        // in the Hierarchy for Awake()/Start() to run at all -- Unity never calls either on a
        // GameObject that begins disabled, for any component on it. So hiding is decided HERE,
        // in code, instead of via the Hierarchy checkbox: if there's nothing to auto-diagnose,
        // hide immediately, before the first frame renders. If there IS something to diagnose,
        // leave it alone and let Start() (below) open it properly.
        if (!(RepairTicketMode.IsActive && !RepairTicketMode.IsDiagnosed))
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }

    private void Start()
    {
        // Auto-popup: the whole point of this change is that diagnosing is no longer something
        // the player has to remember to go trigger. If the ticket doesn't require diagnosis, or
        // this is a return visit where it was already correctly diagnosed earlier
        // (RepairTicketMode.Begin's alreadyDiagnosed param -- see TicketDetailPanelView),
        // IsDiagnosed is already true and this correctly stays closed.
        if (RepairTicketMode.IsActive && !RepairTicketMode.IsDiagnosed)
        {
            OpenForActiveTicket();
        }
    }

    private void OnDestroy()
    {
        // Safety net: if the scene unloads while this is still open (e.g. the player somehow
        // triggers Return to Hub without closing it first), release the lock anyway. Without
        // this, PlayerInputLock's count would stay above zero for the rest of the play session --
        // it's a static that survives scene loads -- silently freezing movement in every scene
        // after this one, with no obvious cause.
        ReleaseInputLockIfHeld();
    }

    /// <summary>Opens automatically on scene start (see Start() above). Also callable manually --
    /// e.g. from a leftover in-scene diagnostic station's interaction -- to reopen after the
    /// player closes this before finishing.</summary>
    public void OpenForActiveTicket()
    {
        if (!RepairTicketMode.IsActive || RepairTicketMode.ActiveTicket == null)
        {
            Debug.LogWarning("[TicketDiagnosticPanelView] No active repair ticket -- nothing to diagnose.");
            return;
        }

        _ticket = RepairTicketMode.ActiveTicket;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (symptomText != null) symptomText.text = _ticket.symptomText;
        if (feedbackText != null) feedbackText.text = string.Empty;

        AcquireInputLockIfNeeded();

        RefreshOptions();
        RefreshDiagnosedBanner();
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        ReleaseInputLockIfHeld();
    }

    private void RefreshOptions()
    {
        var options = _ticket.diagnosisOptions;

        for (int i = 0; i < optionButtons.Count; i++)
        {
            if (optionButtons[i] == null) continue;

            bool hasOption = options != null && i < options.Length;
            optionButtons[i].gameObject.SetActive(hasOption);
            if (!hasOption) continue;

            var option = options[i];
            if (i < optionLabels.Count && optionLabels[i] != null)
                optionLabels[i].text = option.label;

            // Already diagnosed correctly this visit -- leave the options visible for
            // reference, but there's nothing further to pick.
            optionButtons[i].interactable = !RepairTicketMode.IsDiagnosed;

            int capturedIndex = i;
            optionButtons[i].onClick.RemoveAllListeners();
            optionButtons[i].onClick.AddListener(() => HandleOptionPicked(capturedIndex));
        }
    }

    private void HandleOptionPicked(int index)
    {
        if (_ticket?.diagnosisOptions == null || index >= _ticket.diagnosisOptions.Length) return;

        var option = _ticket.diagnosisOptions[index];

        if (feedbackText != null) feedbackText.text = option.feedback;

        if (option.isCorrect)
        {
            RepairTicketMode.MarkDiagnosed();
            RefreshOptions();
            RefreshDiagnosedBanner();

            // Regain movement the instant it's correctly diagnosed -- the player doesn't have
            // to also click Close to get control back, per the "finished OR closed" requirement.
            // The panel itself is left open so they can still read the confirmation/feedback.
            ReleaseInputLockIfHeld();
        }
        else
        {
            // Wrong guess: nothing is locked, the player can try another option.
            // Hook COC4AssessmentManager.ReportMistake(...) here later if you want
            // wrong diagnoses to cost points during a graded run.
            Debug.Log($"[TicketDiagnosticPanelView] Incorrect diagnosis: '{option.label}'.");
        }
    }

    private void RefreshDiagnosedBanner()
    {
        if (diagnosedBanner != null)
            diagnosedBanner.SetActive(RepairTicketMode.IsDiagnosed);
    }

    // ---------------------------------------------------------------
    // Movement/camera/cursor lock -- same pattern as this project's other
    // full-screen station panels (see COC3StationController).
    // ---------------------------------------------------------------

    private void AcquireInputLockIfNeeded()
    {
        if (hasInputLock) return;
        hasInputLock = true;

        PlayerInputLock.Acquire();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ReleaseInputLockIfHeld()
    {
        if (!hasInputLock) return;
        hasInputLock = false;

        PlayerInputLock.Release();

        // Only re-lock the cursor if nothing else (a dialog, another station) still needs it
        // free -- otherwise this would yank the cursor away from whatever else is still open.
        if (!PlayerInputLock.IsLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}