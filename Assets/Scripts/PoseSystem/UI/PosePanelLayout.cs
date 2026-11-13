using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    internal sealed class PosePanelLayout
    {
        public static readonly Color Background = new Color32(21, 28, 36, 255);
        public static readonly Color Surface = new Color32(36, 47, 58, 255);
        public static readonly Color Foreground = new Color32(242, 247, 250, 255);
        public static readonly Color Muted = new Color32(170, 190, 205, 255);
        public static readonly Color Accent = new Color32(147, 231, 199, 255);
        public static readonly Color Warning = new Color32(255, 204, 141, 255);

        private readonly Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public Text Label(Transform parent, string value, float x, float y, float width, float height,
            int size = 20, bool bold = false)
        {
            Text label = Rect(parent, value, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = value;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.color = Foreground;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.supportRichText = false;
            return label;
        }

        public Image Fill(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public Text Button(Transform parent, string value, float x, float y, float width, float height,
            UnityAction action, bool primary = false)
        {
            RectTransform rect = Rect(parent, value, x, y, width, height);
            var image = Fill(rect, primary ? Accent : Surface);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            Configure(button, image);
            button.onClick.AddListener(action);
            Text label = Label(rect, value, 4, 0, width - 8, height, 19, true);
            label.alignment = TextAnchor.MiddleCenter;
            label.color = primary ? Background : Foreground;
            return label;
        }

        public InputField NameField(Transform parent, float x, float y, float width, float height)
        {
            RectTransform rect = Rect(parent, "Pose name", x, y, width, height);
            Image image = Fill(rect, Surface);
            image.raycastTarget = true;
            var field = rect.gameObject.AddComponent<InputField>();
            Configure(field, image);
            field.characterLimit = 64;
            field.lineType = InputField.LineType.SingleLine;
            field.textComponent = Label(rect, "", 12, 0, width - 24, height);
            return field;
        }

        public Slider Slider(Transform parent, string name, float x, float y, float width, float min, float max,
            UnityAction<float> changed)
        {
            RectTransform rect = Rect(parent, name, x, y, width, 46);
            Image target = Fill(rect, Surface);
            target.raycastTarget = true;
            Fill(Rect(rect, "Track", 13, 20, width - 26, 6), Muted);
            // Inset the slider travel so the handle stays inside the track at both endpoints.
            RectTransform travel = Rect(rect, "Handle area", 13, 0, width - 26, 46);
            travel.anchorMin = Vector2.zero;
            travel.anchorMax = Vector2.one;
            travel.offsetMin = new Vector2(13, 0);
            travel.offsetMax = new Vector2(-13, 0);
            RectTransform handle = Rect(travel, "Handle", 0, 6, 26, 34);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.anchorMin = new Vector2(0, 0);
            handle.anchorMax = new Vector2(0, 1);
            handle.sizeDelta = new Vector2(26, -12);
            handle.anchoredPosition = Vector2.zero;
            Fill(handle, Accent);

            var slider = rect.gameObject.AddComponent<Slider>();
            Configure(slider, target);
            slider.handleRect = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(changed);
            return slider;
        }

        private static void Configure(Selectable control, Graphic target)
        {
            control.targetGraphic = target;
            control.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = control.colors;
            colors.highlightedColor = new Color32(220, 255, 239, 255);
            colors.pressedColor = new Color32(153, 190, 173, 255);
            colors.disabledColor = new Color32(122, 132, 138, 145);
            control.colors = colors;
        }
    }
}
