using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [CustomEditor(typeof(BossIdleRecorder))]
    internal sealed class BossIdleRecorderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var recorder = (BossIdleRecorder)target;
            EditorGUILayout.HelpBox(recorder.Status, MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (!recorder.IsRecording) { if (GUILayout.Button("Record idle after countdown")) recorder.Record(); }
                else
                {
                    if (GUILayout.Button("Stop and save")) recorder.Save();
                    if (GUILayout.Button("Cancel")) recorder.Cancel();
                    Repaint();
                }
            }
        }
    }
    [CustomEditor(typeof(BossRig))]
    internal sealed class BossRigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Map stylized character bones"))
            {
                Undo.RecordObject(target, "Map boss bones"); ((BossRig)target).MapStylized(); EditorUtility.SetDirty(target);
            }
            EditorGUILayout.HelpBox("For another rig, assign its neutral-pose transforms to universal bone IDs. Disable other animation drivers on the rig. The scene prototype already has its character and bone mapping saved in BossScene.", MessageType.Info);
        }
    }
}
