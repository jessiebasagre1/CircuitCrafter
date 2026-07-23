using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A reusable data asset holding one tutorial/dialog popup's lines.
/// Create one via Assets > Create > Tutorial > Dialog Sequence for each
/// tutorial moment (e.g. "How to remove the motherboard", "PC fully disassembled").
/// </summary>
[CreateAssetMenu(fileName = "New Dialog Sequence", menuName = "Tutorial/Dialog Sequence")]
public class DialogSequence : ScriptableObject
{
    public List<DialogLine> lines = new List<DialogLine>();
}

[System.Serializable]
public class DialogLine
{
    public string speakerName = "Guide";
    [TextArea(3, 6)] public string text;
}