using System;
using BeatTheBoss.Poses;
using TMPro;
using UnityEngine;

public sealed class LevelCompletionPanel : MonoBehaviour
{
    [SerializeField] private MMPanelSwitch menu;
    [SerializeField] private RectTransform backplate;
    [SerializeField] private UnityEngine.UI.Button buttonPrefab;
    private TMP_Text outcome, levelName, reason, timing;
    private ScoreMeterView meter;

    private void OnEnable()
    {
        if (backplate == null || buttonPrefab == null || menu == null) return;
        if (meter == null) Build();
        var result = LevelCompletionSession.Last;
        if (result == null) return;
        outcome.text = result.success ? "Level Complete" : "Failed";
        outcome.color = result.success ? new Color(.45f,1,.65f) : new Color(1,.45f,.4f);
        levelName.text = result.title;
        reason.text = result.reason;
        meter.SetScore(result.points,result.target);
        timing.gameObject.SetActive(result.basis != LevelEndBasis.Points);
        timing.text = $"Time played: {FormatTime(result.elapsed)}\nLevel length: {(result.duration>0?FormatTime(result.duration):"Unavailable")}";
    }
    private void Build()
    {
        float width = backplate.rect.width-76;
        PosePanelLayout.CreateButton(buttonPrefab,backplate,"Back",38,38,164,96,menu.ShowLevelSelect);
        outcome = Label("",180,width,90,58,true);
        levelName = Label("",286,width,84,38,true);
        reason = Label("",376,width,70,28,false);
        meter = ScoreMeterView.Create(backplate);
        meter.Root.anchorMin = meter.Root.anchorMax = new Vector2(0,1);
        meter.Root.anchoredPosition = new Vector2(backplate.rect.width*.5f,-532);
        meter.Root.localScale = Vector3.one*1.55f;
        timing = Label("",662,width,100,30,false);
        PosePanelLayout.CreateButton(buttonPrefab,backplate,"Retry",38,850,width,112,
            LevelCompletionSession.Retry,MenuButtonStyle.Role.Primary);
    }
    private TMP_Text Label(string value,float y,float width,float height,int size,bool bold)
    {
        var text=PosePanelLayout.CreateLabel(backplate,value,38,y,width,height,size,bold);
        text.enableAutoSizing=true;text.fontSizeMin=20;text.fontSizeMax=size;
        text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;return text;
    }
    private static string FormatTime(double seconds)
    {
        seconds=Math.Round(Math.Max(0,seconds),1);
        return $"{(int)(seconds/60)}:{seconds%60:00.0}";
    }
}
