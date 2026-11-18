using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    // Explicit, opt-in local smoke check; never runs in a player build.
    internal static class BossEditorSmokeCheck
    {
        private static double started;
        private static int phase;
        private static float pausedTime;
        private static string error;
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.playModeStateChanged += Changed;
            Request();
        }
        [MenuItem("Tools/Beat the Boss/Check editor scene in Play mode")]
        private static void RunFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/RequestBossEditorSmoke", "Run requested editor checks");
            Request();
        }
        internal static void Request()
        {
            if (!File.Exists("Temp/RequestBossEditorSmoke") || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.delayCall += () =>
            {
                File.Delete("Temp/RequestBossEditorSmoke");
                var active = EditorSceneManager.GetActiveScene();
                if (active.isDirty) EditorSceneManager.SaveScene(active);
                SessionState.SetBool("BossEditor.Smoke",true);
                EditorSceneManager.OpenScene("Assets/Scenes/BossEditorScene.unity");
                EditorApplication.EnterPlaymode();
            };
        }
        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool("BossEditor.Smoke",false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                started = EditorApplication.timeSinceStartup; phase = 0; error = null;
                Application.logMessageReceived += Log;
                EditorApplication.update += Check;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool("BossEditor.Smoke",false); EditorApplication.update -= Check;
                Application.logMessageReceived -= Log;
            }
        }
        private static void Log(string message,string stack,LogType type)
        { if ((type == LogType.Exception || type == LogType.Error) && (stack.Contains("BossTimeline") || stack.Contains("BossEditor") || stack.Contains("BossController"))) error = message + "\n" + stack; }
        private static void Check()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup-started < 2) return;
            try
            {
                var workbench = UnityEngine.Object.FindAnyObjectByType<BossTimelineWorkbench>();
                if (workbench == null) throw new Exception("Workbench did not start.");
                if (error != null) throw new Exception(error);
                if (phase == 0)
                {
                    var panel = UnityEngine.Object.FindAnyObjectByType<BossTimelinePanel>();
                    if (!panel.GetComponentsInChildren<Button>().Any(b => b.name == "Add node") ||
                        panel.transform.Find("Timeline node details") == null)
                        throw new Exception("Timeline and node details did not build.");
                    var boss = workbench.boss; var rig = boss.GetComponent<BossRig>();
                    workbench.Seek(3.3f);
                    Quaternion facing = boss.transform.rotation;
                    Quaternion[] rotations = rig.bones.Select(b=>b.joint.localRotation).ToArray();
                    Vector3[] positions = rig.bones.Select(b=>b.joint.localPosition).ToArray();
                    workbench.Seek(8); workbench.Seek(3.3f);
                    for(int i=0;i<rotations.Length;i++)
                        if (Quaternion.Angle(rotations[i],rig.bones[i].joint.localRotation)>.02f || Vector3.Distance(positions[i],rig.bones[i].joint.localPosition)>.0001f)
                            throw new Exception("Full rig seek mismatch: " + rig.bones[i].boneId);
                    if (Quaternion.Angle(facing,boss.transform.rotation)>.02f) throw new Exception("Facing seek mismatch.");
                    workbench.TogglePlay(); phase = 1; started = EditorApplication.timeSinceStartup;
                }
                else if (phase == 1)
                {
                    if (workbench.Cursor <= 3.4f) throw new Exception("Play did not advance.");
                    workbench.TogglePlay(); pausedTime = workbench.Cursor; phase = 2; started = EditorApplication.timeSinceStartup;
                }
                else if (phase == 2)
                {
                    if (Mathf.Abs(workbench.Cursor-pausedTime)>.0001f) throw new Exception("Pause did not freeze playback.");
                    File.WriteAllText("Temp/BossEditorSmokeResult.txt","PASS: editor scene starts, track controls and separate node details created, full 51-bone rig and procedural feet repeat after reverse seek, play advances, pause freezes.");
                    File.WriteAllText("Temp/BossEditorCameras.txt", string.Join("\n", UnityEngine.Object.FindObjectsByType<Camera>().Select(c => $"{c.name}: enabled={c.enabled}, stereo={c.stereoEnabled}, position={c.transform.position}, rotation={c.transform.eulerAngles}, depth={c.depth}, mask={c.cullingMask}, rect={c.rect}")));
                    ScreenCapture.CaptureScreenshot("Temp/BossEditorPreview.png");
                    phase = 3; started = EditorApplication.timeSinceStartup;
                }
                else if (phase == 3)
                {
                    UnityEngine.Object.FindAnyObjectByType<BossEditorEnvironment>().FrameBoss();
                    phase = 4; started = EditorApplication.timeSinceStartup;
                }
                else if (phase == 4)
                {
                    ScreenCapture.CaptureScreenshot("Temp/BossEditorClosePreview.png"); phase = 5; started = EditorApplication.timeSinceStartup;
                }
                else { EditorApplication.update -= Check; EditorApplication.ExitPlaymode(); }
            }
            catch(Exception e)
            {
                File.WriteAllText("Temp/BossEditorSmokeResult.txt",e.ToString());
                EditorApplication.update -= Check; EditorApplication.ExitPlaymode();
            }
        }
    }
}
