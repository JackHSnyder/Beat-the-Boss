using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(BossRig))]
    public sealed class BossController : MonoBehaviour
    {
        public enum ActionKind { FaceAngle, FollowPlayer, StopFollowing, PlayAnimation, SetIdle, PauseIdle, ResumeIdle, DummyAngle, FireOrb, SpikeRow, FireLaser }
        public enum TransitionTiming { StartAtNode, FinishAtNode }
        internal static bool HasTransition(ActionKind action) => action is ActionKind.FaceAngle or ActionKind.PlayAnimation or ActionKind.SetIdle;
        internal static bool IsAttack(ActionKind action) => action is ActionKind.FireOrb or ActionKind.SpikeRow or ActionKind.FireLaser;
        [Serializable] public sealed class Recording
        {
            [Tooltip("Saved animation name. Device recordings take precedence over the bundled JSON.")]
            public string savedName;
            public TextAsset bundledJson;
            public MoveAnimation Load(UnityEngine.Object context)
            {
                try
                {
                    TextAsset source = bundledJson;
                    if (source == null && context is BossController boss)
                        foreach (BundledAnimation item in boss.bundledAnimations)
                            if (item.name == savedName) { source = item.json; break; }
                    MoveAnimation animation = !string.IsNullOrWhiteSpace(savedName) && AnimationStore.Exists(savedName)
                        ? AnimationStore.Load(savedName) : source != null ? AnimationStore.ReadJson(source.text) : null;
                    if (animation?.HasFrames == true) return animation;
                    if (!string.IsNullOrWhiteSpace(savedName) || bundledJson != null)
                        Debug.LogWarning("Boss recording has no animation: " + savedName, context);
                }
                catch (Exception e) when (AnimationStore.IsFileError(e))
                { Debug.LogWarning("Could not load boss recording: " + e.Message, context); }
                return null;
            }
        }
        [Serializable] public sealed class Cue
        {
            [Min(0)] public float time;
            public ActionKind action;
            [Tooltip("Arena angle: 0 = +X, 90 = +Z, matching MoveCircle.")]
            public float angle;
            public TransitionTiming transitionTiming;
            [Min(0), Tooltip("Seconds to blend or turn. Zero uses the controller's default.")]
            public float transitionSeconds;
            public Recording recording = new();
            [Min(.01f)] public float speed = 1;
            public UnityEvent onStarted = new();
        }
        [Serializable] public sealed class BundledAnimation { public string name; public TextAsset json; }
        public BundledAnimation[] bundledAnimations = Array.Empty<BundledAnimation>();
        public GameObject characterPrefab;
        public string songId;
        public string bossModelId = DownloadedAssetLibrary.DefaultCharacter;
        [Header("Score")]
        [Min(1)] public int pointsPerHit = 100;
        [Min(1)] public int targetPoints = 1000;
        public LevelEndBasis endBasis = LevelEndBasis.Timeline;
        internal BossTimelineStore.Document LoadedLevel { get; private set; }
        [Min(0)] public float songDelay;
        public double ElapsedTime => cycle*(double)TimelineDuration+clock;
        private Transform originalVisual;
        private GameObject selectedVisual;
        private string appliedModelId=DownloadedAssetLibrary.DefaultCharacter;
        public MoveCircle arena;
        public Transform player;
        public Recording idle = new() { savedName = "Boss Idle" };
        public List<Cue> timeline = new();
        [SerializeField] private BossProjectile orbPrefab;
        [SerializeField] private Transform orbLaunchPoint;
        [SerializeField] private BossSpikeRow spikeRowPrefab;
        [SerializeField] private BossLaserAttack laserPrefab;
        [SerializeField] private Transform laserLaunchPoint;
        [Tooltip("Default timeline and save selection shared with the encounter editor. Leave blank to use Inspector cues.")]
        public string timelineName = "Boss Timeline";
        public bool playOnStart = true;
        [NonSerialized] public bool previewMode;
        public bool IsPlaying => playing;
        public float TimePosition => clock;
        public float TimelineDuration { get; private set; }
        public float dummyStartAngle = 270;
        private Quaternion initialRotation;
        private MoveAnimation initialIdle;
        private int cycle, eventIndex;
        private bool seekPartial;
        private readonly List<ScheduledEvent> schedule = new();
        private readonly List<Cue> attacksAtPoint = new();
        private sealed class ScheduledEvent
        {
            public Cue cue;
            public float time, duration;
            public int priority, order;
            public bool prepare, firstCycleOnly;
        }
        private Cue preparedAnimation;
        private bool holdMove, holdIdle, animationBlending, forcePose;
        private float animationBlendClock, animationBlendDuration;
        private readonly List<Pose> blendFrom = new();
        private bool turning;
        private float turnFrom, turnTo, turnClock, turnDuration;
        private double accumulator;
        private const float Tick = 1f / 60f;
        [HideInInspector] public bool loop = true;
        [Min(.1f)] public float loopDuration = 12;
        [Min(.01f)] public float turnSmoothTime = .25f;
        [Min(1)] public float turnSpeed = 120;
        [Min(.01f)] public float blendIn = .12f;
        [Min(.01f)] public float blendOut = .18f;
        [Min(0)] public float poseSmoothing = .035f;
        [Min(0)] public float idleLoopBlend = .2f;
        public bool proceduralFeet = true;
        [Min(.01f)] public float stepDuration = .28f;
        [Min(0)] public float stepHeight = .055f;
        [Min(1)] public float stepAngle = 15;
        private BossRig rig;
        private MoveAnimation idleClip, moveClip;
        private readonly List<PoseJoint> sample = new();
        private readonly Dictionary<string, PoseJoint> next = new();
        private readonly List<Cue> ordered = new();
        private readonly Dictionary<Cue, MoveAnimation> clips = new();
        private readonly List<BossProjectile> previewProjectiles = new();
        private readonly List<BossSpikeRow> previewSpikeRows = new();
        private readonly List<BossLaserAttack> previewLasers = new();
        private float clock, idleClock, moveClock, moveSpeed = 1, yawVelocity, targetYaw;
        private bool playing, following, idlePaused;
        private readonly Foot[] feet = { new(), new() };
        private int stepping = -1, nextFoot;
        private float stepClock, feetWeight;
        private sealed class Foot
        {
            public Transform upper, lower, joint;
            public Vector3 home, planted, from, to;
            public Quaternion rotation, fromRotation, toRotation, localRotation;
            public float yaw;
        }
        private void Awake()
        {
            rig = GetComponent<BossRig>();
            if (rig.rigRoot == null && characterPrefab != null)
            {
                var holder = new GameObject("Boss Visual");
                holder.transform.SetParent(transform, false); holder.SetActive(false);
                GameObject visual = Instantiate(characterPrefab, holder.transform);
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                BossRig.DisableLiveDrivers(visual);
                rig.rigRoot = visual.transform;
                holder.SetActive(true);
            }
            rig.Initialize();
            originalVisual=rig.rigRoot;
            gameObject.AddComponent<BossSongPlayback>();
            initialRotation = transform.rotation;
            targetYaw = transform.eulerAngles.y;
            if (arena == null) foreach (MoveCircle c in FindObjectsByType<MoveCircle>()) if (c.gameObject.scene == gameObject.scene) { arena = c; break; }
            if (player == null) foreach (OVRCameraRig c in FindObjectsByType<OVRCameraRig>()) if (c.gameObject.scene == gameObject.scene) { player = c.centerEyeAnchor; break; }
            BindFeet();
            SetCharacter(bossModelId);
            if (GetComponent<BossScore>() == null) gameObject.AddComponent<BossScore>();
            if (GetComponent<EncounterCompletion>() == null) gameObject.AddComponent<EncounterCompletion>();
        }
        private void BindFeet()
        {
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right"; Foot f = feet[i];
                f.upper = rig.Joint(side + "Thigh"); f.lower = rig.Joint(side + "Shin"); f.joint = rig.Joint(side + "Foot");
                if (f.joint == null) continue;
                f.home = transform.InverseTransformPoint(f.joint.position); f.planted = f.joint.position;
                f.rotation = f.joint.rotation; f.localRotation = Quaternion.Inverse(transform.rotation) * f.rotation; f.yaw = targetYaw;
            }
        }
        internal void SetCharacter(string id)
        {
            bossModelId=string.IsNullOrEmpty(id)?DownloadedAssetLibrary.DefaultCharacter:id;
            if (rig==null || appliedModelId==bossModelId) return;
            GameObject prefab;
            try { prefab=DownloadedAssetLibrary.ResolveCharacter(bossModelId); }
            catch (Exception e) when (AnimationStore.IsFileError(e))
            {
                Debug.LogWarning("Using StylizedCharacter: "+e.Message,this);
                // Keep the requested ID in the level so reinstalling the missing package repairs it.
                if (appliedModelId!=DownloadedAssetLibrary.DefaultCharacter) ApplyVisual(null);
                return;
            }
            ApplyVisual(bossModelId==DownloadedAssetLibrary.DefaultCharacter?null:prefab);
        }
        private void ApplyVisual(GameObject prefab)
        {
            rig.BeginPose();
            if (selectedVisual!=null) { selectedVisual.SetActive(false); Destroy(selectedVisual); }
            Transform root=originalVisual;
            if (prefab!=null)
            {
                selectedVisual=new GameObject("Selected boss character"); selectedVisual.SetActive(false);
                selectedVisual.transform.SetParent(transform,false);
                GameObject visual=Instantiate(prefab,selectedVisual.transform,false);
                visual.transform.SetLocalPositionAndRotation(originalVisual.localPosition,originalVisual.localRotation);
                visual.transform.localScale=originalVisual.localScale;
                BossRig.DisableLiveDrivers(visual);
                var originalRenderer=originalVisual.GetComponentInChildren<Renderer>(true);
                if(originalRenderer!=null)foreach(Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                    renderer.gameObject.layer=originalRenderer.gameObject.layer;
                root=visual.transform; selectedVisual.SetActive(true);
            }
            originalVisual.gameObject.SetActive(prefab==null);
            appliedModelId=prefab==null?DownloadedAssetLibrary.DefaultCharacter:bossModelId;
            rig.Rebind(root); BindFeet();
        }
        private void Start()
        {
            if (previewMode) return;
            try
            {
                var doc = BossTimelineStore.LoadForEncounter(timelineName, out _);
                LoadedLevel = doc;
                if (doc != null)
                    BossTimelineStore.Apply(doc, this);
            }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { Debug.LogWarning(e.Message, this); }
            loop = endBasis != LevelEndBasis.Timeline;
            RebuildTimeline();
            if (playOnStart) StartCoroutine(PlayWhenSceneReady());
        }
        private System.Collections.IEnumerator PlayWhenSceneReady()
        {
            while (SceneHandler.IsTransitioning) yield return null;
            playing = true; FireInitialEvents();
        }
        [ContextMenu("Play / restart timeline")]
        public void Play()
        {
            if (!Application.isPlaying) return;
            loop = endBasis != LevelEndBasis.Timeline;
            RebuildTimeline(); playing = true; FireInitialEvents();
        }
        private void FireInitialEvents()
        {
            if (previewMode) return;
            foreach (Cue cue in ordered)
            {
                if (cue.time > .000001f) break;
                FireAttack(cue); cue.onStarted?.Invoke();
            }
        }
        public float TransitionDuration(Cue cue)
        {
            float seconds = cue.transitionSeconds > 0 ? cue.transitionSeconds :
                cue.action == ActionKind.FaceAngle ? turnSmoothTime : blendIn;
            seconds = Mathf.Max(.01f, seconds);
            return loop && cue.transitionTiming == TransitionTiming.FinishAtNode
                ? Mathf.Min(seconds,TimelineDuration) : seconds;
        }
        private void BuildSchedule()
        {
            schedule.Clear();
            for (int i=0;i<ordered.Count;i++)
            {
                Cue cue = ordered[i];
                schedule.Add(new ScheduledEvent { cue=cue,time=cue.time,priority=1,order=i });
                if (!HasTransition(cue.action) || cue.transitionTiming != TransitionTiming.FinishAtNode) continue;
                float duration = TransitionDuration(cue), start = cue.time-duration;
                if (start >= 0)
                    schedule.Add(new ScheduledEvent { cue=cue,time=start,duration=duration,prepare=true,order=i });
                else
                {
                    // The first traversal has no preceding cycle. Use the time available before this node.
                    if (cue.time > .000001f)
                        schedule.Add(new ScheduledEvent { cue=cue,time=0,duration=cue.time,prepare=true,firstCycleOnly=true,order=i });
                    if (loop)
                        schedule.Add(new ScheduledEvent { cue=cue,time=TimelineDuration+start,duration=duration,prepare=true,priority=2,order=i });
                }
            }
            schedule.Sort((a,b) => { int c=a.time.CompareTo(b.time); if(c==0)c=a.priority.CompareTo(b.priority); return c==0?a.order.CompareTo(b.order):c; });
        }
        public void RebuildTimeline()
        {
            ordered.Clear(); clips.Clear();
            foreach (Cue cue in timeline)
                if (cue != null && (previewMode || cue.action != ActionKind.DummyAngle))
                    ordered.Add(cue);
            for (int i = 1; i < ordered.Count; i++) { Cue c = ordered[i]; int j = i - 1; while (j >= 0 && ordered[j].time > c.time) { ordered[j + 1] = ordered[j]; j--; } ordered[j + 1] = c; }
            TimelineDuration = Mathf.Max(.1f, loopDuration);
            foreach (Cue cue in ordered)
            {
                if (cue.action is ActionKind.PlayAnimation or ActionKind.SetIdle) clips[cue] = cue.recording?.Load(this);
                clips.TryGetValue(cue,out MoveAnimation clip);
                float duration = EventDuration(cue.action,clip,cue.speed,loop,spikeRowPrefab,laserPrefab);
                TimelineDuration = Mathf.Max(TimelineDuration, cue.time + duration);
            }
            initialIdle = idle?.Load(this);
            BuildSchedule(); ResetSimulation();
        }
        public float ClipDuration(Cue cue) => cue.action == ActionKind.PlayAnimation && clips.TryGetValue(cue, out MoveAnimation clip) && clip != null
            ? (clip.PlaybackEnd - clip.PlaybackStart) / Mathf.Max(.01f, cue.speed) : 0;
        internal static float EventDuration(ActionKind action,MoveAnimation clip,float speed,bool repeating,BossSpikeRow spikes,BossLaserAttack laser)
        {
            // Shared with level details so the displayed length equals the encounter's actual ending time.
            if (repeating && IsAttack(action)) return 0;
            if (action==ActionKind.SpikeRow && spikes!=null) return spikes.Duration;
            if (action==ActionKind.FireLaser && laser!=null) return laser.Duration;
            return action==ActionKind.PlayAnimation && clip!=null ? (clip.PlaybackEnd-clip.PlaybackStart)/Mathf.Max(.01f,speed) : 0;
        }
        public bool HasClip(Cue cue) => clips.TryGetValue(cue, out MoveAnimation clip) && clip != null;
        private void ResetSimulation()
        {
            ClearPreviewAttacks();
            playing = false; accumulator = 0; cycle = eventIndex = 0; seekPartial = false;
            clock = idleClock = moveClock = yawVelocity = 0;
            turning = animationBlending = holdMove = holdIdle = false; preparedAnimation = null;
            forcePose = true;
            idlePaused = following = false; idleClip = initialIdle; moveClip = null;
            transform.rotation = initialRotation; targetYaw = transform.eulerAngles.y;
            rig.BeginPose();
            stepping = -1; nextFoot = 0; stepClock = 0; feetWeight = 0;
            foreach (Foot f in feet) if (f.joint != null)
            { f.planted = transform.TransformPoint(f.home); f.rotation = transform.rotation * f.localRotation; f.yaw = targetYaw; }
            if (previewMode) PlaceDummy(dummyStartAngle);
            ProcessEvents(false);
            RenderPose(0);
        }
        // Manual scrubbing reconstructs the first traversal. Playback uses AdvancePreview and never seeks at a wrap.
        public void Seek(float time)
        {
            float target = Mathf.Clamp(time,0,TimelineDuration);
            if (cycle > 0 || target < clock-.000001f || (seekPartial && Mathf.Abs(target-clock)>.000001f))
                ResetSimulation();
            accumulator = 0;
            while (clock+Tick <= target+.000001f) Simulate(Tick,false);
            float remainder = target-clock;
            seekPartial = remainder > .000001f;
            if (seekPartial) Simulate(remainder,false);
            if (Mathf.Abs(clock-target)<.00001f) clock=target;
        }
        public bool AdvancePreview(float seconds)
        {
            AdvanceTimeline(seconds,false);
            return loop || clock < TimelineDuration-.000001f;
        }
        private void AdvanceTimeline(float seconds,bool events)
        {
            accumulator += Mathf.Max(0,seconds);
            while (accumulator+1e-6 >= Tick)
            {
                accumulator = Math.Max(0,accumulator-Tick);
                float remaining = Tick;
                while (remaining > .000001f)
                {
                    if (clock >= TimelineDuration-.000001f)
                    {
                        if (!loop) { playing=false; accumulator=0; return; }
                        // Only the event cursor wraps. Facing, clips, idle phase, feet, dummy and attacks keep running.
                        clock=0; cycle++; eventIndex=0; ProcessEvents(events);
                    }
                    float step = Mathf.Min(remaining,TimelineDuration-clock);
                    Simulate(step,events); remaining-=step;
                    if (clock >= TimelineDuration-.000001f)
                    {
                        if (!loop) { playing=false; accumulator=0; return; }
                        clock=0; cycle++; eventIndex=0; ProcessEvents(events);
                    }
                }
            }
        }
        public void StopTimeline()
        {
            playing = false;
            // A death during the incoming fade must also cancel the deferred first play.
            StopAllCoroutines();
        }
        public void ResumeTimeline() => playing = true;
        private void PlaceDummy(float angle)
        {
            if (!previewMode || player == null || arena == null) return;
            player.position = arena.GetWorldPoint(angle);
            Vector3 facing = transform.position - player.position; facing.y = 0;
            if (facing.sqrMagnitude > .001f) player.rotation = Quaternion.LookRotation(facing);
        }
        public void PerformMove(MoveAnimation animation, float speed = 1)
        {
            if (animation?.HasFrames != true) return;
            CaptureBlend(blendIn);
            moveClip=animation; moveClock=0; moveSpeed=Mathf.Max(.01f,speed); holdMove=false;
            preparedAnimation=null;
        }
        private void LateUpdate()
        {
            if (!previewMode && playing) AdvanceTimeline(Time.deltaTime,true);
        }
        private void Simulate(float seconds,bool events)
        {
            float end = Mathf.Min(TimelineDuration,clock+seconds);
            while (clock < end-.000001f)
            {
                float next = eventIndex < schedule.Count ? Mathf.Min(end,schedule[eventIndex].time) : end;
                float step = Mathf.Max(0,next-clock);
                if (step > 0)
                {
                    AdvanceAttacks(step); clock=next; RenderPose(step);
                }
                ProcessEvents(events);
            }
        }
        private void ProcessEvents(bool events)
        {
            bool changed=false;
            attacksAtPoint.Clear();
            while (eventIndex < schedule.Count && schedule[eventIndex].time <= clock+.000001f)
            {
                ScheduledEvent item=schedule[eventIndex++];
                if (item.firstCycleOnly && cycle>0) continue;
                changed=true;
                if (item.prepare)
                {
                    if (item.cue.action==ActionKind.FaceAngle) BeginTurn(item.cue,item.duration);
                    else BeginAnimation(item.cue,item.duration,true);
                }
                else
                {
                    Execute(item.cue,events);
                    if (events || previewMode) attacksAtPoint.Add(item.cue);
                }
            }
            if (!changed) return;
            RenderPose(0);
            foreach (Cue cue in attacksAtPoint) FireAttack(cue);
        }
        private void AdvanceAttacks(float dt)
        {
            for (int i=previewProjectiles.Count-1;i>=0;i--)
                if (previewProjectiles[i]==null || !previewProjectiles[i].AdvancePreview(dt)) previewProjectiles.RemoveAt(i);
            for (int i=previewSpikeRows.Count-1;i>=0;i--)
                if (previewSpikeRows[i]==null || !previewSpikeRows[i].Advance(dt)) previewSpikeRows.RemoveAt(i);
            for (int i=previewLasers.Count-1;i>=0;i--)
                if (previewLasers[i]==null || !previewLasers[i].Advance(dt)) previewLasers.RemoveAt(i);
        }
        private void FireAttack(Cue cue)
        {
            switch (cue.action)
            {
                case ActionKind.FireOrb when orbPrefab != null && orbLaunchPoint != null:
                    BossProjectile projectile=Instantiate(orbPrefab,orbLaunchPoint.position,Quaternion.LookRotation(transform.forward));
                    projectile.Launch(previewMode);
                    if (previewMode) previewProjectiles.Add(projectile);
                    break;
                case ActionKind.SpikeRow: LaunchSpikeRow(); break;
                case ActionKind.FireLaser: LaunchLaser(); break;
            }
        }

        private void LaunchLaser()
        {
            if (laserPrefab == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;
            if (forward.sqrMagnitude < .001f) return;
            Vector3 origin = laserLaunchPoint != null ? laserLaunchPoint.position :
                orbLaunchPoint != null ? orbLaunchPoint.position : transform.position+Vector3.up*1.35f+forward*.6f;
            float floorY = arena != null ? arena.transform.position.y : transform.position.y;
            Vector3 ground = new(origin.x,floorY,origin.z);
            float edge = 30;
            if (arena != null && !arena.TryGetForwardEdgeDistance(ground,forward,out edge)) edge = 30;
            BossLaserAttack laser = Instantiate(laserPrefab,origin,Quaternion.LookRotation(forward));
            laser.Launch(floorY,edge,transform,previewMode);
            if (previewMode) previewLasers.Add(laser);
        }

        private void LaunchSpikeRow()
        {
            if (spikeRowPrefab == null || arena == null)
                return;
            Vector3 origin = transform.position;
            origin.y = arena.transform.position.y;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (!arena.TryGetForwardEdgeDistance(origin, forward, out float distance))
                return;

            BossSpikeRow row = Instantiate(spikeRowPrefab, origin, Quaternion.LookRotation(forward));
            row.Launch(distance, previewMode);
            if (previewMode)
                previewSpikeRows.Add(row);
        }

        private void OnDisable() => ClearPreviewAttacks();

        private void ClearPreviewAttacks()
        {
            foreach (BossProjectile projectile in previewProjectiles)
                if (projectile != null)
                    projectile.Retire();
            previewProjectiles.Clear();
            foreach (BossSpikeRow row in previewSpikeRows)
                if (row != null)
                    row.Retire();
            previewSpikeRows.Clear();
            foreach (BossLaserAttack laser in previewLasers)
                if (laser != null) laser.Retire();
            previewLasers.Clear();
        }
        private void CaptureBlend(float duration)
        {
            rig.CapturePose(blendFrom);
            animationBlendClock=0; animationBlendDuration=duration;
            animationBlending=duration>.000001f; forcePose=true;
        }
        private void BeginAnimation(Cue cue,float duration,bool before)
        {
            if (!clips.TryGetValue(cue,out MoveAnimation clip) || clip==null) return;
            CaptureBlend(duration);
            if (cue.action==ActionKind.PlayAnimation)
            { moveClip=clip; moveClock=0; moveSpeed=Mathf.Max(.01f,cue.speed); holdMove=before; }
            else { idleClip=clip; idleClock=0; holdIdle=before; }
            preparedAnimation=before?cue:null;
        }
        private void AnimationNode(Cue cue)
        {
            if (!clips.TryGetValue(cue,out MoveAnimation clip) || clip==null) return;
            if (cue.transitionTiming==TransitionTiming.StartAtNode)
            { BeginAnimation(cue,TransitionDuration(cue),false); return; }
            if (preparedAnimation!=cue) BeginAnimation(cue,0,false);
            if (cue.action==ActionKind.PlayAnimation) holdMove=false; else holdIdle=false;
            preparedAnimation=null; animationBlending=false; forcePose=true;
        }
        private float AngleTarget(Cue cue)
        {
            Vector3 point=arena!=null ? arena.GetWorldPoint(cue.angle) :
                transform.position+new Vector3(Mathf.Cos(cue.angle*Mathf.Deg2Rad),0,Mathf.Sin(cue.angle*Mathf.Deg2Rad));
            Vector3 delta=point-transform.position;
            return delta.x*delta.x+delta.z*delta.z>.0001f ? Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg : transform.eulerAngles.y;
        }
        private void BeginTurn(Cue cue,float duration)
        {
            following=false; yawVelocity=0; turnFrom=transform.eulerAngles.y; turnTo=AngleTarget(cue);
            targetYaw=turnTo; turnClock=0; turnDuration=Mathf.Max(.000001f,duration); turning=true;
        }
        private void RenderPose(float dt)
        {
            if (following && player!=null) Face(player.position);
            float yaw=transform.eulerAngles.y;
            if (turning)
            {
                turnClock+=dt;
                yaw=Mathf.LerpAngle(turnFrom,turnTo,Mathf.SmoothStep(0,1,Mathf.Clamp01(turnClock/turnDuration)));
                if (turnClock>=turnDuration-.000001f) { yaw=turnTo; turning=false; yawVelocity=0; }
            }
            else if (dt>0)
                yaw=Mathf.SmoothDampAngle(yaw,targetYaw,ref yawVelocity,Mathf.Max(.01f,turnSmoothTime),Mathf.Max(1,turnSpeed),dt);
            transform.rotation=Quaternion.Euler(0,yaw,0);
            // Point events which do not change animation must not bypass smoothing and pop the current pose.
            if (dt<=0 && !forcePose && !animationBlending) return;
            bool explicitBlend=animationBlending || forcePose;
            rig.BeginPose();
            if (!idlePaused && !holdIdle) idleClock+=dt;
            if (idleClip!=null)
            {
                float length=idleClip.PlaybackEnd-idleClip.PlaybackStart;
                float seam=Mathf.Min(idleLoopBlend,length*.5f);
                float phase=Mathf.Repeat(idleClock,Mathf.Max(.001f,length-seam));
                Sample(idleClip,idleClip.PlaybackStart+phase); rig.Apply(sample,idleClip,1);
                if (seam>0 && idleClock>=length-seam && phase<seam)
                { Sample(idleClip,idleClip.PlaybackEnd-seam+phase); rig.Apply(sample,idleClip,1-Mathf.SmoothStep(0,1,phase/seam)); }
            }
            float lowerBodyMove=0;
            if (moveClip!=null)
            {
                if (!holdMove) moveClock+=dt;
                float duration=(moveClip.PlaybackEnd-moveClip.PlaybackStart)/moveSpeed;
                float exit=Mathf.Min(blendOut,duration*.5f);
                float weight=Mathf.SmoothStep(0,1,Mathf.Clamp01((duration-moveClock)/Mathf.Max(.001f,exit)));
                Sample(moveClip,moveClip.PlaybackStart+Mathf.Clamp(moveClock,0,duration)*moveSpeed);
                rig.Apply(sample,moveClip,weight);
                if (BossRig.Enabled(moveClip,"LeftFoot") || BossRig.Enabled(moveClip,"RightFoot")) lowerBodyMove=weight;
                if (!holdMove && moveClock>=duration) moveClip=null;
            }
            if (animationBlending)
            {
                animationBlendClock+=dt;
                float weight=Mathf.SmoothStep(0,1,Mathf.Clamp01(animationBlendClock/animationBlendDuration));
                rig.BlendFrom(blendFrom,weight);
                if (animationBlendClock>=animationBlendDuration-.000001f) animationBlending=false;
            }
            if (dt>0 && !explicitBlend) rig.Smooth(poseSmoothing,dt);
            UpdateFeet(dt,lowerBodyMove);
            forcePose=false;
        }
        private void Sample(MoveAnimation clip, float time) => clip.Sample(time, sample, next);
        private void Face(Vector3 point)
        { Vector3 d = point - transform.position; if (d.x * d.x + d.z * d.z > .0001f) targetYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
        private void Execute(Cue cue, bool events)
        {
            switch (cue.action)
            {
                case ActionKind.FaceAngle:
                    if (cue.transitionTiming==TransitionTiming.FinishAtNode)
                    {
                        following=turning=false; yawVelocity=0; targetYaw=AngleTarget(cue);
                        transform.rotation=Quaternion.Euler(0,targetYaw,0);
                    }
                    else BeginTurn(cue,TransitionDuration(cue));
                    break;
                case ActionKind.FollowPlayer: turning=false; following=true; break;
                case ActionKind.StopFollowing: following=turning=false; yawVelocity=0; targetYaw=transform.eulerAngles.y; break;
                case ActionKind.PlayAnimation: case ActionKind.SetIdle: AnimationNode(cue); break;
                case ActionKind.PauseIdle: idlePaused = true; break;
                case ActionKind.ResumeIdle: idlePaused = false; break;
                case ActionKind.DummyAngle: PlaceDummy(cue.angle); break;
            }
            if (events) cue.onStarted?.Invoke();
        }
        private void UpdateFeet(float dt, float lowerBodyMove)
        {
            float desiredWeight = proceduralFeet ? 1 - lowerBodyMove : 0;
            feetWeight = Mathf.MoveTowards(feetWeight, desiredWeight, dt / .12f);
            if (feet[0].joint == null || feet[1].joint == null) return;
            if (lowerBodyMove > .95f || !proceduralFeet)
            {
                stepping = -1;
                foreach (Foot f in feet) { f.planted = f.joint.position; f.rotation = f.joint.rotation; f.yaw = transform.eulerAngles.y; }
                return;
            }
            if (stepping < 0)
            {
                Foot f = feet[nextFoot];
                if (Mathf.Abs(Mathf.DeltaAngle(f.yaw, transform.eulerAngles.y)) > stepAngle || Vector3.Distance(f.planted, transform.TransformPoint(f.home)) > .14f)
                {
                    stepping = nextFoot; nextFoot = 1 - nextFoot; stepClock = 0;
                    f.from = f.planted; f.to = transform.TransformPoint(f.home);
                    f.fromRotation = f.rotation; f.toRotation = transform.rotation * f.localRotation; f.yaw = transform.eulerAngles.y;
                }
            }
            if (stepping >= 0)
            {
                Foot f = feet[stepping]; stepClock += dt; float t = Mathf.Clamp01(stepClock / Mathf.Max(.01f, stepDuration));
                f.planted = Vector3.Lerp(f.from, f.to, Mathf.SmoothStep(0, 1, t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * stepHeight);
                f.rotation = Quaternion.Slerp(f.fromRotation, f.toRotation, Mathf.SmoothStep(0, 1, t));
                if (t >= 1) stepping = -1;
            }
            foreach (Foot f in feet)
            {
                Quaternion r = Quaternion.Slerp(f.joint.rotation, f.rotation, feetWeight);
                BossRig.SolveLimb(f.upper, f.lower, f.joint, Vector3.Lerp(f.joint.position, f.planted, feetWeight), transform.forward);
                f.joint.rotation = r;
            }
        }
    }
}
