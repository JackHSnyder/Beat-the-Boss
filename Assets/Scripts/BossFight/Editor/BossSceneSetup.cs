using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal static class BossSceneSetup
    {
        [MenuItem("Tools/Beat the Boss/Set up scene character")]
        private static void Setup()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/BossScene.unity")
            { Debug.LogWarning("Open BossScene outside Play mode first."); return; }
            BossController boss = Object.FindObjectsByType<BossController>().FirstOrDefault(b => b.gameObject.scene == scene);
            if (boss == null) { Debug.LogError("Boss controller missing from BossScene."); return; }
            BossRig rig = boss.GetComponent<BossRig>();
            Undo.RecordObject(rig, "Configure boss rig");
            Undo.RecordObject(boss, "Configure boss character");
            if (rig.rigRoot == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.movement/Shared/Prefabs/Character/StylizedCharacter.prefab");
                if (prefab == null) { Debug.LogError("Stylized character prefab not found."); return; }
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, boss.transform);
                Undo.RegisterCreatedObjectUndo(visual, "Add boss character");
                visual.name = "Stylized Character";
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one;
                rig.rigRoot = visual.transform;
            }
            // The tracking prefab contains other pose drivers. Keep its rendered rig, but let only the boss animate it.
            foreach (Behaviour driver in rig.rigRoot.GetComponentsInChildren<Behaviour>(true))
            {
                Undo.RecordObject(driver, "Disable competing character driver");
                driver.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(driver);
            }
            foreach (Renderer renderer in rig.rigRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            rig.MapStylized();
            boss.characterPrefab = null; // The saved scene owns the visual; do not spawn a duplicate.
            boss.arena = Object.FindObjectsByType<MoveCircle>().FirstOrDefault(c => c.gameObject.scene == scene);
            var camera = Object.FindObjectsByType<OVRCameraRig>().FirstOrDefault(c => c.gameObject.scene == scene);
            if (camera != null) boss.player = camera.centerEyeAnchor;
            foreach (var cue in boss.timeline)
                if (cue.action == BossController.ActionKind.PlayAnimation && cue.recording.savedName == "Pew Peck")
                    cue.recording.savedName = "PEW PECK";
            EditorUtility.SetDirty(rig); EditorUtility.SetDirty(boss);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = rig.rigRoot.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("Boss scene character saved with " + rig.bones.Count + " mapped bones.", boss);
        }
    }
}
