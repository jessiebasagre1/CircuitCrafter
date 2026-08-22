using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The browser panel: one background Image that swaps sprite per step, with a small set of
/// live controls repositioned on top of it.
///
/// ATTACH THIS TO: the browser panel itself. Built and wired by
/// Tools > COC 1 > Rufus Setup Builder.
///
/// ONE PANEL, NOT SEVEN
/// Every screen in the sequence - the browser, the Rufus site, the download bar, the UAC
/// prompt - is the same panel with a different sprite behind it and the controls moved. Seven
/// separate panels would be seven sets of references to keep in sync, and adding an eighth
/// screen later would mean building another one. This way a new screen is one new asset.
///
/// CONTROLS ARE PLACED BY ANCHOR, NOT BY PIXEL
/// Each step's Rect is normalised 0-1, and Place() turns that into anchorMin/anchorMax with
/// zero offsets. The control then tracks the panel at any size or resolution, which a pixel
/// position would not.
/// </summary>
public class RufusSetupView : MonoBehaviour
{
    [SerializeField] private RufusSetupManager manager;

    [Header("Background")]
    [Tooltip("Fills the panel. Its sprite is swapped for every step.")]
    [SerializeField] private Image background;

    [Tooltip("The rect controls are positioned inside - normally the same rect as the background.")]
    [SerializeField] private RectTransform contentRoot;

    [Tooltip("Parent of the screens drawn in Unity UI. Exactly one child is switched on per " +
             "step, chosen by the step's Drawn Screen name.")]
    [SerializeField] private Transform screensRoot;

    [Header("Instruction strip")]
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private TMP_Text hintText;

    [Header("Click Hotspot")]
    [SerializeField] private Button hotspotButton;
    [SerializeField] private TMP_Text hotspotLabel;
    [SerializeField] private Image hotspotOutline;

    [Header("Decoy")]
    [SerializeField] private Button decoyButton;
    [SerializeField] private TMP_Text decoyLabel;

    [Header("Type URL")]
    [SerializeField] private TMP_InputField urlInput;
    [SerializeField] private Button goButton;

    [Header("Timed")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text progressLabel;

    [Header("Finished")]
    [Tooltip("Shown if the browser is reopened after Rufus is already installed.")]
    [SerializeField] private GameObject alreadyInstalledBanner;

    private void OnEnable()
    {
        if (manager == null) manager = RufusSetupManager.Instance;

        if (manager == null)
        {
            Debug.LogWarning("[RufusSetupView] No RufusSetupManager. Is it in the scene, on an " +
                             "always-active object?", this);
            return;
        }

        manager.OnStepChanged += HandleStepChanged;
        manager.OnStepProgress += HandleProgress;
        manager.OnWrongAnswer += HandleWrongAnswer;
        manager.OnSequenceComplete += HandleComplete;
        manager.OnAlreadyComplete += HandleAlreadyComplete;

        if (hotspotButton != null) hotspotButton.onClick.AddListener(HandleHotspotClicked);
        if (decoyButton != null) decoyButton.onClick.AddListener(HandleDecoyClicked);
        if (goButton != null) goButton.onClick.AddListener(HandleGoClicked);

        // Enter also submits, because nobody types a web address and then reaches for a button.
        if (urlInput != null) urlInput.onSubmit.AddListener(HandleUrlSubmitted);

        HideAll();
        if (alreadyInstalledBanner != null) alreadyInstalledBanner.SetActive(false);

        // The learner is sitting at a PC typing, not walking. Without this, WASD drive both
        // the address bar and the character - "rufus.ie" walks them backwards on the 's'.
        PlayerInputLock.Acquire();
        _holdingInput = true;

        manager.Begin();
    }

    private bool _holdingInput;

    private void OnDisable()
    {
        // Release exactly once, however this object went away - closed, disabled, or destroyed
        // with the scene. Releasing without the flag would drop somebody else's lock.
        if (_holdingInput)
        {
            PlayerInputLock.Release();
            _holdingInput = false;
        }

        if (manager != null)
        {
            manager.CancelPendingBegin();
            manager.OnStepChanged -= HandleStepChanged;
            manager.OnStepProgress -= HandleProgress;
            manager.OnWrongAnswer -= HandleWrongAnswer;
            manager.OnSequenceComplete -= HandleComplete;
            manager.OnAlreadyComplete -= HandleAlreadyComplete;
        }

        if (hotspotButton != null) hotspotButton.onClick.RemoveListener(HandleHotspotClicked);
        if (decoyButton != null) decoyButton.onClick.RemoveListener(HandleDecoyClicked);
        if (goButton != null) goButton.onClick.RemoveListener(HandleGoClicked);
        if (urlInput != null) urlInput.onSubmit.RemoveListener(HandleUrlSubmitted);
    }

    // ==================================================================

    private void HandleStepChanged(RufusSetupStepData step)
    {
        HideAll();

        ShowScreen(step);

        if (instructionText != null) instructionText.text = step.instruction;
        if (hintText != null) hintText.gameObject.SetActive(false);

        switch (step.kind)
        {
            case RufusSetupStepData.StepKind.ClickHotspot:
                ShowHotspot(step);
                break;

            case RufusSetupStepData.StepKind.TypeUrl:
                ShowUrlBar(step);
                break;

            case RufusSetupStepData.StepKind.Timed:
                ShowProgress(step);
                break;
        }
    }

    /// <summary>
    /// Picks what the learner looks at: either one of the screens drawn in Unity UI, or a
    /// background screenshot. A step uses one or the other, never both.
    /// </summary>
    private void ShowScreen(RufusSetupStepData step)
    {
        bool drawn = !string.IsNullOrEmpty(step.drawnScreen);

        if (screensRoot != null)
        {
            bool found = false;

            for (int i = 0; i < screensRoot.childCount; i++)
            {
                var child = screensRoot.GetChild(i);
                bool match = drawn && child.name == step.drawnScreen;
                child.gameObject.SetActive(match);
                if (match) found = true;
            }

            // A name that matches nothing would just show a blank panel, which looks identical
            // to a missing sprite. Say which name failed instead of leaving it to guesswork.
            if (drawn && !found)
                Debug.LogWarning($"[RufusSetupView] Step '{step.stepId}' asks for drawn screen " +
                                 $"'{step.drawnScreen}', but no child of '{screensRoot.name}' has " +
                                 "that name. Rebuild the browser panel.", this);
        }

        if (background != null)
        {
            background.sprite = step.background;

            // Off when a drawn screen is in charge, and off when the sprite is missing - a step
            // with no image should look unfinished rather than showing the previous step's.
            background.enabled = !drawn && step.background != null;
        }
    }

    /// <summary>
    /// Keeps an invisible click target clickable.
    ///
    /// A Graphic with a fully transparent mesh gets culled by its CanvasRenderer, and
    /// GraphicRaycaster skips culled graphics - so an alpha-0 hotspot is active, correctly
    /// placed, has raycastTarget ticked, and still cannot be clicked. Doing this here as well
    /// as in the builder means a panel built before the fix repairs itself on the next step
    /// change, with no rebuild.
    /// </summary>
    private static void KeepClickable(Selectable control)
    {
        if (control == null) return;

        var cr = control.GetComponent<CanvasRenderer>();
        if (cr != null) cr.cullTransparentMesh = false;

        var img = control.GetComponent<Image>();
        if (img != null && img.color.a <= 0f)
            img.color = new Color(img.color.r, img.color.g, img.color.b, 1f / 255f);
    }

    private void ShowHotspot(RufusSetupStepData step)
    {
        // A decoy is optional: an empty rect means this screen has only one button worth clicking.
        bool hasDecoy = step.decoyHotspot.width > 0f && step.decoyHotspot.height > 0f;

        if (decoyButton != null)
        {
            decoyButton.gameObject.SetActive(hasDecoy);
            if (hasDecoy)
            {
                Place(decoyButton.transform as RectTransform, step.decoyHotspot);
                if (decoyLabel != null) decoyLabel.text = step.decoyLabel ?? "";

                // Front of the controls layer. uGUI raycasts in reverse hierarchy order, so a
                // sibling created later wins the click even when it is fully transparent - and
                // an invisible control that loses a raycast looks exactly like a dead button.
                decoyButton.transform.SetAsLastSibling();
                KeepClickable(decoyButton);
            }
        }

        if (hotspotButton != null)
        {
            Place(hotspotButton.transform as RectTransform, step.hotspot);
            hotspotButton.gameObject.SetActive(true);

            if (hotspotLabel != null) hotspotLabel.text = step.hotspotLabel ?? "";
            if (hotspotOutline != null) hotspotOutline.enabled = step.showHotspotOutline;

            // AFTER the decoy, so the correct answer is the frontmost of the two. If the two
            // rects ever overlap, a click on the shared area should count as right rather than
            // wrong - the learner did find the real target.
            //
            // Doing this at runtime rather than trusting the build order means it stays true
            // however the hierarchy is reordered afterwards.
            hotspotButton.transform.SetAsLastSibling();
            KeepClickable(hotspotButton);
        }
    }

    private void ShowUrlBar(RufusSetupStepData step)
    {
        if (urlInput != null)
        {
            Place(urlInput.transform as RectTransform, step.inputRect);
            urlInput.gameObject.SetActive(true);
            urlInput.text = "";

            if (urlInput.placeholder is TMP_Text ph) ph.text = step.urlPlaceholder ?? "";

            urlInput.Select();
            urlInput.ActivateInputField();
        }

        if (goButton != null)
        {
            Place(goButton.transform as RectTransform, step.goRect);
            goButton.gameObject.SetActive(true);
        }
    }

    private void ShowProgress(RufusSetupStepData step)
    {
        if (progressBar != null)
        {
            Place(progressBar.transform as RectTransform, step.progressRect);
            progressBar.gameObject.SetActive(true);
            progressBar.value = 0f;
        }

        if (progressLabel != null)
        {
            // The label is a sibling of the bar, deliberately - it sits over the same rect but
            // is not switched off with it.
            Place(progressLabel.transform as RectTransform, step.progressRect);
            progressLabel.gameObject.SetActive(true);
            progressLabel.text = step.progressLabel ?? "";
        }
    }

    private void HandleProgress(float t)
    {
        if (progressBar != null) progressBar.value = t;
    }

    private void HandleWrongAnswer(RufusSetupStepData step, string what)
    {
        if (hintText == null) return;

        if (string.IsNullOrEmpty(step.hint))
        {
            hintText.gameObject.SetActive(false);
            return;
        }

        hintText.text = step.hint;
        hintText.gameObject.SetActive(true);

        // Give them the field back with the wrong answer cleared, rather than making them
        // select-all-delete before they can try again.
        if (step.kind == RufusSetupStepData.StepKind.TypeUrl && urlInput != null)
        {
            urlInput.text = "";
            urlInput.Select();
            urlInput.ActivateInputField();
        }
    }

    private void HandleComplete()
    {
        HideAll();
        if (instructionText != null) instructionText.text = "Rufus is installed.";
    }

    private void HandleAlreadyComplete()
    {
        HideAll();
        if (background != null) background.enabled = false;
        if (screensRoot != null)
            for (int i = 0; i < screensRoot.childCount; i++)
                screensRoot.GetChild(i).gameObject.SetActive(false);
        if (alreadyInstalledBanner != null) alreadyInstalledBanner.SetActive(true);
        if (instructionText != null)
            instructionText.text = "Rufus is already installed on this PC. Close this window and " +
                                   "open it from the desktop.";
    }

    private void HideAll()
    {
        if (hotspotButton != null) hotspotButton.gameObject.SetActive(false);
        if (decoyButton != null) decoyButton.gameObject.SetActive(false);
        if (urlInput != null) urlInput.gameObject.SetActive(false);
        if (goButton != null) goButton.gameObject.SetActive(false);
        if (progressBar != null) progressBar.gameObject.SetActive(false);
        if (progressLabel != null) progressLabel.gameObject.SetActive(false);
    }

    // ==================================================================

    private void HandleHotspotClicked()
    {
        if (manager != null) manager.SubmitHotspot();
    }

    private void HandleDecoyClicked()
    {
        if (manager != null) manager.SubmitDecoy();
    }

    private void HandleGoClicked()
    {
        if (manager != null && urlInput != null) manager.SubmitUrl(urlInput.text);
    }

    private void HandleUrlSubmitted(string value)
    {
        if (manager != null) manager.SubmitUrl(value);
    }

    // ==================================================================

    /// <summary>
    /// Turns a normalised top-left Rect into anchors on the panel.
    ///
    /// Unity's anchors run from the BOTTOM-left with y going up; the step data is written
    /// top-left with y going down, because that is how anyone reads coordinates off a
    /// screenshot. The 1f - (y + height) below is the whole of that conversion.
    /// </summary>
    private static void Place(RectTransform rt, Rect n)
    {
        if (rt == null) return;

        rt.anchorMin = new Vector2(n.x, 1f - (n.y + n.height));
        rt.anchorMax = new Vector2(n.x + n.width, 1f - n.y);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}