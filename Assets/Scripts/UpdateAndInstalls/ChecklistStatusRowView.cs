using UnityEngine;
using TMPro;

/// <summary>
/// One read-only row on the Service Report checklist: a label plus a
/// check/cross indicator. No button -- this screen only reports status, the
/// actual work happens in the other 4 apps. Put this on a prefab and assign
/// it to ServiceReportView.rowPrefab.
/// </summary>
public class ChecklistStatusRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private GameObject checkIcon;
    [SerializeField] private GameObject crossIcon;

    public void Setup(string label, bool complete)
    {
        if (labelText != null) labelText.text = label;
        if (checkIcon != null) checkIcon.SetActive(complete);
        if (crossIcon != null) crossIcon.SetActive(!complete);
    }
}
