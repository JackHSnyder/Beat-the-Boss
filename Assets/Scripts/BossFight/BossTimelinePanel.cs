using System;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(350)]
    public sealed class BossTimelinePanel : MonoBehaviour
    {
        public BossTimelineWorkbench workbench;
        public Camera headsetCamera;
        public GameObject backplatePrefab, nameFieldPrefab;
        public UnityEngine.UI.Button primaryButtonPrefab, secondaryButtonPrefab;
        public UnityEngine.UI.Slider sliderPrefab;
        internal BossTimelineWorkbench Workbench => workbench;
        internal float ViewStart => viewStart;
        internal float PixelsPerSecond => GraphWidth / windowSeconds;
        internal const int LaneTop = 32, LaneHeight = 45;
        private const int GraphWidth = 928, GraphHeight = 212;
        private static readonly string[] Tracks = { "Animation", "Rotation", "Attacks", "Dummy" };
        private static readonly Color[] Colors = { new(.22f,.6f,.85f), new(.9f,.6f,.25f), new(.92f,.38f,.42f), new(.2f,.72f,.6f) };
        private readonly float[] snaps = { .1f, .25f, .5f, 1, 0 };
        private sealed class Surface
        {
            public GameObject root;
            public Canvas canvas;
            public RectTransform panel;
        }
        private Surface main, details, navigation;
        private PosePanelLayout layout;
        private BossEditorEnvironment environment;
        private RectTransform track, drawings, playhead, properties, viewControls;
        private TMP_Text timing, playLabel, snapLabel, fileLabel, detailTitle, status;
        private UnityEngine.UI.Slider scrub;
        private InputSystemUIInputModule mouse;
        private PointableCanvasModule xrInput;
        private string[] animations = Array.Empty<string>(), files = Array.Empty<string>();
        private string view = "node", message;
        private int selectedTrack, snapIndex, page;
        private float viewStart, windowSeconds = 12, dragTime;
        private bool inHeadset, built, redraw, detailsDirty, pickingInitialIdle;
        private BossController.Cue dragCue;
        private bool placementPending;
        private float trackingReadySince = -1;
        private double recenterAt = -1;
        private readonly System.Collections.Generic.List<(TMP_Text label,string title)> recenterLabels = new();
        private RecordingConfirmation confirmation;
        private string nameBuffer;
        private bool capitalLetters=true;
        private TMP_Text nameDisplay;
        private TMP_InputField nameInput;

        private Surface CreateSurface(string name, Vector2 size)
        {
            var surface = new Surface { root = Instantiate(backplatePrefab, transform, false) };
            surface.root.name = name;
            surface.canvas = surface.root.GetComponentInChildren<Canvas>(true);
            surface.canvas.sortingOrder = 10;
            var scaler = surface.canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600,900);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            surface.canvas.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().enabled = false;
            surface.panel = (RectTransform)surface.canvas.transform.Find("UIBackplate");
            surface.panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().enabled = false;
            surface.panel.sizeDelta = size;
            var go = new GameObject(name + " gaze"); go.SetActive(false); go.transform.SetParent(surface.root.transform, false);
            var gaze = go.AddComponent<GazeInteractable>();
            gaze.InjectAllGazeInteractable(surface.root.GetComponentInChildren<ClippedPlaneSurface>(true));
            gaze.InjectOptionalPointableElement(surface.root.GetComponent<PointableCanvas>());
            gaze.InjectOptionalMultiGrabScalingSupport(false); go.SetActive(true);
            return surface;
        }
        private void Start()
        {
            layout = new PosePanelLayout(primaryButtonPrefab, secondaryButtonPrefab, nameFieldPrefab, sliderPrefab);
            environment = GetComponent<BossEditorEnvironment>();
            main = CreateSurface("Boss timeline controls", new Vector2(1100,396));
            details = CreateSurface("Timeline node details", new Vector2(420,840));
            navigation = CreateSurface("Editor view and navigation", new Vector2(380,568));
            Label(navigation.panel,"VIEW & NAVIGATION",24,18,332,40,23,true);
            Label(navigation.panel,"Viewing position",24,78,332,30,18);
            Button(navigation.panel,"Boss view",24,116,160,48,environment.FrameBoss);
            Button(navigation.panel,"Arena view",196,116,160,48,environment.FrameArena);
            Button(navigation.panel,"Closer",24,180,160,48,() => environment.Zoom(-5));
            Button(navigation.panel,"Further",196,180,160,48,() => environment.Zoom(5));
            Button(navigation.panel,"Orbit <",24,244,160,48,() => environment.Orbit(-30));
            Button(navigation.panel,"Orbit >",196,244,160,48,() => environment.Orbit(30));
            RecenterButton(navigation.panel,"Reset panels (B)",24,320,332,48);
            Button(navigation.panel,"Recordings",24,412,332,48,() => Leave(SceneDestination.Recording));
            Button(navigation.panel,"Main menu",24,484,332,48,() => Leave(SceneDestination.MainMenu));
            var panel = main.panel;
            Label(panel, "ENCOUNTER TIMELINE", 24,12,320,40,23,true);
            timing = Label(panel,"",350,16,195,32,18);
            playLabel = Button(panel,"Play",554,16,78,36,workbench.TogglePlay,true);
            Button(panel,"Stop",640,16,78,36,workbench.Stop);
            Button(panel,"Add node",734,16,150,36,() => { view = "node"; workbench.Add(Modes(selectedTrack)[0]); },true);
            Button(panel,"Undo",900,16,78,36,() => workbench.Undo());
            Button(panel,"Redo",986,16,90,36,() => workbench.Undo(true));
            Button(panel,"Files",24,68,78,34,() => OpenDetails("files"));
            fileLabel = Button(panel,"Name timeline",114,68,212,34,EditName);
            Button(panel,"Save",334,68,76,34,RequestSave,true);
            Button(panel,"Encounter",420,68,120,34,() => OpenDetails("encounter"));
            snapLabel = Button(panel,"Snap .1s",550,68,145,34,() => { snapIndex = (snapIndex+1)%snaps.Length; workbench.snap = snaps[snapIndex]; });
            Button(panel,"Zoom +",714,68,86,34,() => Zoom(.5f));
            Button(panel,"Zoom -",810,68,86,34,() => Zoom(2));
            Button(panel,"Earlier",910,68,78,34,() => Pan(-1));
            Button(panel,"Later",998,68,78,34,() => Pan(1));
            for (int i=0;i<Tracks.Length;i++)
            {
                int lane = i;
                Button(panel,Tracks[i],18,120+LaneTop+i*LaneHeight+3,120,LaneHeight-6,() => SelectAt(workbench.Cursor,lane,false));
            }
            track = layout.Rect(panel,"Timeline hit surface",148,120,GraphWidth,GraphHeight);
            layout.Fill(track,new Color(.065f,.08f,.11f,.95f)).raycastTarget = true;
            track.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            track.gameObject.AddComponent<BossTimelineSurface>().owner = this;
            drawings = layout.Rect(track,"Timeline drawings",0,0,GraphWidth,GraphHeight);
            playhead = layout.Rect(track,"Playhead",0,0,2,GraphHeight); layout.Fill(playhead,Color.white);
            scrub = layout.Slider(panel,"Seek",148,340,GraphWidth,0,12,t => workbench.Seek(workbench.Snapped(t)),false);
            detailTitle = Label(details.panel,"Node details",24,18,372,44,26,true);
            properties = layout.Rect(details.panel,"Properties",24,78,372,650);
            status = Label(details.panel,"",24,748,372,64,16);
            viewControls = layout.Rect(main.canvas.transform,"Arena camera controls",0,0,1090,40);
            viewControls.anchoredPosition = new Vector2(20,-20);
            Button(viewControls,"Boss view",0,0,105,38,environment.FrameBoss);
            Button(viewControls,"Arena view",115,0,105,38,environment.FrameArena);
            Button(viewControls,"Zoom +",230,0,85,38,() => environment.Zoom(-5));
            Button(viewControls,"Zoom -",325,0,85,38,() => environment.Zoom(5));
            Button(viewControls,"Orbit <",420,0,85,38,() => environment.Orbit(-30));
            Button(viewControls,"Orbit >",515,0,85,38,() => environment.Orbit(30));
            RecenterButton(viewControls,"Reset panels",610,0,118,38);
            Button(viewControls,"Recordings",738,0,150,38,() => Leave(SceneDestination.Recording));
            Button(viewControls,"Main menu",900,0,176,38,() => Leave(SceneDestination.MainMenu));
            var events = FindAnyObjectByType<EventSystem>();
            if (events == null) events = new GameObject("Timeline input",typeof(EventSystem)).GetComponent<EventSystem>();
            mouse = events.GetComponent<InputSystemUIInputModule>() ?? events.gameObject.AddComponent<InputSystemUIInputModule>();
            xrInput = events.GetComponent<PointableCanvasModule>() ?? events.gameObject.AddComponent<PointableCanvasModule>();
            xrInput.ExclusiveMode = false;
            confirmation=new RecordingConfirmation(layout,main.panel,details.panel,navigation.panel,viewControls);
            built = true; Present(environment.InHeadset); ReadAnimations();
            workbench.Changed += OnChanged; OnChanged();
            environment.ViewChanged += MovePanelsWithObserver;
            if (workbench.IsNew) EditName();
        }
        private void OnDestroy()
        {
            if (workbench != null) workbench.Changed -= OnChanged;
            if (environment != null) environment.ViewChanged -= MovePanelsWithObserver;
        }
        private void OnDisable() { recenterAt = -1; confirmation?.Cancel(); }
        private void MovePanelsWithObserver(Pose previous,Pose current)
        {
            if (!inHeadset || placementPending) return;
            // Carry the whole layout with the tracking origin, independently of when the SDK updates eye anchors.
            Quaternion rotation=current.rotation*Quaternion.Inverse(previous.rotation);
            foreach (Surface surface in new[] {main,details,navigation})
            {
                Transform canvas=surface.canvas.transform;
                canvas.SetPositionAndRotation(current.position+rotation*(canvas.position-previous.position),rotation*canvas.rotation);
            }
        }
        private void RecenterButton(Transform parent,string title,float x,float y,float width,float height)
            => recenterLabels.Add((Button(parent,title,x,y,width,height,RequestRecenter),title));
        private void RequestRecenter()
        {
            if (inHeadset && recenterAt < 0) recenterAt=Time.unscaledTimeAsDouble+3;
        }
        private void TickRecenter()
        {
            if (recenterAt>=0 && Time.unscaledTimeAsDouble>=recenterAt)
            { recenterAt=-1; PlacePanels(); }
            recenterLabels.RemoveAll(item=>item.label==null);
            foreach (var item in recenterLabels)
                item.label.text=recenterAt<0?item.title:$"Reset in {Mathf.CeilToInt((float)(recenterAt-Time.unscaledTimeAsDouble))}…";
        }
        private void Present(bool headset)
        {
            inHeadset = headset; viewControls.gameObject.SetActive(!headset);
            recenterAt = -1;
            mouse.enabled = !headset; xrInput.enabled = headset;
            Present(main,headset,Vector2.zero,new Vector2(20,20));
            Present(details,headset,Vector2.one,new Vector2(-20,-20));
            Present(navigation,headset,Vector2.zero,Vector2.zero);
            navigation.root.SetActive(headset);
            placementPending = headset;
            trackingReadySince = -1;
            // Do not show panels positioned against an uninitialized eye anchor during scene loading.
            SetPanelsVisible(!headset);
        }
        private void Present(Surface surface, bool headset, Vector2 anchor, Vector2 position)
        {
            surface.root.GetComponent<RayInteractable>().enabled = headset;
            surface.root.GetComponent<PokeInteractable>().enabled = headset;
            surface.root.GetComponentInChildren<GazeInteractable>(true).enabled = headset;
            surface.canvas.renderMode = headset ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            surface.canvas.worldCamera = headset ? headsetCamera : null;
            var rect = (RectTransform)surface.canvas.transform; rect.localScale = Vector3.one*(headset ? .001f : 1);
            if (headset)
            {
                rect.sizeDelta = surface.panel.sizeDelta;
                surface.panel.anchorMin = surface.panel.anchorMax = surface.panel.pivot = new Vector2(.5f,.5f);
                surface.panel.anchoredPosition = Vector2.zero;
            }
            else
            {
                surface.panel.anchorMin = surface.panel.anchorMax = surface.panel.pivot = anchor;
                surface.panel.anchoredPosition = position;
            }
        }
        private void PlacePanels()
        {
            if (!inHeadset || headsetCamera == null) return;
            var eye = headsetCamera.transform;
            var yaw = PlayerPoseTracker.Heading(eye.rotation);
            Place(main,eye,yaw*new Vector3(-.34f,-.20f,1.7f));
            Place(details,eye,yaw*new Vector3(.48f,.02f,1.65f));
            Place(navigation,eye,yaw*new Vector3(-1.23f,.02f,1.55f));
            placementPending = false;
            SetPanelsVisible(true);
        }
        private void SetPanelsVisible(bool visible)
        {
            main.canvas.enabled = details.canvas.enabled = navigation.canvas.enabled = visible;
            foreach (Surface surface in new[] {main,details,navigation})
            {
                surface.root.GetComponent<RayInteractable>().enabled = visible && inHeadset;
                surface.root.GetComponent<PokeInteractable>().enabled = visible && inHeadset;
                surface.root.GetComponentInChildren<GazeInteractable>(true).enabled = visible && inHeadset;
            }
        }
        private void LateUpdate()
        {
            if (!built || !placementPending || headsetCamera == null) return;
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            bool tracked = head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool valid) && valid;
            tracked |= OVRManager.isHmdPresent && OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
            UpdateInitialPlacement(tracked);
        }
        private void UpdateInitialPlacement(bool tracked)
        {
            if (!tracked || SceneHandler.IsTransitioning) { trackingReadySince = -1; return; }
            if (trackingReadySince < 0) trackingReadySince = Time.unscaledTime;
            // LateUpdate observes the updated rig; allow a few frames for the tracking origin to settle.
            if (Time.unscaledTime-trackingReadySince >= .25f) PlacePanels();
        }
        private static void Place(Surface surface, Transform eye, Vector3 offset)
        {
            Vector3 position = eye.position+offset;
            surface.canvas.transform.SetPositionAndRotation(position,Quaternion.LookRotation(position-eye.position,Vector3.up));
        }
        private void Update()
        {
            if (!built) return;
            if (inHeadset != environment.InHeadset) Present(environment.InHeadset);
            if (inHeadset && OVRInput.GetDown(OVRInput.Button.Two,OVRInput.Controller.RTouch)) RequestRecenter();
            TickRecenter();
            confirmation.Tick();
            if (!inHeadset && Keyboard.current != null && !confirmation.IsOpen && view!="name")
            {
                if (Keyboard.current.spaceKey.wasPressedThisFrame) workbench.TogglePlay();
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame) workbench.Seek(workbench.Cursor-1f/30);
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame) workbench.Seek(workbench.Cursor+1f/30);
            }
            if (redraw) Draw();
            if (detailsDirty) DrawDetails();
            playLabel.text = workbench.Playing ? "Pause" : "Play";
            timing.text = $"{workbench.Cursor:0.00} / {workbench.boss.TimelineDuration:0.00}s";
            snapLabel.text = workbench.snap == 0 ? "Snap off" : $"Snap {workbench.snap:0.##}";
            scrub.maxValue = workbench.boss.TimelineDuration; scrub.SetValueWithoutNotify(workbench.Cursor);
            float x = (workbench.Cursor-viewStart)*PixelsPerSecond;
            playhead.gameObject.SetActive(x>=0 && x<=GraphWidth); playhead.anchoredPosition = new Vector2(x,0);
            fileLabel.text = workbench.saveName+(workbench.Dirty ? " *" : "");
            status.text = message ?? ((workbench.Dirty ? "Unsaved changes - " : "")+workbench.Status);
        }
        internal static int Lane(BossController.ActionKind action) => action switch
        {
            BossController.ActionKind.FaceAngle or BossController.ActionKind.FollowPlayer or BossController.ActionKind.StopFollowing => 1,
            BossController.ActionKind.FireOrb or BossController.ActionKind.SpikeRow or BossController.ActionKind.FireLaser => 2,
            BossController.ActionKind.DummyAngle => 3,
            _ => 0
        };
        private static BossController.ActionKind[] Modes(int lane) => lane switch
        {
            0 => new[] { BossController.ActionKind.PlayAnimation,BossController.ActionKind.SetIdle,BossController.ActionKind.PauseIdle,BossController.ActionKind.ResumeIdle },
            1 => new[] { BossController.ActionKind.FollowPlayer,BossController.ActionKind.StopFollowing,BossController.ActionKind.FaceAngle },
            2 => new[] { BossController.ActionKind.FireOrb,BossController.ActionKind.SpikeRow,BossController.ActionKind.FireLaser },
            _ => new[] { BossController.ActionKind.DummyAngle }
        };
        private static string ModeName(BossController.ActionKind kind) => kind switch
        {
            BossController.ActionKind.PlayAnimation => "Play animation",
            BossController.ActionKind.SetIdle => "Change idle",
            BossController.ActionKind.PauseIdle => "Stop idle",
            BossController.ActionKind.ResumeIdle => "Resume idle",
            BossController.ActionKind.FollowPlayer => "Follow player",
            BossController.ActionKind.StopFollowing => "Stop following player",
            BossController.ActionKind.FaceAngle => "Turn to angle",
            BossController.ActionKind.FireOrb => "Fire orb",
            BossController.ActionKind.SpikeRow => "Spike row",
            BossController.ActionKind.FireLaser => "Shoot laser",
            _ => "Move dummy"
        };
        private string Title(BossController.Cue cue) => cue.action switch
        {
            BossController.ActionKind.PlayAnimation => string.IsNullOrEmpty(cue.recording?.savedName) ? "Animation" : cue.recording.savedName,
            BossController.ActionKind.SetIdle => "Idle: "+(cue.recording?.savedName ?? "choose animation"),
            BossController.ActionKind.FaceAngle or BossController.ActionKind.DummyAngle => $"{ModeName(cue.action)} {cue.angle:0}°",
            _ => ModeName(cue.action)
        };
        private float PointWidth(BossController.Cue cue) => Mathf.Clamp(workbench.boss.timeline
            .Where(c => c != cue && Lane(c.action)==Lane(cue.action) && c.time>cue.time)
            .Select(c => (c.time-cue.time)*PixelsPerSecond-8).DefaultIfEmpty(170).Min(),12,170);
        internal BossController.Cue Hit(float time, int lane)
        {
            for (int i=workbench.boss.timeline.Count-1;i>=0;i--)
            {
                var cue = workbench.boss.timeline[i]; if (Lane(cue.action)!=lane) continue;
                float duration = workbench.boss.ClipDuration(cue);
                float width = duration>0 ? duration : PointWidth(cue)/PixelsPerSecond;
                if (time>=cue.time-8/PixelsPerSecond && time<=cue.time+width) return cue;
            }
            return null;
        }
        internal BossController.Cue SelectAt(float time, int lane, bool selectNode = true)
        {
            if (lane>=0 && lane<Tracks.Length) selectedTrack = lane;
            var cue = selectNode ? Hit(time,lane) : null;
            workbench.Seek(workbench.Snapped(time)); view = "node"; message = null;
            workbench.Select(cue); return cue;
        }
        internal void ShowDrag(BossController.Cue cue,float time) { dragCue = cue; dragTime = time; redraw = true; }
        internal void Redraw() { dragCue = null; redraw = true; }
        private void OnChanged()
        {
            if (workbench.Selected != null) selectedTrack = Lane(workbench.Selected.action);
            message = null; Redraw(); detailsDirty = true;
        }
        private void Draw()
        {
            redraw = false; Clear(drawings);
            for (int i=0;i<Tracks.Length;i++)
                layout.Fill(layout.Rect(drawings,"Lane",0,LaneTop+i*LaneHeight,GraphWidth,LaneHeight),new Color(1,1,1,i==selectedTrack ? .15f : .025f));
            float spacing = windowSeconds<=12 ? 1 : windowSeconds<=30 ? 5 : 10;
            for (float t=Mathf.Ceil(viewStart/spacing)*spacing;t<=viewStart+windowSeconds;t+=spacing)
            {
                float x = (t-viewStart)*PixelsPerSecond;
                Label(drawings,$"{t:0.#}s",x+3,0,60,25,14);
                layout.Fill(layout.Rect(drawings,"Grid",x,28,1,GraphHeight-28),new Color(1,1,1,.12f));
            }
            foreach (var cue in workbench.boss.timeline)
            {
                int lane = Lane(cue.action); float time = cue==dragCue ? dragTime : cue.time;
                float x = (time-viewStart)*PixelsPerSecond, duration = workbench.boss.ClipDuration(cue);
                float width = duration>0 ? duration*PixelsPerSecond : PointWidth(cue);
                if (BossController.HasTransition(cue.action))
                {
                    float seconds=workbench.boss.TransitionDuration(cue);
                    bool finish=cue.transitionTiming==BossController.TransitionTiming.FinishAtNode;
                    float start=finish?time-seconds:time, end=finish?time:time+seconds;
                    TransitionStrip(start,end,lane,Colors[lane]);
                    if (finish && start<0 && workbench.boss.loop)
                        TransitionStrip(workbench.boss.TimelineDuration+start,workbench.boss.TimelineDuration,lane,Colors[lane]);
                }
                float left = Mathf.Max(0,x), right = Mathf.Min(GraphWidth,x+width);
                if (right<=left) continue;
                Color color = cue==workbench.Selected ? Color.white : Colors[lane];
                bool missing = (cue.action is BossController.ActionKind.PlayAnimation or BossController.ActionKind.SetIdle)
                    && !string.IsNullOrEmpty(cue.recording?.savedName) && !workbench.boss.HasClip(cue);
                if (missing) color = PosePanelLayout.Warning;
                float y = LaneTop+lane*LaneHeight+5;
                layout.Fill(layout.Rect(drawings,"Node",left,y,right-left,LaneHeight-10),color*new Color(1,1,1,.23f));
                layout.Fill(layout.Rect(drawings,"Node marker",left,y,3,LaneHeight-10),color);
                var label = Label(drawings,Title(cue),left+8,y,Mathf.Max(1,right-left-12),LaneHeight-10,15);
                label.color = color; label.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
        private void TransitionStrip(float start,float end,int lane,Color color)
        {
            float left=Mathf.Max(0,(start-viewStart)*PixelsPerSecond), right=Mathf.Min(GraphWidth,(end-viewStart)*PixelsPerSecond);
            if(right<=left)return;
            color.a=.8f;
            layout.Fill(layout.Rect(drawings,"Transition interval",left,LaneTop+(lane+1)*LaneHeight-5,right-left,3),color);
        }
        private void OpenDetails(string next)
        {
            view = next; page = 0; message = null;
            try { if (next=="files") files = BossTimelineStore.List(); }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { message = e.Message; }
            detailsDirty = true;
        }
        private void ReadAnimations()
        {
            try
            {
                animations = AnimationStore.List().Concat(workbench.boss.bundledAnimations.Where(a => a!=null).Select(a => a.name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
                workbench.ReloadAnimations();
            }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { message = e.Message; }
        }
        private void PickAnimation(bool initial) { pickingInitialIdle = initial; OpenDetails("animations"); ReadAnimations(); }
        private void ChooseAnimation(string name)
        {
            if (pickingInitialIdle) workbench.SetIdle(name); else workbench.SetSelectedAnimation(name);
            OpenDetails(pickingInitialIdle ? "encounter" : "node");
        }
        private void DrawDetails()
        {
            detailsDirty = false; Clear(properties);
            if (view=="name") { DrawNameEditor(); return; }
            if (view=="files")
            {
                detailTitle.text = "Timeline files";
                Button(properties,"New timeline",0,0,178,40,() => GuardUnsaved(() => { workbench.New(); EditName(); }));
                Button(properties,"Refresh",194,0,178,40,() => OpenDetails("files"));
                Picker(files,name => GuardUnsaved(() => { workbench.Load(name); OpenDetails("node"); })); return;
            }
            if (view=="animations")
            {
                detailTitle.text = "Choose animation";
                Button(properties,"Back",0,0,178,40,() => OpenDetails(pickingInitialIdle ? "encounter" : "node"));
                Button(properties,"Refresh",194,0,178,40,() => { ReadAnimations(); detailsDirty = true; });
                Picker(animations,ChooseAnimation);
                Button(properties,"Clear animation",0,584,372,42,() => ChooseAnimation(null)); return;
            }
            if (view=="encounter")
            {
                detailTitle.text = "Encounter settings";
                Number("Minimum length",$"{workbench.boss.loopDuration:0.##} seconds",0,() => workbench.SetDuration(workbench.boss.loopDuration-1),() => workbench.SetDuration(workbench.boss.loopDuration+1));
                Button(properties,workbench.boss.loop ? "Loop: on" : "Loop: off",0,100,372,42,workbench.ToggleLoop);
                Label(properties,"Starting idle",0,164,372,30,17);
                Button(properties,string.IsNullOrEmpty(workbench.boss.idle.savedName) ? "Choose animation" : workbench.boss.idle.savedName,0,200,372,44,() => PickAnimation(true));
                Number("Dummy start angle",$"{workbench.boss.dummyStartAngle:0}°",278,() => workbench.SetDummyStart(workbench.boss.dummyStartAngle-15),() => workbench.SetDummyStart(workbench.boss.dummyStartAngle+15));
                Button(properties,"Name / Rename timeline",0,372,372,42,EditName);
                RecenterButton(properties,"Recenter panels",0,436,372,42);
                Button(properties,"Recordings",0,500,178,42,() => Leave(SceneDestination.Recording));
                Button(properties,"Main menu",194,500,178,42,() => Leave(SceneDestination.MainMenu)); return;
            }
            var cue = workbench.Selected;
            if (cue==null)
            {
                detailTitle.text = Tracks[selectedTrack]+" track";
                Label(properties,"Click a track to place the cursor, then add a node. Select a node to edit its properties here.",0,0,372,120,20);
                Label(properties,"Drag nodes to change their time.\nSpace: play or pause\nArrow keys: step preview",0,156,372,140,18); return;
            }
            detailTitle.text = Tracks[Lane(cue.action)]+" node";
            if (view=="modes")
            {
                Button(properties,"Back",0,0,372,40,() => OpenDetails("node"));
                int row = 0;
                foreach (var mode in Modes(Lane(cue.action)))
                    Button(properties,ModeName(mode),0,66+row++*54,372,44,() => { workbench.SetSelectedAction(mode); OpenDetails("node"); });
                return;
            }
            Number("Start time",$"{cue.time:0.00} seconds",0,() => workbench.MoveSelected(cue.time-Nudge),() => workbench.MoveSelected(cue.time+Nudge));
            Label(properties,"Behavior",0,104,372,30,17);
            Button(properties,ModeName(cue.action),0,142,372,44,() => OpenDetails("modes"));
            bool animation=cue.action is BossController.ActionKind.PlayAnimation or BossController.ActionKind.SetIdle;
            if (animation)
            {
                bool missing=!string.IsNullOrEmpty(cue.recording?.savedName) && !workbench.boss.HasClip(cue);
                Label(properties,missing?"Saved animation (unavailable)":"Saved animation",0,200,372,24,17).color=missing?PosePanelLayout.Warning:PosePanelLayout.Foreground;
                Button(properties,string.IsNullOrEmpty(cue.recording?.savedName)?"Choose animation":cue.recording.savedName,0,230,372,40,() => PickAnimation(false));
            }
            if (BossController.HasTransition(cue.action))
            {
                float y=animation?286:202;
                bool finish=cue.transitionTiming==BossController.TransitionTiming.FinishAtNode;
                Label(properties,"Transition timing",0,y,372,24,17);
                Button(properties,finish?"Finish at node":"Start at node",0,y+30,372,40,
                    () => workbench.SetTransitionTiming(finish?BossController.TransitionTiming.StartAtNode:BossController.TransitionTiming.FinishAtNode));
                float seconds=workbench.boss.TransitionDuration(cue);
                Number("Transition duration",$"{seconds:0.00} seconds",y+82,
                    () => workbench.SetTransitionSeconds(seconds-.05f),() => workbench.SetTransitionSeconds(seconds+.05f));
            }
            if (cue.action==BossController.ActionKind.PlayAnimation)
                Number("Playback speed",$"{cue.speed:0.0}x",468,() => workbench.SetSpeed(cue.speed-.1f),() => workbench.SetSpeed(cue.speed+.1f));
            else if (cue.action is BossController.ActionKind.FaceAngle or BossController.ActionKind.DummyAngle)
                Number("Arena angle",$"{cue.angle:0}°",cue.action==BossController.ActionKind.FaceAngle?386:224,() => workbench.AdjustAngle(-15),() => workbench.AdjustAngle(15));
            else if (BossController.IsAttack(cue.action))
                Label(properties,cue.action switch
                {
                    BossController.ActionKind.FireOrb => "Fires the editable orb prefab in the direction the boss is facing.",
                    BossController.ActionKind.FireLaser => "Marks a straight path with a red floor line, then fires a fast laser along it. Aim locks when the warning starts.",
                    _ => "Warns the area ahead, then raises the editable spike row."
                },0,224,372,152,19);
            Button(properties,"Duplicate",0,584,178,42,workbench.DuplicateSelected);
            Button(properties,"Delete",194,584,178,42,workbench.DeleteSelected);
        }
        private void Picker(string[] items,Action<string> choose)
        {
            const int count = 7;
            int pages = Mathf.Max(1,Mathf.CeilToInt(items.Length/(float)count));
            page = Mathf.Clamp(page,0,pages-1);
            if (items.Length==0) Label(properties,"No saved files found.",0,66,372,80,20);
            for (int i=page*count;i<Mathf.Min(items.Length,(page+1)*count);i++)
            {
                string item = items[i];
                Button(properties,item,0,66+(i-page*count)*54,372,44,() => choose(item));
            }
            if (pages<=1) return;
            Button(properties,"Previous",0,488,112,40,() => { page--; detailsDirty = true; });
            Label(properties,$"{page+1} / {pages}",126,488,112,40,18);
            Button(properties,"Next",260,488,112,40,() => { page++; detailsDirty = true; });
        }
        private float Nudge => workbench.snap>0 ? workbench.snap : 1f/30;
        private void Number(string name,string value,float y,UnityAction minus,UnityAction plus)
        {
            Label(properties,name,0,y,372,30,17);
            Button(properties,"-",0,y+38,56,40,minus);
            Label(properties,value,68,y+38,236,40,22,true);
            Button(properties,"+",316,y+38,56,40,plus);
        }
        private TMP_Text Label(Transform parent,string value,float x,float y,float width,float height,int size=20,bool bold=false) =>
            layout.Label(parent,value,x,y,width,height,size,bold);
        private TMP_Text Button(Transform parent,string name,float x,float y,float width,float height,UnityAction action,bool primary=false)
        {
            var label = layout.Button(parent,name,x,y,width,height,action,primary); label.fontSize = 17;
            return label;
        }
        private static void Clear(Transform target)
        {
            foreach (Transform child in target) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        }
        private void Pan(int direction) { viewStart = Mathf.Clamp(viewStart+direction*windowSeconds*.5f,0,Mathf.Max(0,workbench.boss.TimelineDuration-windowSeconds)); Redraw(); }
        private void Zoom(float factor) { windowSeconds = Mathf.Clamp(windowSeconds*factor,2,120); viewStart = Mathf.Clamp(workbench.Cursor-windowSeconds*.25f,0,Mathf.Max(0,workbench.boss.TimelineDuration-windowSeconds)); Redraw(); }
        private void Leave(SceneDestination destination)
        {
            GuardUnsaved(() => SceneHandler.TransitionTo(destination));
        }
        private void GuardUnsaved(Action next)
        {
            if (confirmation.IsOpen) return;
            bool pendingName=view=="name" && nameBuffer!=workbench.saveName;
            if (!workbench.Dirty && !pendingName) { next(); return; }
            if (workbench.Playing) workbench.TogglePlay();
            confirmation.Show("Unsaved changes",$"Discard unsaved changes to '{workbench.saveName}' and continue?",
                "Discard and leave",next);
        }
        private void EditName()
        {
            nameBuffer=workbench.saveName;
            OpenDetails("name");
        }
        private bool ApplyName()
        {
            if (!workbench.SetName(nameBuffer)) return false;
            OpenDetails("encounter"); return true;
        }
        private void RequestSave()
        {
            if (view=="name" && !ApplyName()) return;
            workbench.Save();
        }
        private void UpdateNameDisplay()
        {
            if (nameDisplay!=null) nameDisplay.text=nameBuffer;
            if (nameInput!=null) nameInput.SetTextWithoutNotify(nameBuffer);
        }
        private void AppendName(char letter)
        {
            if (nameBuffer.Length<64) nameBuffer+=letter;
            UpdateNameDisplay();
        }
        private void DrawNameEditor()
        {
            detailTitle.text=workbench.IsNew?"Name timeline":"Rename timeline";
            Label(properties,"Timeline and level name",0,0,372,40,22,true);
            nameDisplay=Label(properties,nameBuffer,8,54,356,52,22);
            nameInput=layout.NameField(properties,0,54,372,52);
            nameInput.characterLimit=64; nameInput.SetTextWithoutNotify(nameBuffer);
            nameInput.onValueChanged.AddListener(value=>nameBuffer=value);
            nameInput.gameObject.SetActive(!inHeadset);
            nameDisplay.gameObject.SetActive(inHeadset);
            const string keys="1234567890QWERTYUIOPASDFGHJKL-ZXCVBNM_";
            for (int i=0;i<keys.Length;i++)
            {
                char key=keys[i]; char letter=capitalLetters?key:char.ToLowerInvariant(key);
                Button(properties,letter.ToString(),i%10*37,142+i/10*52,33,44,() => AppendName(letter));
            }
            Button(properties,"Shift",0,376,80,44,() => { capitalLetters=!capitalLetters; detailsDirty=true; });
            Button(properties,"Space",90,376,112,44,() => AppendName(' '));
            Button(properties,"Backspace",212,376,160,44,() => { if(nameBuffer.Length>0)nameBuffer=nameBuffer[..^1]; UpdateNameDisplay(); });
            Button(properties,"Clear",0,438,100,44,() => { nameBuffer=""; UpdateNameDisplay(); });
            Button(properties,"Cancel",110,438,100,44,() => OpenDetails("encounter"));
            Button(properties,"Done",220,438,152,44,() => ApplyName(),true);
            Label(properties,"This name also appears in the level list. Use Save to create the level or save a rename.",0,516,372,110,18);
        }
    }
}
