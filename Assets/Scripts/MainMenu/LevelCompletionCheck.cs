using UnityEngine;

// A small vector check avoids depending on a checkmark glyph in every UI font.
internal static class LevelCompletionCheck
{
    internal static RectTransform Create(Transform parent)
    {
        var root=(RectTransform)new GameObject("Completed checkmark",typeof(RectTransform)).transform;
        root.SetParent(parent,false); root.sizeDelta=new Vector2(48,48);
        Segment(root,new Vector2(-16,0),new Vector2(-5,-11));
        Segment(root,new Vector2(-5,-11),new Vector2(17,14));
        return root;
    }
    private static void Segment(Transform parent,Vector2 from,Vector2 to)
    {
        var rect=(RectTransform)new GameObject("Check stroke",typeof(RectTransform)).transform;
        rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.anchoredPosition=(from+to)*.5f;rect.sizeDelta=new Vector2(Vector2.Distance(from,to),6);
        rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg);
        var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.4f,1,.6f);image.raycastTarget=false;
    }
}
