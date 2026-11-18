using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    internal static class BossEditorSceneSetup
    {
        private const string EditorPath = "Assets/Scenes/BossEditorScene.unity";
        [MenuItem("Tools/Beat the Boss/Create boss timeline editor scene")]
        private static void Setup()
        {
            if (Application.isPlaying) return;
            Scene original = SceneManager.GetActiveScene();
            Scene source = SceneManager.GetSceneByPath("Assets/Scenes/BossScene.unity");
            bool sourceOpened = !source.isLoaded;
            if (sourceOpened) source = EditorSceneManager.OpenScene("Assets/Scenes/BossScene.unity", OpenSceneMode.Additive);
            BossController sourceBoss = Components<BossController>(source).Single();
            TextAsset pew = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Animations/Boss/PEW PECK.json");
            if (pew != null && !sourceBoss.bundledAnimations.Any(a => a.name == "PEW PECK"))
                sourceBoss.bundledAnimations = sourceBoss.bundledAnimations.Concat(new[] { new BossController.BundledAnimation { name = "PEW PECK", json = pew } }).ToArray();
            sourceBoss.timelineName = "Boss Timeline";
            EditorUtility.SetDirty(sourceBoss); EditorSceneManager.SaveScene(source);
            if (File.Exists(EditorPath))
            {
                Scene existing = SceneManager.GetSceneByPath(EditorPath); bool opened = !existing.isLoaded;
                if (opened) existing = EditorSceneManager.OpenScene(EditorPath, OpenSceneMode.Additive);
                ConfigureViewingPosition(existing); EditorSceneManager.SaveScene(existing);
                if (opened) EditorSceneManager.CloseScene(existing,true);
                if (sourceOpened) EditorSceneManager.CloseScene(source,true);
                SceneManager.SetActiveScene(original);
                File.WriteAllText("Temp/BossEditorSetupResult.txt", "Updated unobstructed editor viewing positions."); return;
            }
            EditorSceneManager.SaveScene(source, EditorPath, true);
            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.OpenScene(EditorPath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var boss = Components<BossController>(scene).Single(); boss.playOnStart = false;
            foreach (MoveDetection component in Components<MoveDetection>(scene)) component.enabled = false;
            foreach (AttackLaser component in Components<AttackLaser>(scene)) component.enabled = false;
            foreach (EncounterPlayerDamage component in Components<EncounterPlayerDamage>(scene))
                component.gameObject.SetActive(false);
            foreach (FiringAimTracker component in Components<FiringAimTracker>(scene)) component.enabled = false;
            foreach (FiringTarget component in Components<FiringTarget>(scene))
            { component.enabled = false; if (!component.transform.IsChildOf(boss.transform)) component.gameObject.SetActive(false); }
            foreach (MoveCircle component in Components<MoveCircle>(scene)) component.enabled = false;
            foreach (PlayerAvatarDriver component in Components<PlayerAvatarDriver>(scene)) component.enabled = false;
            var headset = Components<OVRCameraRig>(scene).Single();
            Transform observer = headset.transform.root;
            observer.SetPositionAndRotation(new Vector3(0, 5, -11), Quaternion.identity);
            foreach (var retargeter in observer.GetComponentsInChildren<Meta.XR.Movement.Retargeting.CharacterRetargeter>(true))
            {
                retargeter.enabled = false;
                foreach (Renderer renderer in retargeter.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            }
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "Timeline viewing platform";
            platform.transform.position = new Vector3(0,4.85f,-11); platform.transform.localScale = new Vector3(5,.3f,4);
            var dummy = new GameObject("Dummy Player");
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = "Dummy body"; body.transform.SetParent(dummy.transform,false);
            body.transform.localPosition = new Vector3(0,1,0); body.transform.localScale = new Vector3(.65f,1,.65f);
            var facing = GameObject.CreatePrimitive(PrimitiveType.Cube); facing.name = "Facing indicator"; facing.transform.SetParent(dummy.transform,false);
            facing.transform.localPosition = new Vector3(0,1.55f,.37f); facing.transform.localScale = new Vector3(.18f,.12f,.2f);
            dummy.transform.position = boss.arena.GetWorldPoint(boss.dummyStartAngle);
            dummy.transform.LookAt(new Vector3(boss.transform.position.x,dummy.transform.position.y,boss.transform.position.z));
            var cameraObject = new GameObject("Desktop arena camera",typeof(Camera),typeof(AudioListener));
            var camera = cameraObject.GetComponent<Camera>(); camera.fieldOfView = 50; camera.rect = new Rect(.5f,0,.5f,1);
            cameraObject.transform.position = new Vector3(10,10,-17); cameraObject.transform.LookAt(new Vector3(0,0,0));
            var root = new GameObject("Boss Timeline Editor");
            var environment = root.AddComponent<BossEditorEnvironment>(); environment.boss = boss; environment.observer = observer;
            environment.headset = headset; environment.dummy = dummy.transform; environment.desktopCamera = camera;
            boss.player = dummy.transform;
            var workbench = root.AddComponent<BossTimelineWorkbench>(); workbench.boss = boss;
            var panel = root.AddComponent<BossTimelinePanel>(); panel.workbench = workbench; panel.headsetCamera = headset.centerEyeAnchor.GetComponent<Camera>();
            Scene recording = SceneManager.GetSceneByPath("Assets/Scenes/RecordingScene.unity"); bool openedRecording = !recording.isLoaded;
            if (openedRecording) recording = EditorSceneManager.OpenScene("Assets/Scenes/RecordingScene.unity", OpenSceneMode.Additive);
            var sourcePanel = new SerializedObject(Components<PoseGamePanel>(recording).Single());
            panel.backplatePrefab = (GameObject)sourcePanel.FindProperty("backplatePrefab").objectReferenceValue;
            panel.primaryButtonPrefab = (Button)sourcePanel.FindProperty("primaryButtonPrefab").objectReferenceValue;
            panel.secondaryButtonPrefab = (Button)sourcePanel.FindProperty("secondaryButtonPrefab").objectReferenceValue;
            panel.nameFieldPrefab = (GameObject)sourcePanel.FindProperty("nameFieldPrefab").objectReferenceValue;
            panel.sliderPrefab = (Slider)sourcePanel.FindProperty("sliderPrefab").objectReferenceValue;
            if (openedRecording) EditorSceneManager.CloseScene(recording,true);
            ConfigureViewingPosition(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var build = EditorBuildSettings.scenes.ToList(); if (!build.Any(s => s.path == EditorPath)) build.Add(new EditorBuildSettingsScene(EditorPath,true));
            EditorBuildSettings.scenes = build.ToArray();
            EditorSceneManager.CloseScene(scene,true);
            if (sourceOpened) EditorSceneManager.CloseScene(source,true);
            SceneManager.SetActiveScene(original);
            File.WriteAllText("Temp/BossEditorSetupResult.txt", "Created BossEditorScene with mapped boss, dummy, viewing platform, desktop camera, Meta UI input, and build entry.");
            Debug.Log("Boss timeline editor ready: " + EditorPath);
        }
        private static void ConfigureViewingPosition(Scene scene)
        {
            var environment = Components<BossEditorEnvironment>(scene).Single();
            environment.desktopCamera.transform.position = new Vector3(0, 10, -6);
            environment.desktopCamera.transform.LookAt(new Vector3(0, 0, 2));
            environment.desktopCamera.fieldOfView = 65;
            environment.observer.position = new Vector3(0,4,-6);
            var platform = scene.GetRootGameObjects().First(g => g.name == "Timeline viewing platform");
            platform.transform.position = new Vector3(0,3.85f,-6);
            environment.boss.dummyStartAngle = 90;
            environment.dummy.position = environment.boss.arena.GetWorldPoint(90);
            environment.dummy.LookAt(new Vector3(environment.boss.transform.position.x,environment.dummy.position.y,environment.boss.transform.position.z));
            EditorSceneManager.MarkSceneDirty(scene);
        }
        private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
    }
}

