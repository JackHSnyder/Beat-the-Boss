// Gives the parent layout group a button width based on its TextMesh Pro label.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(LayoutElement))]
public sealed class FitButtonToLabel : MonoBehaviour
{
    private const float HorizontalPadding = 32f;

    [SerializeField] private TMP_Text label;

    private LayoutElement layout;

    private void OnEnable()
    {
        layout = GetComponent<LayoutElement>();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        RefreshWidth();
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }

    private void OnTextChanged(Object changedText)
    {
        if (changedText == label)
            RefreshWidth();
    }

    private void RefreshWidth()
    {
        if (label != null)
            layout.preferredWidth = Mathf.Ceil(label.preferredWidth + HorizontalPadding);
    }
}
