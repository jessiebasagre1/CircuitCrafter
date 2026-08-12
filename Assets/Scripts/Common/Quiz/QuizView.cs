using System;
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
    [Tooltip("Optional — changes the Next button's label to something like 'See Results' on the last question.")]
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
    private bool acceptingInput;
    private bool settingUpToggles;

    private void Awake()
    {
        if (quizPanel != null) quizPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);

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

        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (quizPanel != null) quizPanel.SetActive(true);

        ShowQuestion();
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

        for (int i = 0; i < choiceToggles.Count; i++)
        {
            bool inUse = q.choices != null && i < q.choices.Length;
            choiceToggles[i].gameObject.SetActive(inUse);
            if (!inUse) continue;

            if (i < choiceLabels.Count && choiceLabels[i] != null)
                choiceLabels[i].text = q.choices[i];

            choiceToggles[i].SetIsOnWithoutNotify(false);
            choiceToggles[i].interactable = true;
            choiceToggles[i].group = choiceToggleGroup;

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

        var result = new QuizResult
        {
            taskId = currentTaskId,
            totalQuestions = currentQuestions?.Count ?? 0,
            correctAnswers = correctCount,
            pointsEarned = pointsEarned
        };

        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (resultsSummaryText != null)
            resultsSummaryText.text = $"You got {result.correctAnswers}/{result.totalQuestions} correct!\n+{result.pointsEarned} points";

        // Fires now so QuizManager can score/log immediately; the player
        // closes the results panel manually via CloseResults().
        onFinished?.Invoke(result);
        onFinished = null;
    }

    private void CloseResults()
    {
        if (resultsPanel != null) resultsPanel.SetActive(false);
    }
}