using System;
using System.Linq;
using BeatTheBoss.Poses;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public sealed class DownloadedAssetsPanel : MonoBehaviour
{
    private RectTransform panel, rows;
    private PosePanelLayout layout;
    private RecordingConfirmation confirmation;
    private TMP_Text status, pager, equipLabel;
    private UnityEngine.UI.Button equip, delete;
    private bool characters=true;
    private int page;
    private string selected;
    private const int PerPage=4;
    private DownloadedAssetLibrary.Asset[] entries=Array.Empty<DownloadedAssetLibrary.Asset>();
    private TMP_Text characterTab, songTab;
    internal void Configure(RectTransform backplate,UnityEngine.UI.Button prefab,RectTransform levelPanel,UnityAction close)
    {
        panel=backplate; layout=new PosePanelLayout(prefab,prefab,null,null);
        float width=panel.rect.width-76;
        Button("Back",38,38,164,80,close);
        Button("Refresh",panel.rect.width-238,38,200,80,Refresh);
        layout.Label(panel,"Downloaded Assets",38,134,width,64,42,true);
        characterTab=Button("Character Models",38,220,(width-16)/2,72,() => SetTab(true));
        songTab=Button("Songs",38+(width+16)/2,220,(width-16)/2,72,() => SetTab(false));
        rows=layout.Rect(panel,"Asset rows",38,320,width,352);
        Button("Previous",38,686,180,62,() => { page=Mathf.Max(0,page-1); Draw(); });
        pager=layout.Label(panel,"",230,686,width-384,62,26);
        pager.alignment=TextAlignmentOptions.Midline;
        Button("Next",panel.rect.width-218,686,180,62,() => { page++; Draw(); });
        equipLabel=Button("Equip character",38,766,(width-16)/2,74,Equip);
        equip=equipLabel.GetComponentInParent<UnityEngine.UI.Button>(true);
        delete=Button("Delete",38+(width+16)/2,766,(width-16)/2,74,RequestDelete).GetComponentInParent<UnityEngine.UI.Button>(true);
        status=layout.Label(panel,"Select an asset.",38,856,width,106,24);
        status.textWrappingMode=TextWrappingModes.Normal;
        confirmation=new RecordingConfirmation(layout,panel,levelPanel);
    }
    private void Update() => confirmation?.Tick();
    private void OnDisable() => confirmation?.Cancel();
    internal void Refresh() { DownloadedAssetLibrary.Refresh(); Draw(); }
    private void SetTab(bool value) { characters=value; selected=null; page=0; Draw(); }
    private void Draw()
    {
        entries=DownloadedAssetLibrary.All.Where(a=>a.character==characters).ToArray();
        int pages=Mathf.Max(1,(entries.Length+PerPage-1)/PerPage); page=Mathf.Clamp(page,0,pages-1);
        foreach (Transform child in rows) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        float width=rows.rect.width;
        for (int i=page*PerPage;i<Mathf.Min(entries.Length,(page+1)*PerPage);i++)
        {
            var entry=entries[i];
            var label=layout.Button(rows,entry.title,0,(i%PerPage)*88,width,76,() => { selected=entry.id; Draw(); });
            label.fontSize=30;
            if (selected==entry.id)
            {
                var stripe=layout.Rect(label.GetComponentInParent<UnityEngine.UI.Button>(true).transform,"Selected",5,12,6,52);
                layout.Fill(stripe,PosePanelLayout.Accent);
            }
        }
        characterTab.text=(characters?"● ":"")+"Character Models";
        songTab.text=(!characters?"● ":"")+"Songs";
        var chosen=entries.FirstOrDefault(a=>a.id==selected);
        equip.interactable=characters && chosen!=null && chosen.id!=DownloadedAssetLibrary.Equipped;
        equipLabel.text=characters && chosen?.id==DownloadedAssetLibrary.Equipped?"Equipped":"Equip character";
        equip.gameObject.SetActive(characters);
        delete.interactable=chosen!=null && !chosen.protectedAsset;
        pager.text=$"{page+1} / {pages}";
        status.text=chosen==null?DownloadedAssetLibrary.Status:chosen.protectedAsset
            ? chosen.title+" is built in and cannot be deleted."
            : chosen.title+" selected.";
    }
    private void Equip()
    {
        try { DownloadedAssetLibrary.Equip(selected); Draw(); }
        catch (Exception e) when (AnimationStore.IsFileError(e)) { status.text=e.Message; }
    }
    private void RequestDelete()
    {
        var chosen=entries.FirstOrDefault(a=>a.id==selected);
        if (chosen==null || chosen.protectedAsset) return;
        string id=chosen.id;
        confirmation.Show("Delete asset?","Delete "+chosen.title+"? Levels keep its ID and will show it as missing. An equipped character will return to StylizedCharacter.","Delete",() =>
        {
            try { DownloadedAssetLibrary.Delete(id); selected=null; Draw(); }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { status.text=e.Message; }
        });
    }
    private TMP_Text Button(string title,float x,float y,float width,float height,UnityAction clicked)
    { var text=layout.Button(panel,title,x,y,width,height,clicked); text.fontSize=26; return text; }
}
