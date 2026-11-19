using TMPro;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // The gameplay and results panels use the same counter, fill and colors.
    internal sealed class ScoreMeterView
    {
        internal RectTransform Root, Fill;
        internal TMP_Text Count;
        internal UnityEngine.UI.Image FillImage;
        internal static ScoreMeterView Create(Transform parent)
        {
            var view = new ScoreMeterView();
            view.Root = Rect("Score meter", parent, Vector2.zero, new Vector2(420,112));
            var background = view.Root.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(.045f,.06f,.085f,.94f); background.raycastTarget = false;
            Label(view.Root,"SCORE",new Vector2(0,34),new Vector2(380,26),19);
            view.Count = Label(view.Root,"",new Vector2(0,5),new Vector2(400,38),30);
            var track = Rect("Score track",view.Root,new Vector2(0,-33),new Vector2(380,15));
            var image = track.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(.22f,.25f,.3f); image.raycastTarget = false;
            view.Fill = Rect("Score progress",track,Vector2.zero,Vector2.zero);
            view.Fill.anchorMin = Vector2.zero; view.Fill.anchorMax = Vector2.one;
            view.Fill.offsetMin = view.Fill.offsetMax = Vector2.zero;
            view.FillImage = view.Fill.gameObject.AddComponent<UnityEngine.UI.Image>(); view.FillImage.raycastTarget=false;
            return view;
        }
        internal void SetScore(long current, int target)
        {
            target = Mathf.Max(1,target);
            Count.text=$"{current:N0} / {target:N0}";
            Fill.anchorMax=new Vector2(Mathf.Clamp01((float)((double)current/target)),1);
            FillImage.color=current>=target?new Color(.35f,.95f,.55f):new Color(.25f,.75f,1);
        }
        private static RectTransform Rect(string title,Transform parent,Vector2 position,Vector2 size)
        {
            var rect=(RectTransform)new GameObject(title,typeof(RectTransform)).transform;
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
        }
        private static TMP_Text Label(Transform parent,string value,Vector2 position,Vector2 size,float fontSize)
        {
            var rect=Rect(value==""?"Score count":value,parent,position,size);
            var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
            var style=MenuButtonStyle.Shared;if(style?.font!=null)text.font=style.font;
            text.text=value;text.fontSize=fontSize;text.fontStyle=FontStyles.Bold;text.alignment=TextAlignmentOptions.Center;
            text.color=Color.white;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;
            text.enableAutoSizing=true;text.fontSizeMin=16;text.fontSizeMax=fontSize;return text;
        }
    }
}
