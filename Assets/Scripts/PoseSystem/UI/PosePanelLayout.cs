// Builds recording controls from Meta UI Set prefabs and provides shared text and layout styles.
// PoseGamePanel and its individual panels use it to construct their controls.

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    internal sealed class PosePanelLayout
    {
        public static readonly Color Surface = new Color32(26, 29, 37, 215);
        public static readonly Color Foreground = new Color32(246, 247, 251, 255);
        public static readonly Color Muted = new Color32(197, 202, 215, 255);
        public static readonly Color Accent = new Color32(154, 205, 255, 255);
        public static readonly Color Warning = new Color32(255, 205, 148, 255);

        private readonly Button primaryButton;
        private readonly Button secondaryButton;
        private readonly GameObject nameField;
        private readonly Slider sliderPrefab;

        public PosePanelLayout(Button primaryButton, Button secondaryButton, GameObject nameField, Slider sliderPrefab)
        {
            this.primaryButton = primaryButton;
            this.secondaryButton = secondaryButton;
            this.nameField = nameField;
            this.sliderPrefab = sliderPrefab;
        }

        public RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, x, y, width, height);
            return rect;
        }

        public TMP_Text Label(Transform parent, string value, float x, float y, float width, float height,
            int size = 20, bool bold = false)
        {
            TMP_Text label = Rect(parent, value, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.color = Foreground;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.richText = false;
            label.overflowMode = TextOverflowModes.Truncate;
            return label;
        }

        public Image Fill(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public TMP_Text Button(Transform parent, string value, float x, float y, float width, float height,
            UnityAction action, bool primary = false)
        {
            Button button = Object.Instantiate(primary ? primaryButton : secondaryButton, parent, false);
            button.name = string.IsNullOrEmpty(value) ? "List row" : value;
            Place((RectTransform)button.transform, x, y, width, height);
            button.onClick.AddListener(action);

            // Keep Meta's visual hierarchy so its hover, press, and disabled animations still target it.
            var content = (RectTransform)button.transform.Find("Content");
            content.sizeDelta = new Vector2(content.sizeDelta.x, height - 4);
            var elements = content.Find("Background/Elements");
            elements.Find("Icon").gameObject.SetActive(false);
            elements.Find("Gap").gameObject.SetActive(false);
            var text = (RectTransform)elements.Find("Text");
            text.Find("Subtitle").gameObject.SetActive(false);
            elements.GetComponent<HorizontalLayoutGroup>().enabled = false;
            text.GetComponent<VerticalLayoutGroup>().enabled = false;
            Stretch(text);
            var label = text.Find("Label").GetComponent<TMP_Text>();
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Midline;
            label.fontSize = 18;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            label.text = value;
            return label;
        }

        public TMP_InputField NameField(Transform parent, float x, float y, float width, float height)
        {
            var root = Object.Instantiate(nameField, parent, false);
            Place((RectTransform)root.transform, x, y, width, height);
            root.GetComponent<VerticalLayoutGroup>().enabled = false;
            root.transform.Find("Title").gameObject.SetActive(false);
            root.transform.Find("HelperText").gameObject.SetActive(false);
            var field = root.GetComponentInChildren<TMP_InputField>(true);
            Stretch((RectTransform)field.transform);
            field.characterLimit = 64;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.textComponent.fontSize = 18;
            if (field.placeholder is TMP_Text placeholder)
                placeholder.text = "Enter a name";
            return field;
        }

        public Slider Slider(Transform parent, string name, float x, float y, float width, float min, float max,
            UnityAction<float> changed)
        {
            Slider slider = Object.Instantiate(sliderPrefab, parent, false);
            slider.name = name;
            var rect = (RectTransform)slider.transform;
            float height = rect.rect.height;
            Place(rect, x, y + (46 - height) / 2, width, height);
            // Keep the whole 46-pixel row usable for pointing, even though the UI Set track is shorter.
            Image hitArea = Fill(rect, Color.clear);
            hitArea.raycastTarget = true;
            hitArea.raycastPadding = new Vector4(0, (height - 46) / 2, 0, (height - 46) / 2);
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(changed);
            return slider;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
