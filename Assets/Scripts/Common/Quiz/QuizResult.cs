using System;

/// <summary>
/// Plain summary of one completed quiz. Passed to QuizManager.OnQuizCompleted
/// so future systems (achievements, badges, analytics) can react without any
/// changes to QuizManager or QuizView.
/// </summary>
[Serializable]
public class QuizResult
{
    public string taskId;
    public int totalQuestions;
    public int correctAnswers;
    public int pointsEarned;

    public bool IsPerfect => totalQuestions > 0 && correctAnswers == totalQuestions;
}