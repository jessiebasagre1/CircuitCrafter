using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// "Is the learner typing right now?" - ask this before acting on any letter key.
///
/// WHY IT IS NEEDED
/// Input.GetKeyDown(KeyCode.E) is true whether the learner meant "leave this machine" or was typing
/// the E in SERVER01. Unity's old Input class reads the physical keyboard and knows nothing about
/// the text field that is busy receiving the same keystroke, so without a check like this every
/// letter that appears in a hostname, a domain name or a username doubles as a hotkey.
///
/// It only bites once a shortcut moves onto a letter. Escape is safe because nobody types Escape
/// into a field; E is not.
///
/// HOW IT DECIDES
/// The EventSystem tracks which object currently has keyboard focus, and both TMP_InputField and
/// the legacy InputField make themselves the selected object while they are being typed into -
/// including when something focuses them from code with ActivateInputField(). So the selected
/// object, plus that field's own isFocused, is the authoritative answer and costs one component
/// lookup.
///
/// USE IT LIKE THIS:
///     if (Input.GetKeyDown(exitKey) &amp;&amp; !TypingGuard.IsTyping) Leave();
///
/// Deliberately general and in Core rather than inside COC 3, because COC 2's terminal and COC 4's
/// ticket notes have exactly the same exposure the moment either grows a letter shortcut.
/// </summary>
public static class TypingGuard
{
    /// <summary>True while a text field has keyboard focus, so letter keys belong to it.</summary>
    public static bool IsTyping
    {
        get
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;

            GameObject selected = es.currentSelectedGameObject;
            if (selected == null) return false;

            // isFocused as well as selected: a field the learner has clicked away from can stay the
            // selected object for a frame, and treating that as typing would swallow a real
            // keypress - which is the opposite failure and just as annoying.
            TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;

            InputField legacy = selected.GetComponent<InputField>();
            if (legacy != null && legacy.isFocused) return true;

            return false;
        }
    }

    /// <summary>The focused field's object name, or "nothing". For diagnostics only.</summary>
    public static string FocusedFieldName()
    {
        EventSystem es = EventSystem.current;
        GameObject selected = es != null ? es.currentSelectedGameObject : null;
        return selected != null ? selected.name : "nothing";
    }
}
