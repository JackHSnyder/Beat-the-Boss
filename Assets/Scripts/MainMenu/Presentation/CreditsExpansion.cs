using TMPro;
using UnityEngine;

// Keeps the introduction editable as ordinary text boxes while flowing the credits
// below the visible paragraphs and a text-only expand/collapse button.
public sealed class CreditsExpansion : MonoBehaviour
{
    [SerializeField] private TMP_Text introduction;
    [SerializeField] private TMP_Text additionalParagraphs;
    [SerializeField] private TMP_Text credits;
    [SerializeField] private UnityEngine.UI.Button toggle;
    [SerializeField] private TMP_Text toggleLabel;
    [SerializeField, Min(0)] private float paragraphGap = 16;
    [SerializeField, Min(0)] private float creditsGap = 24;
    private bool expanded;

    private void Awake() => toggle.onClick.AddListener(Toggle);
    private void OnEnable() => SetExpanded(false);
    private void OnDestroy()
    {
        if (toggle != null) toggle.onClick.RemoveListener(Toggle);
    }

    public void Toggle() => SetExpanded(!expanded);

    public void SetExpanded(bool value)
    {
        expanded = value;
        if (introduction == null || additionalParagraphs == null || credits == null || toggle == null || toggleLabel == null)
            return;

        float bottom = -introduction.rectTransform.anchoredPosition.y + FitHeight(introduction);
        PlaceAt(additionalParagraphs.rectTransform, bottom + paragraphGap);
        additionalParagraphs.gameObject.SetActive(expanded);
        if (expanded) bottom += paragraphGap + FitHeight(additionalParagraphs);
        var buttonRect = (RectTransform)toggle.transform;
        PlaceAt(buttonRect, bottom + 8);
        toggleLabel.text = expanded ? "Show Less" : "Show More";
        PlaceAt(credits.rectTransform, bottom + 8 + buttonRect.rect.height + creditsGap);
        FitHeight(credits);
    }

    private static float FitHeight(TMP_Text text)
    {
        float height = text.GetPreferredValues(text.text, text.rectTransform.rect.width, Mathf.Infinity).y;
        text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        return height;
    }

    private static void PlaceAt(RectTransform rect, float top)
    {
        Vector2 position = rect.anchoredPosition;
        position.y = -top;
        rect.anchoredPosition = position;
    }
}
