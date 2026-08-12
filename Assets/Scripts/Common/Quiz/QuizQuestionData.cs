using UnityEngine;

/// <summary>
/// A single quiz question. Reusable across every COC module -- create as
/// many of these as you like via Assets > Create > COC > Quiz > Question,
/// then group them into a QuizSetData for whichever task should trigger them.
/// </summary>
[CreateAssetMenu(menuName = "COC/Quiz/Question", fileName = "NewQuizQuestion")]
public class QuizQuestionData : ScriptableObject
{
    [TextArea(2, 5)]
    public string questionText;

    [Tooltip("2 or more answer choices. The player picks one.")]
    public string[] choices = new string[4];

    [Tooltip("Index into Choices that is the correct answer (0-based).")]
    public int correctChoiceIndex;

    [Tooltip("Points awarded if answered correctly. Customizable per question - harder questions can be worth more.")]
    public int points = 10;

    [TextArea(2, 4)]
    [Tooltip("Optional. Shown after answering, right or wrong, to explain the correct answer.")]
    public string explanation;
}