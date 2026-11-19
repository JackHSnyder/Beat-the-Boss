using System;
using System.Collections.Generic;
using System.Linq;
using BeatTheBoss.Poses;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public sealed class LevelSelectPanel : MonoBehaviour
{
    [Serializable]
    private sealed class LevelEntry
    {
        public string title;
        public string difficulty;
        public int encounters;
        public SceneDestination destination;
        public LevelLoadout loadout;
        public BossTimelineStore.Document bundledDefinition = new();
        [NonSerialized] public BossTimelineStore.Document document;
    }

    private const float Inset = 38f;
    private const float HeaderHeight = 96f;
    private const float ListTop = 374f;
    private const float RowHeight = 132f;
    private const float PanelGap = 240f;

    [SerializeField] private MMPanelSwitch menu;
    [SerializeField] private UnityEngine.UI.Button buttonPrefab;
    [SerializeField] private RectTransform listBackplate;
    [SerializeField] private RectTransform detailsBackplate;
    [SerializeField] private Transform listPanel;
    [SerializeField] private Transform detailsPanel;
    [SerializeField] private LevelEntry[] levels;
    [SerializeField] private Transform loadoutPanel;
    [SerializeField] private RectTransform loadoutBackplate;
    [SerializeField] private MoveLoadoutPanel loadoutSelection;
    [SerializeField] private LevelBossPreview bossPreview;
    [Header("Bundled level duration references")]
    [SerializeField] private BossController.BundledAnimation[] bundledAnimations = Array.Empty<BossController.BundledAnimation>();
    [SerializeField] private BossSpikeRow spikeRowPrefab;
    [SerializeField] private BossLaserAttack laserPrefab;

    private Vector3 center;
    private Quaternion front;
    private float splitOffset;
    private float splitProgress;
    private CanvasGroup detailsVisibility, loadoutVisibility;
    private Vector3 loadoutScale;
    private float loadoutProgress;
    private bool loadoutOpen;
    private TMP_Text loadoutButton;
    private UnityEngine.UI.ScrollRect scroll;
    private TMP_Text downloadStatus;
    private TMP_Text detailsTitle;
    private TMP_Text levelLength;
    private TMP_Text pointTarget;
    private RectTransform completionStatus;
    private int selectionVersion;
    private LevelEntry selectedLevel;
    private readonly List<LevelEntry> loadedLevels=new();
    private readonly List<LevelLoadout> runtimeLoadouts=new();
    private RectTransform rows;
    private bool built;
    private Transform assetsPanel;
    private RectTransform assetsBackplate;
    private CanvasGroup assetsVisibility;
    private DownloadedAssetsPanel assetBrowser;
    private bool assetsOpen;
    private float assetsProgress;
    private void Awake()
    {
        center = listPanel.localPosition;
        front = listPanel.localRotation;
        // The panels share a parent and use the same world-space canvas scale.
        splitOffset = (listBackplate.rect.width + PanelGap) * listBackplate.lossyScale.x /
            (2f * listPanel.parent.lossyScale.x);
        detailsVisibility = detailsBackplate.gameObject.AddComponent<CanvasGroup>();
        loadoutVisibility = loadoutBackplate.gameObject.AddComponent<CanvasGroup>();
        loadoutScale = loadoutPanel.localScale;
        loadoutPanel.gameObject.SetActive(false);
        ConfigurePoke(listPanel);
        BuildList();
        BuildAssets();
        BuildDetails();
        built=true;
        menu.SectionChanging += CloseDetails;
        RefreshLevels();
        detailsPanel.gameObject.SetActive(false);
    }
    private void OnEnable() { if (built) RefreshLevels(); }
    private void OnDestroy()
    {
        if (menu != null) menu.SectionChanging -= CloseDetails;
        foreach (var definition in runtimeLoadouts)
            if (definition!=null && definition!=MoveLoadout.ActiveLevel && definition!=EncounterLevelSession.Template) Destroy(definition);
    }

    private void OnDisable()
    {
        selectionVersion++;
        if (bossPreview != null) bossPreview.Hide();
        selectedLevel = null;
        assetsOpen=false; assetsProgress=0;
        if (assetsPanel!=null) assetsPanel.gameObject.SetActive(false);
        splitProgress = loadoutProgress = 0f; loadoutOpen = false;
        if (loadoutPanel != null) { loadoutPanel.localScale = loadoutScale; loadoutPanel.gameObject.SetActive(false); }
        listPanel.SetLocalPositionAndRotation(center, front);
        detailsPanel.gameObject.SetActive(false);
        if (scroll == null)
            return;
        downloadStatus.text = "";
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;
    }

    private void Update()
    {
        if (selectedLevel == null && !assetsOpen && assetsProgress==0 && splitProgress==0 && loadoutProgress==0) return;
        bool reduced = SceneHandler.ReducedTransitions;
        float target=selectedLevel!=null?1:0;
        splitProgress = reduced ? target : Mathf.MoveTowards(splitProgress,target,Time.unscaledDeltaTime/.36f);
        assetsProgress=reduced?(assetsOpen?1:0):Mathf.MoveTowards(assetsProgress,assetsOpen?1:0,Time.unscaledDeltaTime/.36f);
        loadoutProgress = reduced ? (loadoutOpen ? 1f : 0f) :
            Mathf.MoveTowards(loadoutProgress, loadoutOpen ? 1f : 0f, Time.unscaledDeltaTime / .36f);
        PositionPanels(splitProgress);
        if (!loadoutOpen && loadoutProgress == 0f) loadoutPanel.gameObject.SetActive(false);
        if (!assetsOpen && assetsProgress==0) assetsPanel.gameObject.SetActive(false);
    }

    private void PositionPanels(float progress)
    {
        float split = Mathf.SmoothStep(0,1,progress);
        float expand = Mathf.SmoothStep(0,1,loadoutProgress);
        Vector3 offset = front*Vector3.right*splitOffset;
        Vector3 detailsStart = center + offset*split + Vector3.forward*(.04f*(1-split));
        listPanel.SetLocalPositionAndRotation(Vector3.Lerp(center-offset*split,center-offset*2,expand),
            Quaternion.AngleAxis(Mathf.Lerp(-17*split,-34,expand),Vector3.up)*front);
        detailsPanel.SetLocalPositionAndRotation(Vector3.Lerp(detailsStart,center,expand),
            Quaternion.AngleAxis(Mathf.Lerp(17*split,0,expand),Vector3.up)*front);
        loadoutPanel.SetLocalPositionAndRotation(Vector3.Lerp(detailsStart+Vector3.forward*.04f,center+offset*2,expand),
            Quaternion.AngleAxis(Mathf.Lerp(17*split,34,expand),Vector3.up)*front);
        loadoutPanel.localScale = loadoutScale*Mathf.Lerp(.94f,1,expand);
        // Keep the wings facing inward. Their inner bottom corners meet the middle panel's
        // extended bottom edge, with a gap, rather than flattening all three panels.
        AlignInnerBottomCorner(listPanel,listBackplate,true,expand);
        AlignInnerBottomCorner(loadoutPanel,loadoutBackplate,false,expand);
        detailsVisibility.alpha = Mathf.InverseLerp(.55f,1,split);
        detailsVisibility.interactable = detailsVisibility.blocksRaycasts = progress >= 1f;
        loadoutVisibility.alpha = Mathf.InverseLerp(.3f,.85f,expand);
        loadoutVisibility.interactable = loadoutVisibility.blocksRaycasts = loadoutOpen && loadoutProgress >= 1f;
        loadoutButton.text = loadoutOpen ? "Close Loadout" : "Choose Loadout";
        if (assetsProgress>0)
        {
            float reveal=Mathf.SmoothStep(0,1,assetsProgress);
            listPanel.localPosition=Vector3.Lerp(listPanel.localPosition,center+offset,reveal);
            listPanel.localRotation=Quaternion.Slerp(listPanel.localRotation,Quaternion.AngleAxis(17,Vector3.up)*front,reveal);
            assetsPanel.SetLocalPositionAndRotation(center-offset*reveal,Quaternion.AngleAxis(-17*reveal,Vector3.up)*front);
            assetsVisibility.alpha=Mathf.InverseLerp(.4f,1,reveal);
            assetsVisibility.interactable=assetsVisibility.blocksRaycasts=assetsOpen && assetsProgress>=1;
        }
    }
    private void BuildAssets()
    {
        // Clone the existing blank Meta panel before its details controls are populated.
        assetsPanel=Instantiate(detailsPanel.gameObject,detailsPanel.parent,false).transform;
        assetsPanel.name="Downloaded Assets Panel";
        assetsBackplate=assetsPanel.GetComponentsInChildren<RectTransform>(true).First(r=>r.name==detailsBackplate.name);
        assetsVisibility=assetsBackplate.GetComponent<CanvasGroup>();
        assetsVisibility.alpha=0;
        assetBrowser=assetsPanel.gameObject.AddComponent<DownloadedAssetsPanel>();
        assetBrowser.Configure(assetsBackplate,buttonPrefab,listBackplate,HideAssets);
        ConfigurePoke(assetsPanel); assetsPanel.gameObject.SetActive(false);
    }
    public void ShowAssets()
    {
        selectionVersion++;
        if (bossPreview != null) bossPreview.Hide();
        selectedLevel=null; loadoutOpen=false;
        detailsPanel.gameObject.SetActive(false); loadoutPanel.gameObject.SetActive(false);
        loadoutProgress=0; assetsOpen=true;
        assetsPanel.gameObject.SetActive(true); assetBrowser.Refresh();
    }
    public void HideAssets() { assetsOpen=false; }
    public void CloseDetails()
    {
        selectionVersion++;
        selectedLevel = null;
        loadoutOpen = false;
        if (detailsPanel != null) detailsPanel.gameObject.SetActive(false);
        if (bossPreview != null) bossPreview.Hide();
    }
    private void AlignInnerBottomCorner(Transform panel, RectTransform backplate, bool left, float weight)
    {
        Transform space=listPanel.parent;
        Vector3 reference=space.InverseTransformPoint(detailsBackplate.TransformPoint(new Vector3(
            left?detailsBackplate.rect.xMin:detailsBackplate.rect.xMax,detailsBackplate.rect.yMin,0)));
        Vector3 corner=space.InverseTransformPoint(backplate.TransformPoint(new Vector3(
            left?backplate.rect.xMax:backplate.rect.xMin,backplate.rect.yMin,0)));
        Vector3 right=front*Vector3.right;
        float gap=PanelGap*listBackplate.lossyScale.x/space.lossyScale.x;
        panel.localPosition+=(reference+right*(left?-gap:gap)-corner)*weight;
    }
    private static void ConfigurePoke(Transform panel)
    {
        var poke=panel.GetComponent<Oculus.Interaction.PokeInteractable>();
        if (poke==null) return;
        poke.EnterHoverNormal=.06f;
        poke.ExitHoverNormal=.10f;
        poke.EnterHoverTangent=.01f;
        poke.ExitHoverTangent=.02f;
        poke.DragThresholds.DragTangent=.02f;
        poke.PositionPinning.MaxPinDistance=.005f;
    }
    public void ShowLoadout()
    {
        if (selectedLevel == null) return;
        if (loadoutOpen) { HideLoadout(); return; }
        loadoutSelection.Configure(selectedLevel.loadout);
        loadoutOpen = true; loadoutPanel.gameObject.SetActive(true);
        if (SceneHandler.ReducedTransitions) loadoutProgress = 1;
        PositionPanels(splitProgress);
    }
    public void HideLoadout()
    {
        loadoutOpen = false;
        if (SceneHandler.ReducedTransitions)
        {
            loadoutProgress = 0; PositionPanels(splitProgress);
            loadoutPanel.gameObject.SetActive(false);
        }
    }

    private void BuildList()
    {
        float width = listBackplate.rect.width - 2f * Inset;
        AddButton(listBackplate, "Back", Inset, Inset, 164f, HeaderHeight, 26, menu.ShowMainMenu);
        const float downloadWidth = 258f;
        AddButton(listBackplate, "Download Levels", Inset + width - downloadWidth, Inset,
            downloadWidth, HeaderHeight, 26, () => downloadStatus.text = "Level downloads are coming soon.");
        AddButton(listBackplate,"New Level",Inset,150,(width-16)/2,80,26,CreateLevel,MenuButtonStyle.Role.Accent);
        AddButton(listBackplate,"Refresh Levels",Inset+(width+16)/2,150,(width-16)/2,80,26,RefreshLevels);
        AddButton(listBackplate,"Show downloaded assets",Inset,242,width,80,26,ShowAssets);
        downloadStatus = PosePanelLayout.CreateLabel(listBackplate, "", Inset, 328,
            width, 38, 23);
        downloadStatus.color = PosePanelLayout.Muted;
        downloadStatus.alignment = TextAlignmentOptions.MidlineRight;

        RectTransform viewport = CreateRect(listBackplate, "Level list");
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(Inset, Inset);
        viewport.offsetMax = new Vector2(-Inset, -ListTop);
        var listBackground = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
        listBackground.color = new Color(0f, 0f, 0f, 0.18f);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

        rows = CreateRect(viewport, "Level rows");
        rows.anchorMin = Vector2.up;
        rows.anchorMax = Vector2.one;
        rows.pivot = new Vector2(0.5f, 1f);
        rows.anchoredPosition = Vector2.zero;
        rows.sizeDelta = new Vector2(0f, levels.Length * RowHeight + 24f);

        scroll = viewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = rows;
        scroll.horizontal = false;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = RowHeight;
        // A short list has nothing to scroll; do not turn small poking motions into scroll gestures.
        scroll.enabled = rows.sizeDelta.y > listBackplate.rect.height-ListTop-Inset;

    }
    private LevelLoadout DefaultTemplate => levels.FirstOrDefault(l=>l?.loadout!=null)?.loadout;
    private void CreateLevel()
    {
        EncounterLevelSession.New(DefaultTemplate);
        SceneHandler.TransitionTo(SceneDestination.BossEditor);
    }
    public void RefreshLevels()
    {
        if (scroll==null) return;
        foreach(var bundled in levels) BossTimelineStore.RegisterBundledLevel(bundled.title,LevelId(bundled));
        string selectedId=selectedLevel?.document?.levelId ?? selectedLevel?.loadout?.Id;
        loadedLevels.Clear(); int failed=0;
        try
        {
            foreach (string name in BossTimelineStore.List())
            {
                try
                {
                    var doc=BossTimelineStore.Load(name);
                    loadedLevels.Add(new LevelEntry {title=doc.name,difficulty="Custom",encounters=1,
                        destination=SceneDestination.Boss,document=doc});
                }
                catch (Exception error) when (AnimationStore.IsFileError(error)) { failed++; }
            }
        }
        catch (Exception error) when (AnimationStore.IsFileError(error))
        { downloadStatus.text="Couldn't refresh levels: "+error.Message; return; }
        // A saved example replaces the bundled row of the same name.
        foreach (var level in levels)
            if (!loadedLevels.Any(l=>string.Equals(l.title,level.title,StringComparison.OrdinalIgnoreCase) ||
                (level.loadout!=null && l.document?.levelId==level.loadout.Id))) loadedLevels.Add(level);
        loadedLevels.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.title,b.title));
        foreach (Transform child in rows) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        rows.sizeDelta=new Vector2(0,loadedLevels.Count*RowHeight+24);
        scroll.StopMovement(); scroll.verticalNormalizedPosition=1;
        scroll.enabled=rows.sizeDelta.y>listBackplate.rect.height-ListTop-Inset;
        float width=listBackplate.rect.width-2*Inset;
        for (int i=0;i<loadedLevels.Count;i++)
        {
            LevelEntry level=loadedLevels[i];
            TMP_Text row = AddButton(rows, level.title, 12f, 12f + i * RowHeight,
                width - 24f, RowHeight - 20f, 34, () => SelectLevel(level));
            row.GetComponentInParent<UnityEngine.UI.Button>().gameObject.AddComponent<LevelListItemInput>().Initialize(scroll);
            row.alignment = TextAlignmentOptions.MidlineLeft;
            bool completed=LevelProgress.HasPassed(LevelId(level));
            row.margin = new Vector4(24f, 0f, completed?96f:24f, 0f);
            if (completed)
            {
                var check=LevelCompletionCheck.Create(row.transform.parent);
                check.anchorMin=check.anchorMax=new Vector2(1,.5f);
                check.anchoredPosition=new Vector2(-42,0);
            }
        }
        var selected=loadedLevels.FirstOrDefault(l=>(l.document?.levelId??l.loadout?.Id)==selectedId);
        if (selectedLevel!=null)
        {
            if (selected!=null) SelectLevel(selected);
            else OnDisable();
        }
        downloadStatus.text=$"{loadedLevels.Count} levels loaded"+(failed>0?$"; {failed} unreadable files skipped.":".");
    }

    private void BuildDetails()
    {
        float width = detailsBackplate.rect.width - 2f * Inset;
        detailsTitle = PosePanelLayout.CreateLabel(detailsBackplate, "", Inset, Inset, width, 150f, 50, true);
        detailsTitle.enableAutoSizing=true;
        detailsTitle.fontSizeMin=26; detailsTitle.fontSizeMax=50;
        detailsTitle.textWrappingMode=TextWrappingModes.Normal;
        levelLength = AddStat("Level length", 235f, width);
        pointTarget = AddStat("Point target", 370f, width);
        completionStatus=CreateRect(detailsBackplate,"Completed status");
        completionStatus.anchorMin=completionStatus.anchorMax=completionStatus.pivot=new Vector2(0,1);
        completionStatus.anchoredPosition=new Vector2(Inset,-505);completionStatus.sizeDelta=new Vector2(width,56);
        var check=LevelCompletionCheck.Create(completionStatus);
        check.anchorMin=check.anchorMax=new Vector2(0,.5f);check.anchoredPosition=new Vector2(24,0);
        var completed=PosePanelLayout.CreateLabel(completionStatus,"Completed",64,0,width-64,56,30,true);
        completed.color=new Color(.4f,1,.6f);
        completionStatus.gameObject.SetActive(false);

        float playTop = detailsBackplate.rect.height - Inset - 112f;
        loadoutButton = AddButton(detailsBackplate, "Choose Loadout", Inset, playTop - 208f,
            width, 88f, 30, ShowLoadout);
        AddButton(detailsBackplate, "Boss Editor", Inset, playTop - 104f,
            width, 88f, 30, () => { SelectEncounter(); SceneHandler.TransitionTo(SceneDestination.BossEditor); },MenuButtonStyle.Role.Accent);
        AddButton(detailsBackplate, "Play Level", Inset, playTop,
            width, 112f, 36, () =>
            {
                SelectEncounter();
                MoveLoadout.SelectLevel(selectedLevel.loadout);
                SceneHandler.TransitionTo(selectedLevel.destination);
            },MenuButtonStyle.Role.Primary);
    }

    private TMP_Text AddStat(string title, float top, float width)
    {
        TMP_Text caption = PosePanelLayout.CreateLabel(detailsBackplate, title, Inset, top, width, 38f, 29);
        caption.color = PosePanelLayout.Muted;
        return PosePanelLayout.CreateLabel(detailsBackplate, "", Inset, top + 39f, width, 53f, 42);
    }

    private void SelectLevel(LevelEntry level)
    {
        int version=++selectionVersion;
        assetsOpen=false;
        if (level.document!=null && level.loadout==null)
        {
            level.loadout=LevelLoadout.FromTimeline(level.document,DefaultTemplate);
            runtimeLoadouts.Add(level.loadout);
        }
        selectedLevel = level;
        detailsTitle.text = level.title;
        var definition=Definition(level);
        pointTarget.text=Mathf.Max(1,definition.targetPoints).ToString("N0");
        completionStatus.gameObject.SetActive(LevelProgress.HasPassed(LevelId(level)));
        if (loadoutOpen) loadoutSelection.Configure(level.loadout);
        PositionPanels(splitProgress);
        detailsPanel.gameObject.SetActive(true);
        if (definition.endBasis==LevelEndBasis.Points) levelLength.text="N/A";
        else if (definition.endBasis==LevelEndBasis.Timeline) levelLength.text=FormatLength(TimelineLength(definition));
        else
        {
            levelLength.text="Loading...";
            StartCoroutine(DownloadedAssetLibrary.LoadSong(definition.songId,(clip,error)=>
            {
                if (version!=selectionVersion || selectedLevel!=level || !detailsPanel.gameObject.activeSelf) return;
                levelLength.text=clip!=null ? FormatLength(definition.songDelay+clip.length) : "Unavailable";
            }));
        }
        if (bossPreview != null)
            bossPreview.Show(level.document?.levelId ?? level.title, level.document?.bossModelId, level.document?.idle);
    }
    private static BossTimelineStore.Document Definition(LevelEntry level) => level.document ?? level.bundledDefinition;
    private static string LevelId(LevelEntry level) => level.document?.levelId ?? level.loadout?.Id ?? "bundled-"+level.title.ToUpperInvariant();
    private float TimelineLength(BossTimelineStore.Document definition)
    {
        float duration=Mathf.Max(.1f,definition.duration);
        foreach (var entry in definition.events)
        {
            if (entry.action==BossController.ActionKind.DummyAngle) continue;
            MoveAnimation clip=null;
            if (entry.action==BossController.ActionKind.PlayAnimation)
            {
                var bundled=bundledAnimations.FirstOrDefault(a=>a.name==entry.animation);
                clip=new BossController.Recording {savedName=entry.animation,bundledJson=bundled?.json}.Load(this);
            }
            duration=Mathf.Max(duration,entry.time+BossController.EventDuration(entry.action,clip,entry.speed,false,spikeRowPrefab,laserPrefab));
        }
        return duration;
    }
    private static string FormatLength(double seconds)
    {
        seconds=Math.Round(Math.Max(0,seconds),1);
        return $"{(int)(seconds/60)}:{seconds%60:00.0}";
    }
    private void SelectEncounter()
    {
        if (selectedLevel.document!=null) EncounterLevelSession.Saved(selectedLevel.title,selectedLevel.loadout);
        else EncounterLevelSession.Bundled(selectedLevel.title,selectedLevel.loadout);
    }

    private TMP_Text AddButton(Transform parent, string title, float x, float y,
        float width, float height, int fontSize, UnityAction clicked, MenuButtonStyle.Role role=MenuButtonStyle.Role.Secondary)
    {
        TMP_Text label = PosePanelLayout.CreateButton(buttonPrefab, parent, title, x, y, width, height, clicked,role);
        label.enableAutoSizing = false;
        label.fontSize = fontSize;

        return label;
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        return rect;
    }

}
