// Shows a level's authored slots and its saved-move picker on one contextual panel.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace BeatTheBoss.Poses
{
    public sealed class MoveLoadoutPanel : MonoBehaviour
    {
        private const int MovesPerPage = 6, SlotsPerPage = 3;
        [SerializeField] private UnityEngine.UI.Button buttonPrefab;
        [SerializeField] private RectTransform backplate;
        [SerializeField] private LevelSelectPanel owner;

        private readonly Choice[] slots = new Choice[SlotsPerPage], moves = new Choice[MovesPerPage];
        private readonly TMP_Text[] slotTitles = new TMP_Text[SlotsPerPage];
        private LevelLoadout level;
        private LevelLoadout.Slot selectedSlot;
        private string[] moveNames = Array.Empty<string>();
        private string listError;
        private int slotPage, movePage;
        private bool built, picking;
        private RectTransform slotsView, movesView;
        private TMP_Text title, context, backLabel, slotPageLabel, movePageLabel, slotStatus, moveStatus;
        private UnityEngine.UI.Button previousSlot, nextSlot, previousMove, nextMove, clearSlot, clearAll;

        public void Configure(LevelLoadout definition)
        {
            definition.Validate();
            level = definition; selectedSlot = null;
            slotPage = movePage = 0; picking = false;
            if (built) RefreshMoves();
        }
        private void OnEnable()
        {
            if (level == null) return;
            if (!built) Build();
            RefreshMoves();
        }
        private void Build()
        {
            title = PosePanelLayout.CreateLabel(backplate, "Loadout", 38, 38, 506, 64, 44, true);
            backLabel = Button(backplate, "Close", 568, 38, 144, 64, () =>
            {
                if (picking) { picking = false; UpdatePanel(); }
                else owner.HideLoadout();
            }, 26);
            context = PosePanelLayout.CreateLabel(backplate, "", 38, 112, 674, 46, 28);
            slotsView = Rect(backplate, "Level slots", 38, 178, 674, 784);
            movesView = Rect(backplate, "Choose saved move", 38, 178, 674, 784);
            for (int i = 0; i < SlotsPerPage; i++)
            {
                int row = i;
                slotTitles[i] = PosePanelLayout.CreateLabel(slotsView, "", 0, i*170, 674, 42, 30, true);
                slots[i] = new Choice(Button(slotsView, "", 0, i*170+44, 674, 76, () => SelectSlot(row)));
            }
            previousSlot = Control(Button(slotsView, "Previous", 0, 490, 180, 62, () => ChangeSlotPage(-1)));
            nextSlot = Control(Button(slotsView, "Next", 494, 490, 180, 62, () => ChangeSlotPage(1)));
            slotPageLabel = PosePanelLayout.CreateLabel(slotsView, "", 194, 490, 286, 62, 26);
            slotPageLabel.alignment = TextAlignmentOptions.Midline;
            slotStatus = PosePanelLayout.CreateLabel(slotsView, "", 0, 570, 674, 92, 26);
            clearAll = Control(Button(slotsView, "Clear level loadout", 0, 700, 674, 76, ResetLoadout));

            Button(movesView, "Refresh", 0, 0, 324, 64, RefreshMoves);
            clearSlot = Control(Button(movesView, "Unassign slot", 338, 0, 336, 64, () =>
            {
                MoveLoadout.Assign(level, selectedSlot, ""); picking = false; UpdatePanel();
            }));
            for (int i = 0; i < MovesPerPage; i++)
            {
                int row = i;
                moves[i] = new Choice(Button(movesView, "", 0, 82+i*88, 674, 76, () => AssignMove(row)));
            }
            previousMove = Control(Button(movesView, "Previous", 0, 616, 180, 62, () => ChangeMovePage(-1)));
            nextMove = Control(Button(movesView, "Next", 494, 616, 180, 62, () => ChangeMovePage(1)));
            movePageLabel = PosePanelLayout.CreateLabel(movesView, "", 194, 616, 286, 62, 26);
            movePageLabel.alignment = TextAlignmentOptions.Midline;
            moveStatus = PosePanelLayout.CreateLabel(movesView, "", 0, 704, 674, 76, 25);
            built = true;
        }
        private void RefreshMoves()
        {
            listError = null;
            try { moveNames = MoveStore.List(); }
            catch (Exception error) when (AnimationStore.IsFileError(error))
            {
                moveNames = Array.Empty<string>(); listError = "Couldn't load saved moves. " + error.Message;
            }
            movePage = Mathf.Clamp(movePage, 0, MovePageCount-1);
            UpdatePanel();
        }
        private void SelectSlot(int row)
        {
            int index = slotPage*SlotsPerPage+row;
            if (index >= level.Slots.Count) return;
            selectedSlot = level.Slots[index]; picking = true;
            int selected = FindMove(MoveLoadout.Get(level, selectedSlot));
            movePage = selected >= 0 ? selected/MovesPerPage : 0;
            UpdatePanel();
        }
        private void AssignMove(int row)
        {
            int index = movePage*MovesPerPage+row;
            if (selectedSlot == null || index >= moveNames.Length) return;
            MoveLoadout.Assign(level, selectedSlot, moveNames[index]);
            picking = false; UpdatePanel();
        }
        public void ResetLoadout()
        {
            if (level == null) return;
            MoveLoadout.Clear(level); selectedSlot = null; picking = false;
            if (built) UpdatePanel();
        }
        private void ChangeSlotPage(int offset)
        { slotPage = Mathf.Clamp(slotPage+offset, 0, SlotPageCount-1); UpdatePanel(); }
        private void ChangeMovePage(int offset)
        { movePage = Mathf.Clamp(movePage+offset, 0, MovePageCount-1); UpdatePanel(); }
        private int FindMove(string name) => Array.FindIndex(moveNames, value => string.Equals(value,name,StringComparison.OrdinalIgnoreCase));
        private int SlotPageCount => Mathf.Max(1,(level.Slots.Count+SlotsPerPage-1)/SlotsPerPage);
        private int MovePageCount => Mathf.Max(1,(moveNames.Length+MovesPerPage-1)/MovesPerPage);
        private void UpdatePanel()
        {
            if (!built || level == null) return;
            slotsView.gameObject.SetActive(!picking); movesView.gameObject.SetActive(picking);
            title.text = picking ? "Choose a move" : "Loadout";
            context.text = picking ? selectedSlot.Title : level.Title;
            backLabel.text = picking ? "Back" : "Close";
            bool assignedAny = false;
            foreach (var slot in level.Slots) assignedAny |= !string.IsNullOrEmpty(MoveLoadout.Get(level,slot));
            clearAll.interactable = assignedAny;
            for (int i = 0; i < SlotsPerPage; i++)
            {
                int index = slotPage*SlotsPerPage+i; bool visible = index < level.Slots.Count;
                slotTitles[i].gameObject.SetActive(visible); slots[i].Button.gameObject.SetActive(visible);
                if (!visible) continue;
                var slot = level.Slots[index]; string name = MoveLoadout.Get(level,slot);
                slotTitles[i].text = slot.Title;
                slots[i].Label.text = string.IsNullOrEmpty(name) ? "Choose a move  ›" :
                    listError == null && FindMove(name)<0 ? name+" (missing)  ›" : name+"  ›";
                slots[i].Highlight.enabled = slot == selectedSlot;
            }
            string selectedName = selectedSlot == null ? "" : MoveLoadout.Get(level,selectedSlot);
            for (int i = 0; i < MovesPerPage; i++)
            {
                int index = movePage*MovesPerPage+i; bool visible = index < moveNames.Length;
                moves[i].Button.gameObject.SetActive(visible);
                if (!visible) continue;
                moves[i].Label.text = moveNames[index];
                moves[i].Highlight.enabled = string.Equals(selectedName,moveNames[index],StringComparison.OrdinalIgnoreCase);
            }
            Pager(previousSlot,nextSlot,slotPageLabel,slotPage,SlotPageCount);
            Pager(previousMove,nextMove,movePageLabel,movePage,MovePageCount);
            clearSlot.interactable = selectedSlot != null && !string.IsNullOrEmpty(selectedName);
            slotStatus.text = level.Slots.Count == 0 ? "This level has no move slots." : "Select a slot to choose its move. Assignments save automatically.";
            moveStatus.text = listError ?? (moveNames.Length == 0 ? "No saved moves. Record a move, then refresh." : "Choose a move for this slot.");
        }
        private static void Pager(UnityEngine.UI.Button previous,UnityEngine.UI.Button next,TMP_Text text,int page,int count)
        {
            previous.gameObject.SetActive(count>1); next.gameObject.SetActive(count>1);
            previous.interactable = page>0; next.interactable = page+1<count;
            text.text = count>1 ? $"{page+1} / {count}" : "";
        }
        private TMP_Text Button(Transform parent,string text,float x,float y,float width,float height,UnityAction action,int font=30)
        {
            var label = PosePanelLayout.CreateButton(buttonPrefab,parent,text,x,y,width,height,action);
            label.fontSize = font; label.enableAutoSizing = false;
            return label;
        }
        private static UnityEngine.UI.Button Control(TMP_Text label) => label.GetComponentInParent<UnityEngine.UI.Button>(true);
        private static RectTransform Rect(Transform parent,string name,float x,float y,float width,float height)
        {
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false); rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(width,height);
            return rect;
        }
        private sealed class Choice
        {
            internal readonly TMP_Text Label;
            internal readonly UnityEngine.UI.Button Button;
            internal readonly UnityEngine.UI.Image Highlight;
            internal Choice(TMP_Text label)
            {
                Label = label; Button = Control(label);
                var marker = Rect(Button.transform,"Selection",0,0,5,0);
                marker.anchorMin = Vector2.zero; marker.anchorMax = Vector2.up;
                marker.offsetMin = new Vector2(5,12); marker.offsetMax = new Vector2(10,-12);
                Highlight = marker.gameObject.AddComponent<UnityEngine.UI.Image>();
                Highlight.color = PosePanelLayout.Accent; Highlight.raycastTarget = false;
            }
        }
    }
}
