using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BeatTheBoss.Poses
{
    // Shared by recording editors. The dimmed content cannot receive pointer, gaze, or navigation input.
    internal sealed class RecordingConfirmation
    {
        private readonly CanvasGroup content;
        private readonly RectTransform overlay;
        private readonly TMP_Text title, message, acceptLabel;
        private Action accept;
        private Action alternative;
        private readonly TMP_Text alternativeLabel;
        private readonly RectTransform cancelButton, acceptButton, alternativeButton;
        private readonly float cardWidth;
        private readonly CanvasGroup[] additional;
        private (float alpha,bool interactable,bool blocks)[] previous;
        internal bool IsOpen => overlay.gameObject.activeSelf;

        internal RecordingConfirmation(PosePanelLayout layout, RectTransform panel, params RectTransform[] otherPanels)
        {
            additional=new CanvasGroup[otherPanels.Length];
            for (int i=0;i<otherPanels.Length;i++)
            {
                var group=otherPanels[i].GetComponent<CanvasGroup>();
                if (group==null) group=otherPanels[i].gameObject.AddComponent<CanvasGroup>();
                additional[i]=group;
            }
            var body = layout.Rect(panel, "Recording content", 0, 0, panel.rect.width, panel.rect.height);
            for (int i = panel.childCount-1; i >= 0; i--)
            {
                Transform child = panel.GetChild(i);
                if (child != body) child.SetParent(body, false);
            }
            content = body.gameObject.AddComponent<CanvasGroup>();
            overlay = layout.Rect(panel, "Confirmation prompt", 0, 0, panel.rect.width, panel.rect.height);
            layout.Fill(overlay, new Color(0,0,0,.4f)).raycastTarget = true;
            float width=Mathf.Min(600,panel.rect.width-32);
            cardWidth=width;
            var card = layout.Rect(overlay, "Confirmation card", (panel.rect.width-width)/2, (panel.rect.height-288)/2, width, 288);
            layout.Fill(card, new Color(.10f,.12f,.16f,1)).raycastTarget = true;
            title = layout.Label(card, "", 24, 18, width-48, 48, 26, true);
            message = layout.Label(card, "", 24, 78, width-48, 118, 20);
            message.alignment = TextAlignmentOptions.TopLeft;
            float buttonWidth=(width-64)/2;
            cancelButton=(RectTransform)layout.Button(card, "Cancel", 24, 220, buttonWidth, 44, Cancel).GetComponentInParent<UnityEngine.UI.Button>(true).transform;
            acceptLabel = layout.Button(card, "Confirm", 40+buttonWidth, 220, buttonWidth, 44, Confirm, true);
            acceptButton=(RectTransform)acceptLabel.GetComponentInParent<UnityEngine.UI.Button>(true).transform;
            alternativeLabel=layout.Button(card,"Save as new",40+buttonWidth,220,buttonWidth,44,ConfirmAlternative,true);
            alternativeButton=(RectTransform)alternativeLabel.GetComponentInParent<UnityEngine.UI.Button>(true).transform;
            alternativeButton.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);
        }
        internal void Show(string heading, string text, string button, Action confirmed)
        {
            if (IsOpen) return;
            float width=(cardWidth-64)/2;
            PlaceButton(cancelButton,24,width); PlaceButton(acceptButton,40+width,width);
            alternativeButton.gameObject.SetActive(false);
            acceptButton.GetComponent<MenuButtonVisual>()?.Configure(MenuButtonStyle.Shared,MenuButtonStyle.Role.Primary);
            Open(heading,text,button,confirmed);
        }
        internal void ShowChoice(string heading,string text,string first,Action firstAction,string second,Action secondAction)
        {
            if (IsOpen) return;
            float width=(cardWidth-72)/3;
            PlaceButton(cancelButton,24,width); PlaceButton(acceptButton,36+width,width); PlaceButton(alternativeButton,48+width*2,width);
            alternativeButton.gameObject.SetActive(true);alternativeLabel.text=second;alternative=secondAction;
            acceptButton.GetComponent<MenuButtonVisual>()?.Configure(MenuButtonStyle.Shared,MenuButtonStyle.Role.Secondary);
            Open(heading,text,first,firstAction);
        }
        private static void PlaceButton(RectTransform button,float x,float width)
        {
            button.anchoredPosition=new Vector2(x,-220);button.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
        }
        private void Open(string heading,string text,string button,Action confirmed)
        {
            title.text = heading; message.text = text; acceptLabel.text = button; accept = confirmed;
            content.alpha = .35f; content.interactable = content.blocksRaycasts = false;
            previous=new (float,bool,bool)[additional.Length];
            for (int i=0;i<additional.Length;i++)
            {
                var group=additional[i]; previous[i]=(group.alpha,group.interactable,group.blocksRaycasts);
                group.alpha*=.35f; group.interactable=group.blocksRaycasts=false;
            }
            overlay.gameObject.SetActive(true); overlay.SetAsLastSibling();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
        internal void Tick()
        {
            if (IsOpen && Keyboard.current?.escapeKey.wasPressedThisFrame == true) Cancel();
        }
        internal void Cancel() { Close(); }
        private void Close()
        {
            accept = alternative = null; overlay.gameObject.SetActive(false);
            content.alpha = 1; content.interactable = content.blocksRaycasts = true;
            if (previous!=null) for (int i=0;i<additional.Length;i++)
            { if(additional[i]!=null) { additional[i].alpha=previous[i].alpha; additional[i].interactable=previous[i].interactable; additional[i].blocksRaycasts=previous[i].blocks; } }
            previous=null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
        private void Confirm()
        {
            Action action = accept; Close(); action?.Invoke();
        }
        private void ConfirmAlternative()
        {
            Action action=alternative; Close(); action?.Invoke();
        }
    }
}
