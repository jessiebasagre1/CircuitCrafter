using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Generic quiz UI. Works for any QuizSetData/QuizQuestionData -- put one
/// of these per scene (wired into QuizManager's Quiz View field) and it
/// handles however many questions and however many choices each one has,
/// up to the number of choice button slots you wire up in the Inspector.
/// </summary>
public class QuizView : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject quizPanel;
    [SerializeField] private GameObject resultsPanel;

    [Header("Intro (optional)")]
    [Tooltip("Optional -- e.g. a \"Quiz Time!\" panel shown briefly right when the quiz starts, before the first question appears. Leave empty to skip straight to the first question, same as before this existed.")]
    [SerializeField] private GameObject introPanel;
    [Tooltip("Optional -- if assigned, introPanel fades in/out using this instead of popping instantly. Leave empty for a plain instant show/hide.")]
    [SerializeField] private CanvasGroup introCanvasGroup;
    [Tooltip("Total time introPanel stays on screen (fade in + hold + fade out all included) before the first question appears.")]
    [SerializeField] private float introDuration = 1.5f;
    [Tooltip("How long the fade in and fade out each take. Only used if Intro Canvas Group is assigned. Must be less than half of Intro Duration.")]
    [SerializeField] private float introFadeDuration = 0.3f;

    [Header("Question UI")]
    [SerializeField] private TMP_Text progressText;   // e.g. "Question 2 / 3"
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private TMP_Text feedbackText;   // "Correct!" / "Incorrect." + explanation

    [Header("Choice Toggles")]
    [SerializeField] private List<Toggle> choiceToggles = new List<Toggle>();
    [SerializeField] private List<TMP_Text> choiceLabels = new List<TMP_Text>();
    [SerializeField] private ToggleGroup choiceToggleGroup;

    [Header("Navigation")]
    [Tooltip("Shown after the player answers, so they can read the explanation before moving on.")]
    [SerializeField] private Button nextButton;
    [Tooltip("Optional � changes the Next button's label to something like 'See Results' on the last question.")]
    [SerializeField] private TMP_Text nextButtonLabel;

    [Header("Results UI")]
    [SerializeField] private TMP_Text resultsSummaryText;
    [SerializeField] private Button closeResultsButton;

    private List<QuizQuestionData> currentQuestions;
    private int currentIndex;
    private int correctCount;
    private int pointsEarned;
    private string currentTaskId;
    private Action<QuizResult> onFinished;
    private QuizResult pendingResult;
    private bool acceptingInput;
    private bool settingUpToggles;

    /// <summary>True when BeginQuiz switched this object on, so CloseResults knows to switch it
    /// back off. A canvas that was already active before the quiz is left exactly as it was.</summary>
    private bool activatedSelf;

    private void Awake()
    {
        if (quizPanel != null) quizPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);

        // A group that will not switch off cannot be cleared between questions, and a stale
        // "on" toggle is what stops a later answer registering. The player still cannot
        // un-answer, because answering locks every toggle anyway.
        if (choiceToggleGroup != null) choiceToggleGroup.allowSwitchOff = true;

        if (closeResultsButton != null)
            closeResultsButton.onClick.AddListener(CloseResults);

        if (nextButton != null)
        {
            nextButton.onClick.AddListener(HandleNextClicked);
            nextButton.gameObject.SetActive(false);
        }
    }

    /// <summary>Starts a quiz. Called by QuizManager -- not meant to be
    /// called directly by other gameplay code.</summary>
    public void BeginQuiz(string taskId, List<QuizQuestionData> questions, Action<QuizResult> onFinishedCallback)
    {
        currentTaskId = taskId;
        currentQuestions = questions;
        onFinished = onFinishedCallback;
        currentIndex = 0;
        correctCount = 0;
        pointsEarned = 0;

        // Switch ourselves on before anything else.
        //
        // A view asked to show a quiz has to be able to show it. In COC1 this object happened to
        // be left active in the scene and only its child panels were toggled, so nothing here ever
        // needed to think about it - but the moment the canvas starts out inactive (which is how
        // most people would build it, and how the COC2 copy was set up), StartCoroutine below
        // throws "Coroutine couldn't be started because the game object is inactive" and the quiz
        // silently never appears. The camera has already switched by then, so the learner is left
        // looking at an empty quiz background.
        //
        // Self-activating means the canvas can be left switched off in the editor, which is also
        // the tidier way to author it.
        activatedSelf = !gameObject.activeSelf;
        if (activatedSelf) gameObject.SetActive(true);

        if (resultsPanel != null) resultsPanel.SetActive(false);

        // quizPanel deliberately stays hidden here -- PlayIntroThenShowFirstQuestion()
        // shows introPanel first (if assigned) and only reveals quizPanel once
        // that's done, so the question never flashes up underneath the intro.
        StartCoroutine(PlayIntroThenShowFirstQuestion());
    }

    /// <summary>Shows introPanel (if assigned) for introDuration -- fading via
    /// introCanvasGroup if one's assigned, otherwise just popping instantly --
    /// then reveals quizPanel and shows the first question. If introPanel is
    /// left unassigned this skips straight to the first question, exactly as
    /// BeginQuiz() used to behave on its own.</summary>
    private IEnumerator PlayIntroThenShowFirstQuestion()
    {
        if (introPanel != null)
        {
            introPanel.SetActive(true);

            if (introCanvasGroup != null)
            {
                float fade = Mathf.Clamp(introFadeDuration, 0f, introDuration / 2f);
                yield return Fade(introCanvasGroup, 0f, 1f, fade);
                // Realtime, like the fades below. A dialog with Pause Game While Open ticked
                // leaves Time.timeScale at 0, and a scaled wait would then never end - the intro
                // would sit on screen forever with no question behind it.
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, introDuration - fade * 2f));
                yield return Fade(introCanvasGroup, 1f, 0f, fade);
            }
            else
            {
                yield return new WaitForSecondsRealtime(introDuration);
            }

            introPanel.SetActive(false);
        }

        if (quizPanel != null) quizPanel.SetActive(true);
        ShowQuestion();
        Relayout(quizPanel);
    }

    /// <summary>
    /// Forces the layout groups and content size fitters under a panel to recalculate NOW.
    ///
    /// THIS IS THE FIX FOR "THE QUIZ LAYOUT IS A MESS IN PLAY MODE".
    ///
    /// Unity does not run layout on an inactive GameObject. The quiz canvas spends the whole
    /// module switched off, so the arrangement visible in the Scene view is whatever was last
    /// computed - and the first time the quiz is shown, the panel is drawn from those stale sizes.
    /// Nested fitters make it worse: each pass only resolves one level, so a fitter inside a layout
    /// group inside another fitter needs several passes before it settles, and a single frame's
    /// automatic pass does not give it them.
    ///
    /// TMP is the other half. A TMP_Text does not know its own preferred size until it has
    /// generated its mesh, which happens at the end of the frame - so a question longer than the
    /// last one reports the OLD width to the layout group that is trying to size around it. That is
    /// why the mess changes shape depending on which question you are on.
    ///
    /// So: rebuild immediately for the common case, then once more next frame, by which time TMP
    /// has measured its text properly. Two cheap passes, on a panel that appears a handful of times
    /// per module - not something that needs to be clever.
    /// </summary>
    private void Relayout(GameObject panel)
    {
        if (panel == null || !panel.activeInHierarchy) return;

        RebuildNow(panel);

        // Only if we are actually running - a coroutine cannot start on an inactive object, and
        // BeginQuiz may not have switched us on yet in some orders.
        if (isActiveAndEnabled) StartCoroutine(RebuildNextFrame(panel));
    }

    private static void RebuildNow(GameObject panel)
    {
        if (panel == null || !panel.activeInHierarchy) return;

        RectTransform root = panel.transform as RectTransform;
        if (root == null) return;

        // Flush any pending canvas work first, so the rebuild below sees current text.
        Canvas.ForceUpdateCanvases();

        // Deepest first. A parent sized by a fitter can only be right once its children are, and
        // GetComponentsInChildren returns parents before children - hence the reverse walk.
        RectTransform[] all = panel.GetComponentsInChildren<RectTransform>(false);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            if (all[i] != null) LayoutRebuilder.ForceRebuildLayoutImmediate(all[i]);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    private IEnumerator RebuildNextFrame(GameObject panel)
    {
        yield return null;                  // let TMP generate its mesh and report real sizes
        RebuildNow(panel);
    }

    private IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
    {
        group.alpha = from;

        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        group.alpha = to;
    }

    private void ShowQuestion()
    {
        if (currentQuestions == null || currentIndex >= currentQuestions.Count)
        {
            FinishQuiz();
            return;
        }

        if (nextButton != null) nextButton.gameObject.SetActive(false);

        QuizQuestionData q = currentQuestions[currentIndex];

        if (progressText != null)
            progressText.text = $"Question {currentIndex + 1} / {currentQuestions.Count}";

        if (questionText != null)
            questionText.text = q.questionText;

        if (feedbackText != null)
            feedbackText.text = "";

        settingUpToggles = true;

        // ---- 1. SHOW THE RIGHT NUMBER OF CHOICES, AND LABEL THEM -------------------------
        //
        // This happens BEFORE the clearing step below, and the order is not cosmetic. A Toggle
        // unregisters itself from its ToggleGroup while it is inactive and registers again when it
        // is switched back on, so a group asked to clear itself only ever reaches the toggles that
        // are active at that moment. Showing every choice this question needs first means the
        // clear that follows reaches all of them.
        for (int i = 0; i < choiceToggles.Count; i++)
        {
            if (choiceToggles[i] == null) continue;

            bool inUse = q.choices != null && i < q.choices.Length;
            choiceToggles[i].gameObject.SetActive(inUse);
            if (!inUse) continue;

            if (i < choiceLabels.Count && choiceLabels[i] != null)
                choiceLabels[i].text = q.choices[i];
        }

        // ---- 2. CLEAR THE PREVIOUS ANSWER - THROUGH THE GROUP, NOT AROUND IT -------------
        //
        // The original bug: SetIsOnWithoutNotify(false) does exactly what it says - it changes the
        // toggle and tells NOBODY, the ToggleGroup included. The group went on believing the
        // toggle picked on the last question was still the one that was on. Click that same
        // position on a later question and uGUI considers it already on, onValueChanged never
        // fires, and the answer does nothing. That was question 3 refusing to accept a click.
        //
        // SetAllTogglesOff is the supported way to empty a group: it walks the group's own
        // registered toggles, lifts its Allow Switch Off rule for the duration, and turns each one
        // off properly, so the group's idea of the world matches the screen.
        //
        // NOTHING HERE TOUCHES Toggle.group. An earlier attempt detached and re-attached the
        // toggles every question, and that stopped them responding at all: the group setter
        // re-registers a toggle only while that toggle is active, so any toggle hidden by a
        // shorter question - or reached before its parent had been switched on - silently dropped
        // out of the group and never came back. The grouping is authored in the Inspector; it is
        // left exactly as authored.
        if (choiceToggleGroup != null)
        {
            choiceToggleGroup.SetAllTogglesOff();
        }
        else
        {
            foreach (var t in choiceToggles)
                if (t != null) t.SetIsOnWithoutNotify(false);
        }

        // ---- 3. ARM THEM ----------------------------------------------------------------
        for (int i = 0; i < choiceToggles.Count; i++)
        {
            if (choiceToggles[i] == null) continue;
            if (!choiceToggles[i].gameObject.activeSelf) continue;

            // Belt and braces. If anything did survive step 2 still ticked, it would look answered
            // and refuse to be clicked.
            if (choiceToggles[i].isOn) choiceToggles[i].SetIsOnWithoutNotify(false);

            choiceToggles[i].interactable = true;

            int choiceIndex = i;
            choiceToggles[i].onValueChanged.RemoveAllListeners();
            choiceToggles[i].onValueChanged.AddListener(isOn =>
            {
                if (settingUpToggles) return;
                if (isOn) HandleAnswerSelected(choiceIndex);
            });
        }

        settingUpToggles = false;
        acceptingInput = true;

        // The question text and the choice labels have just changed, and so has how many choices
        // are showing. Every one of those drives the layout, so it has to be recalculated here and
        // not only when the panel first opens.
        Relayout(quizPanel);
    }

    private void HandleAnswerSelected(int choiceIndex)
    {
        if (!acceptingInput) return;
        acceptingInput = false;

        QuizQuestionData q = currentQuestions[currentIndex];
        bool isCorrect = choiceIndex == q.correctChoiceIndex;

        if (isCorrect)
        {
            correctCount++;
            pointsEarned += q.points;
        }

        if (feedbackText != null)
        {
            string result = isCorrect ? "Correct!" : "Incorrect.";
            feedbackText.text = string.IsNullOrEmpty(q.explanation) ? result : $"{result} {q.explanation}";
        }

        // Also lock the toggles themselves so the player can't keep clicking
        // around while reading the explanation, even though HandleAnswerSelected
        // already ignores it via acceptingInput.
        foreach (var toggle in choiceToggles)
            if (toggle != null) toggle.interactable = false;

        currentIndex++;

        if (nextButton != null)
        {
            if (nextButtonLabel != null)
                nextButtonLabel.text = currentIndex >= currentQuestions.Count ? "See Results" : "Next";
            nextButton.gameObject.SetActive(true);
        }
    }

    private void HandleNextClicked()
    {
        if (nextButton != null) nextButton.gameObject.SetActive(false);
        ShowQuestion();
    }

    private void FinishQuiz()
    {
        if (quizPanel != null) quizPanel.SetActive(false);

        pendingResult = new QuizResult
        {
            taskId = currentTaskId,
            totalQuestions = currentQuestions?.Count ?? 0,
            correctAnswers = correctCount,
            pointsEarned = pointsEarned
        };

        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (resultsSummaryText != null)
            resultsSummaryText.text = $"You got {pendingResult.correctAnswers}/{pendingResult.totalQuestions} correct!\n+{pendingResult.pointsEarned} points";

        // The results panel has been switched off for the whole quiz, so its layout is as stale as
        // the question panel's was.
        Relayout(resultsPanel);

        // NOTE: onFinished is deliberately NOT invoked here. QuizManager uses
        // onFinished to score the result AND to tell QuizCameraController to
        // switch the camera back (station/player) via NotifyQuizFinished().
        // Firing that the moment results appear would yank the camera away
        // while the player is still reading their score. Instead we wait
        // until they actually dismiss the results panel -- see CloseResults().
    }

    private void CloseResults()
    {
        // EVERYTHING this view turned on, it turns off again.
        //
        // Hiding only the results panel was enough in COC1, where the last question was cleared
        // for a different reason: the quiz canvas sat in QuizCameraController's Enable On Quiz
        // list, and NotifyQuizFinished put it back to the state it was snapshotted in. Anywhere
        // that list does not name this canvas, the question panel simply stayed on screen after
        // the score was dismissed - the quiz appeared not to close.
        //
        // Leaving the tidying to whoever wired the camera controller was the mistake. The view
        // owns what it shows.
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (quizPanel != null) quizPanel.SetActive(false);
        if (introPanel != null) introPanel.SetActive(false);

        acceptingInput = false;
        if (nextButton != null) nextButton.gameObject.SetActive(false);

        // Only if BeginQuiz switched it on. A canvas that was already active - COC1's, which holds
        // other things besides the quiz - is left exactly as it was found.
        if (activatedSelf)
        {
            activatedSelf = false;
            gameObject.SetActive(false);
        }

        // Reported last, and to a local copy: this object may now be inactive, and the callback
        // runs off to QuizManager, which scores the result and switches the camera back. Doing it
        // in this order means the quiz UI is already gone by the time the world comes back, rather
        // than being drawn over it for a frame.
        Action<QuizResult> callback = onFinished;
        QuizResult result = pendingResult;

        onFinished = null;
        pendingResult = null;

        callback?.Invoke(result);
    }
}