using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Deterministic checks for event timing, cross-boundary state and finite animation/rotation blends.
    internal static class BossTimelineTransitionChecks
    {
        [Serializable] private sealed class AnimationFile
        {
            public int version=1;
            public string name="Transition test";
            public MoveAnimation animation;
        }
        private sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            public readonly BossController boss;
            public readonly Transform finger;
            private readonly List<TextAsset> clips=new();
            public Fixture()
            {
                root=new GameObject("Timeline transition check (temporary)");
                var wrist=new GameObject("Wrist").transform; wrist.SetParent(root.transform,false);
                finger=new GameObject("Finger").transform; finger.SetParent(wrist,false);
                var rig=root.AddComponent<BossRig>(); rig.rigRoot=root.transform;
                rig.bones.Add(new BossRig.Binding {boneId="RightWrist",joint=wrist});
                rig.bones.Add(new BossRig.Binding {boneId="RightIndexProximal",joint=finger});
                boss=root.AddComponent<BossController>();boss.enabled=false;boss.previewMode=true;
                boss.timelineName="";boss.idle=new BossController.Recording();boss.loopDuration=2;boss.proceduralFeet=false;
                boss.poseSmoothing=0;boss.blendOut=0;
                typeof(BossController).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boss,null);
            }
            public BossController.Cue Angle(float time,float angle,BossController.TransitionTiming timing,float seconds)
            {
                var cue=new BossController.Cue {time=time,action=BossController.ActionKind.FaceAngle,angle=angle,transitionTiming=timing,transitionSeconds=seconds};
                boss.timeline.Add(cue);return cue;
            }
            public BossController.Cue Animation(float time,BossController.TransitionTiming timing,float seconds,float angle=90)
            {
                var pose=new[] {new PoseJoint{id="RightIndexProximal",localRotation=Quaternion.Euler(angle,0,0)}};
                var clip=new MoveAnimation {frames=new[]{new MoveAnimationFrame{time=0,joints=pose},new MoveAnimationFrame{time=.4f,joints=pose}}};
                var asset=new TextAsset(JsonUtility.ToJson(new AnimationFile {animation=clip}));clips.Add(asset);
                var cue=new BossController.Cue {time=time,action=BossController.ActionKind.PlayAnimation,
                    recording=new BossController.Recording {bundledJson=asset},transitionTiming=timing,transitionSeconds=seconds};
                boss.timeline.Add(cue);return cue;
            }
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach(var clip in clips)UnityEngine.Object.DestroyImmediate(clip);
            }
        }
        [MenuItem("Tools/Beat the Boss/Check timeline transitions and loops")]
        internal static void Run()
        {
            using(var f=new Fixture())
            {
                f.Angle(1.013f,0,BossController.TransitionTiming.FinishAtNode,.4f);f.boss.RebuildTimeline();
                f.boss.Seek(.5f);Require(Mathf.Abs(Mathf.DeltaAngle(0,f.root.transform.eulerAngles.y))<.001f,"Early turn began before its transition interval.");
                f.boss.Seek(.813f);Require(Mathf.Abs(Mathf.DeltaAngle(45,f.root.transform.eulerAngles.y))<.05f,"Early turn is not halfway through its interval.");
                f.boss.Seek(1.013f);Require(Mathf.Abs(Mathf.DeltaAngle(90,f.root.transform.eulerAngles.y))<.001f,"Rotation did not arrive exactly at the fractional node time.");
                f.boss.Seek(.813f);Require(Mathf.Abs(Mathf.DeltaAngle(45,f.root.transform.eulerAngles.y))<.05f,"Reverse seek changed transition state.");
            }
            using(var f=new Fixture())
            {
                f.Angle(1,0,BossController.TransitionTiming.StartAtNode,.4f);f.boss.RebuildTimeline();
                f.boss.Seek(1);Require(Mathf.Abs(Mathf.DeltaAngle(0,f.root.transform.eulerAngles.y))<.001f,"Start-at-node turn started early.");
                f.boss.Seek(1.2f);Require(Mathf.Abs(Mathf.DeltaAngle(45,f.root.transform.eulerAngles.y))<.05f,"Start-at-node turn did not advance smoothly.");
                f.boss.Seek(1.4f);Require(Mathf.Abs(Mathf.DeltaAngle(90,f.root.transform.eulerAngles.y))<.001f,"Start-at-node turn did not finish.");
            }
            using(var f=new Fixture())
            {
                f.boss.loopDuration=1;f.Angle(.2f,0,BossController.TransitionTiming.StartAtNode,.2f);f.boss.RebuildTimeline();
                f.boss.AdvancePreview(.9f);float yaw=f.root.transform.eulerAngles.y;
                float idle=Float(f.boss,"idleClock");f.boss.AdvancePreview(.2f);
                Require(Mathf.Abs(Mathf.DeltaAngle(yaw,f.root.transform.eulerAngles.y))<.001f,"Loop reset facing without a node.");
                Require(Float(f.boss,"idleClock")>idle,"Loop reset idle phase.");
                Require(f.boss.TimePosition>.09f&&f.boss.TimePosition<.11f,"Loop did not wrap its clock.");
            }
            using(var f=new Fixture())
            {
                f.boss.loopDuration=1;f.Angle(0,0,BossController.TransitionTiming.FinishAtNode,.3f);
                f.Angle(.4f,180,BossController.TransitionTiming.StartAtNode,.1f);f.boss.RebuildTimeline();
                f.boss.Seek(.99f);float yaw=f.root.transform.eulerAngles.y;f.boss.AdvancePreview(.02f);
                Require(Mathf.Abs(Mathf.DeltaAngle(yaw,f.root.transform.eulerAngles.y))<1,"Finish-at-zero turn snapped across loop.");
                Require(Mathf.Abs(Mathf.DeltaAngle(90,f.root.transform.eulerAngles.y))<.001f,"Wrapped rotation did not reach its node.");
            }
            using(var f=new Fixture())
            {
                f.Animation(1,BossController.TransitionTiming.FinishAtNode,.4f);f.boss.RebuildTimeline();
                f.boss.Seek(.8f);Require(Mathf.Abs(Quaternion.Angle(Quaternion.identity,f.finger.localRotation)-45)<.1f,"Animation did not blend in before node.");
                f.boss.Seek(1);Require(Quaternion.Angle(Quaternion.Euler(90,0,0),f.finger.localRotation)<.001f,"Animation was not at its first frame on node.");
            }
            using(var f=new Fixture())
            {
                f.Animation(1,BossController.TransitionTiming.StartAtNode,.2f);f.boss.RebuildTimeline();
                f.boss.Seek(1);Require(Quaternion.Angle(Quaternion.identity,f.finger.localRotation)<.001f,"Animation blended before start-at-node.");
                f.boss.Seek(1.1f);Require(Mathf.Abs(Quaternion.Angle(Quaternion.identity,f.finger.localRotation)-45)<.1f,"Start-at-node animation blend incorrect.");
            }
            using(var f=new Fixture())
            {
                f.boss.loopDuration=1;f.Animation(0,BossController.TransitionTiming.FinishAtNode,.3f);f.boss.RebuildTimeline();
                f.boss.Seek(.99f);Quaternion pose=f.finger.localRotation;f.boss.AdvancePreview(.02f);
                Require(Quaternion.Angle(pose,f.finger.localRotation)<1,"Animation snapped at wrapped finish-at-zero node.");
            }
            using(var f=new Fixture())
            {
                f.Animation(0,BossController.TransitionTiming.FinishAtNode,.1f);
                f.Animation(.2f,BossController.TransitionTiming.StartAtNode,.2f,-90);f.boss.RebuildTimeline();
                f.boss.Seek(.2f);Require(Quaternion.Angle(Quaternion.Euler(90,0,0),f.finger.localRotation)<.001f,"Starting another animation discarded the outgoing pose.");
                f.boss.Seek(.3f);Require(Quaternion.Angle(Quaternion.identity,f.finger.localRotation)<.1f,"Interrupted animations did not crossfade.");
            }
            using(var f=new Fixture())
            {
                var cue=f.Animation(1,BossController.TransitionTiming.FinishAtNode,.4f);cue.action=BossController.ActionKind.SetIdle;
                f.boss.RebuildTimeline();f.boss.Seek(.8f);
                Require(Mathf.Abs(Quaternion.Angle(Quaternion.identity,f.finger.localRotation)-45)<.1f,"Change idle did not blend ahead of its node.");
                f.boss.Seek(1);Require(Quaternion.Angle(Quaternion.Euler(90,0,0),f.finger.localRotation)<.001f,"Change idle did not reach first frame on its node.");
            }
            using(var f=new Fixture())
            {
                f.boss.loopDuration=1;f.boss.timeline.Add(new BossController.Cue {time=.4f,action=BossController.ActionKind.PauseIdle});
                f.boss.RebuildTimeline();f.boss.AdvancePreview(1.2f);
                Require(Mathf.Abs(Float(f.boss,"idleClock")-.4f)<.001f,"Loop reset the paused idle state.");
            }
            using(var f=new Fixture())
            {
                f.boss.previewMode=false;f.boss.loopDuration=1;
                int starts=0,middles=0;
                var start=new BossController.Cue {time=0,action=BossController.ActionKind.StopFollowing};
                var middle=new BossController.Cue {time=.5f,action=BossController.ActionKind.PauseIdle};
                start.onStarted.AddListener(()=>starts++);middle.onStarted.AddListener(()=>middles++);
                f.boss.timeline.Add(start);f.boss.timeline.Add(middle);f.boss.RebuildTimeline();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(BossController).GetMethod("FireInitialEvents",flags).Invoke(f.boss,null);
                typeof(BossController).GetMethod("AdvanceTimeline",flags).Invoke(f.boss,new object[]{2f,true});
                Require(starts==3&&middles==2,$"Gameplay nodes did not fire exactly once per traversal: starts={starts}, middles={middles}, clock={f.boss.TimePosition:R}.");
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/BossTimelineTransitionChecks.txt","PASS: exact fractional rotation deadlines; start/finish animation blending; continuous facing and idle phase; clock wrapping; transitions across zero; deterministic reverse seeking; interrupted clip crossfades; idle changes and pause preservation; gameplay event counts.");
            Debug.Log("Timeline transition and loop checks passed.");
        }
        private static float Float(BossController boss,string name) => (float)typeof(BossController).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(boss);
        private static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
    }
}
