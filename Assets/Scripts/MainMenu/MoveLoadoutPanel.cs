// Displays saved moves and lets the player assign them to the three gameplay slots.

using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    public sealed class MoveLoadoutPanel : MonoBehaviour
    {
        private const int MovesPerPage = 6;
        private static readonly string[] SlotTitles = { "Attack", "Dash Left", "Dash Right" };

        [SerializeField] private Button buttonPrefab;
        [SerializeField] private RectTransform movesBackplate;

        private readonly Choice[] slotButtons = new Choice[SlotTitles.Length];
        private readonly Choice[] moveButtons = new Choice[MovesPerPage];
        private string[] moveNames = Array.Empty<string>();
        private MoveSlot? selectedSlot;
        private int page;
        private Button previous;
        private Button next;
        private TMP_Text pageNumber;
        private TMP_Text status;
        private string listError;

        private void OnEnable()
        {
            if (movesBackplate == null || buttonPrefab == null)
            {
                Debug.LogError("Assign the Moves backplate and button prefab to the loadout panel.", this);
                return;
            }
            if (status == null)
                BuildPanel();
            RefreshMoves();
        }

        public void ResetLoadout()
        {
            MoveLoadout.Clear();
            selectedSlot = null;
            if (status != null)
                UpdatePanel();
        }

        private void BuildPanel()
        {
            for (int i = 0; i < SlotTitles.Length; i++)
            {
                MoveSlot slot = MoveLoadout.Slots[i];
                float top = i * 140;
                Label(transform, SlotTitles[i], top, 36, 26, TextAlignmentOptions.MidlineLeft);
                slotButtons[i] = new Choice(AddButton(transform, "", top + 38, 78, () => SelectSlot(slot)));
            }

            RectTransform moves = CreateRect(movesBackplate, "Saved moves list");
            // The backplate's layout group positions the authored header. Anchor the list below it.
            moves.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            moves.anchorMin = Vector2.zero;
            moves.anchorMax = Vector2.one;
            moves.offsetMin = new Vector2(24, 24);
            moves.offsetMax = new Vector2(-24, -86);
            var refresh = AddButton(moves, "Refresh", 0, 52, RefreshMoves, 150).GetComponentInParent<Button>();
            AlignRight((RectTransform)refresh.transform);

            // Keep the list reachable with the same point-and-click input as the other VR menus.
            for (int i = 0; i < moveButtons.Length; i++)
            {
                int row = i;
                moveButtons[i] = new Choice(AddButton(moves, "", 66 + i * 60, 54, () => AssignMove(row)));
            }

            previous = AddButton(moves, "Previous", 434, 48, () => ChangePage(-1), 150)
                .GetComponentInParent<Button>();
            next = AddButton(moves, "Next", 434, 48, () => ChangePage(1), 150)
                .GetComponentInParent<Button>();
            AlignRight((RectTransform)next.transform);
            pageNumber = Label(moves, "", 434, 48, 21, TextAlignmentOptions.Midline);
            status = Label(moves, "", 492, 44, 22, TextAlignmentOptions.MidlineLeft);
        }

        private void RefreshMoves()
        {
            listError = null;
            try
            {
                moveNames = MoveStore.List();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                moveNames = Array.Empty<string>();
                listError = "Couldn't load saved moves.";
                Debug.LogWarning(error.Message, this);
            }
            page = Mathf.Clamp(page, 0, PageCount - 1);
            UpdatePanel();
        }

        private void SelectSlot(MoveSlot slot)
        {
            selectedSlot = slot;
            int moveIndex = FindMove(MoveLoadout.Get(slot));
            if (moveIndex >= 0)
                page = moveIndex / MovesPerPage;
            UpdatePanel();
        }

        private void AssignMove(int row)
        {
            int index = page * MovesPerPage + row;
            if (selectedSlot is MoveSlot slot && index < moveNames.Length)
            {
                MoveLoadout.Assign(slot, moveNames[index]);
                UpdatePanel();
            }
        }

        private int FindMove(string name) => Array.FindIndex(moveNames,
            move => string.Equals(move, name, StringComparison.OrdinalIgnoreCase));

        private int PageCount => Mathf.Max(1, (moveNames.Length + MovesPerPage - 1) / MovesPerPage);

        private void ChangePage(int offset)
        {
            page = Mathf.Clamp(page + offset, 0, PageCount - 1);
            UpdatePanel();
        }

        private void UpdatePanel()
        {
            string selectedMove = selectedSlot is MoveSlot selected ? MoveLoadout.Get(selected) : "";
            for (int i = 0; i < slotButtons.Length; i++)
            {
                MoveSlot slot = MoveLoadout.Slots[i];
                string assigned = MoveLoadout.Get(slot);
                slotButtons[i].Label.text = assigned.Length == 0 ? "Unassigned" :
                    listError == null && FindMove(assigned) < 0 ? assigned + " (missing)" : assigned;
                slotButtons[i].Highlight.enabled = selectedSlot == slot;
            }

            for (int row = 0; row < moveButtons.Length; row++)
            {
                Choice choice = moveButtons[row];
                int index = page * MovesPerPage + row;
                choice.Button.gameObject.SetActive(index < moveNames.Length);
                if (index >= moveNames.Length)
                    continue;
                choice.Label.text = moveNames[index];
                choice.Button.interactable = selectedSlot.HasValue;
                choice.Highlight.enabled = string.Equals(selectedMove, moveNames[index],
                    StringComparison.OrdinalIgnoreCase);
            }

            bool paged = PageCount > 1;
            previous.gameObject.SetActive(paged);
            next.gameObject.SetActive(paged);
            previous.interactable = page > 0;
            next.interactable = page + 1 < PageCount;
            pageNumber.text = paged ? $"{page + 1} / {PageCount}" : "";
            status.text = listError ?? (moveNames.Length == 0 ? "No saved moves. Record one, then refresh." :
                selectedSlot.HasValue ? "" : "Select a slot, then choose a move.");
        }

        private TMP_Text AddButton(Transform parent, string value, float top, float height, UnityAction click,
            float width = 0)
        {
            TMP_Text label = PosePanelLayout.CreateButton(buttonPrefab, parent, value, 0, top, width, height, click);
            label.fontSize = 26;
            var button = (RectTransform)label.GetComponentInParent<Button>().transform;
            if (width == 0)
                StretchWidth(button);

            // Stretch the Meta button's background and its hit area to match the row.
            var content = (RectTransform)button.Find("Content");
            button.GetComponent<HorizontalLayoutGroup>().enabled = false;
            content.GetComponent<HorizontalLayoutGroup>().enabled = false;
            Stretch(content, 2);
            Stretch((RectTransform)content.Find("Background"), 0);
            return label;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = -rect.offsetMin;
        }

        private static TMP_Text Label(Transform parent, string value, float top, float height, int size,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(parent, value);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, -top);
            rect.sizeDelta = new Vector2(0, height);
            StretchWidth(rect);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value;
            label.alignment = alignment;
            label.fontSize = size;
            label.color = PosePanelLayout.Foreground;
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void StretchWidth(RectTransform rect)
        {
            rect.anchorMax = new Vector2(1, 1);
            rect.sizeDelta = new Vector2(0, rect.sizeDelta.y);
        }

        private static void AlignRight(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        }

        private sealed class Choice
        {
            public TMP_Text Label { get; }
            public Button Button { get; }
            public Image Highlight { get; }

            public Choice(TMP_Text label)
            {
                Label = label;
                Button = label.GetComponentInParent<Button>();
                RectTransform marker = CreateRect(Button.transform, "Selection");
                marker.anchorMin = Vector2.zero;
                marker.anchorMax = Vector2.up;
                marker.offsetMin = new Vector2(-11, 9);
                marker.offsetMax = new Vector2(-5, -9);
                Highlight = marker.gameObject.AddComponent<Image>();
                Highlight.color = PosePanelLayout.Accent;
                Highlight.raycastTarget = false;
                Highlight.enabled = false;
            }
        }
    }
}
