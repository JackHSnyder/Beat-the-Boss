using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal static class BossTimelineChecks
    {
        [MenuItem("Tools/Beat the Boss/Check timeline playback")]
        internal static void Run()
        {
            GameObject root = new GameObject("Timeline verification (temporary)");
            TextAsset json = null;
            try
            {
                Transform wrist = new GameObject("Wrist").transform; wrist.SetParent(root.transform,false);
                Transform finger = new GameObject("Finger").transform; finger.SetParent(wrist,false);
                var rig = root.AddComponent<BossRig>(); rig.rigRoot = root.transform;
                rig.bones.Add(new BossRig.Binding { boneId = "RightWrist", joint = wrist });
                rig.bones.Add(new BossRig.Binding { boneId = "RightIndexProximal", joint = finger });
                var boss = root.AddComponent<BossController>(); boss.enabled = false; boss.previewMode = true;
                boss.idle = new BossController.Recording(); boss.timelineName = ""; boss.loopDuration = 2;
                var dummy = new GameObject("Test dummy"); dummy.transform.SetParent(root.transform,false); boss.player = dummy.transform;
                var circleObject = new GameObject("Test circle", typeof(LineRenderer)); circleObject.transform.SetParent(root.transform,false);
                boss.arena = circleObject.AddComponent<MoveCircle>(); boss.arena.enabled = false;
                json = new TextAsset("{\"version\":1,\"name\":\"test\",\"animation\":{\"playbackStart\":0.2,\"playbackEnd\":0.8,\"excludedBones\":[\"RightWrist\"],\"frames\":[{\"time\":0,\"joints\":[{\"id\":\"RightWrist\",\"localRotation\":{\"x\":0,\"y\":0.7071068,\"z\":0,\"w\":0.7071068}},{\"id\":\"RightIndexProximal\",\"localRotation\":{\"x\":0.3826834,\"y\":0,\"z\":0,\"w\":0.9238795}}]},{\"time\":1,\"joints\":[{\"id\":\"RightWrist\",\"localRotation\":{\"x\":0,\"y\":0.7071068,\"z\":0,\"w\":0.7071068}},{\"id\":\"RightIndexProximal\",\"localRotation\":{\"x\":0.7071068,\"y\":0,\"z\":0,\"w\":0.7071068}}]}]}}");
                var clip = new BossController.Cue { time = .1f, action = BossController.ActionKind.PlayAnimation, recording = new BossController.Recording { bundledJson = json } };
                int effects = 0; clip.onStarted.AddListener(() => effects++);
                boss.timeline.Add(clip);
                boss.timeline.Add(new BossController.Cue { time = .6f, action = BossController.ActionKind.DummyAngle, angle = 90 });
                boss.timeline.Add(new BossController.Cue { time = 0, action = BossController.ActionKind.FaceAngle, angle = 0 });
                typeof(BossController).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boss,null);
                boss.RebuildTimeline();
                Require(Mathf.Abs(boss.ClipDuration(clip) - .6f) < .0001f,"Clip width must use the trimmed length.");
                boss.Seek(.4f); Quaternion rotation = finger.localRotation, facing = root.transform.rotation;
                Require(Quaternion.Angle(wrist.localRotation,Quaternion.identity) < .001f,"Excluded wrist must stay on base layer.");
                Require(Quaternion.Angle(rotation,Quaternion.identity) > 10,"Enabled finger must animate.");
                boss.Seek(1); Vector3 destination = boss.arena.GetWorldPoint(90);
                Require(Vector3.Distance(dummy.transform.position,destination) < .001f,"Dummy point events must follow seek.");
                boss.Seek(.4f);
                Require(Quaternion.Angle(rotation,finger.localRotation) < .001f && Quaternion.Angle(facing,root.transform.rotation) < .001f,"Reverse seek must exactly reconstruct pose and facing.");
                Require(effects == 0,"Seeking must not fire gameplay effects.");
                clip.speed = 2; boss.RebuildTimeline(); Require(Mathf.Abs(boss.ClipDuration(clip)-.3f)<.0001f,"Speed must scale clip duration.");
                File.WriteAllText("Temp/BossTimelineChecks.txt", "PASS: standalone animation JSON, trimmed duration, speed duration, bone exclusion, finger playback, reverse seek pose/facing, dummy position, suppressed gameplay events.");
                Debug.Log("Boss timeline playback checks passed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); if (json != null) UnityEngine.Object.DestroyImmediate(json); }
        }
        private static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    }
}
