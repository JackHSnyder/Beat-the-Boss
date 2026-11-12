using BeatTheBoss.Poses;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PoseWorkbench))]
internal sealed class PoseWorkbenchEditor : Editor
{
    private string[] poses;
    private int selected;
    private SavedPose lastLoaded;

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var workbench = (PoseWorkbench)target;
        if (!Application.isPlaying || !workbench.isActiveAndEnabled)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to capture, load, and test poses.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        workbench.SetModelTest(EditorGUILayout.Toggle("Model test input", workbench.ModelTest));
        if (workbench.ModelTest)
        {
            float bend = EditorGUILayout.Slider("Test left elbow", workbench.TestBend, -90, 90);
            if (!Mathf.Approximately(bend, workbench.TestBend))
                workbench.SetTestBend(bend);
        }

        TrackedPose frame = workbench.CurrentFrame;
        EditorGUILayout.LabelField("Tracking", $"{frame.Mode} · {frame.Bones.Count} joints");
        workbench.CaptureName = EditorGUILayout.TextField("Pose name", workbench.CaptureName);
        workbench.CaptureDelay = Mathf.Max(0, EditorGUILayout.FloatField("Capture delay (s)", workbench.CaptureDelay));
        if (GUILayout.Button("Capture and save"))
            workbench.QueueCapture();
        if (workbench.CaptureIn > 0)
            EditorGUILayout.LabelField($"Capturing in {workbench.CaptureIn:F1}s");

        EditorGUILayout.Space();
        bool refresh = GUILayout.Button("Refresh saved poses");
        if (poses == null || refresh || lastLoaded != workbench.LoadedPose)
        {
            lastLoaded = workbench.LoadedPose;
            poses = workbench.ListPoses();
            int loadedIndex = System.Array.IndexOf(poses, lastLoaded?.name);
            selected = loadedIndex >= 0 ? loadedIndex : Mathf.Clamp(selected, 0, Mathf.Max(0, poses.Length - 1));
        }
        if (poses.Length > 0)
        {
            selected = EditorGUILayout.Popup("Saved pose", selected, poses);
            if (GUILayout.Button("Load selected"))
                workbench.Load(poses[selected]);
        }

        if (workbench.LoadedPose != null)
        {
            EditorGUILayout.LabelField("Loaded pose", workbench.LoadedPose.name);
            PoseMatcher.Result result = workbench.Match;
            string status = result.Missing > 0 ? "Tracking missing" :
                result.Compared == 0 && result.ConstraintCount == 0 ? "No checks selected" :
                result.IsMatch ? "Match" : "Pose does not match";
            EditorGUILayout.HelpBox($"{status}\n{result.Compared} compared · {result.Missing} missing · " +
                $"{result.OutsideTolerance} over individual limits", result.IsMatch ? MessageType.Info : MessageType.Warning);
            if (result.FirstFailedCategory is PoseCategory failed)
                EditorGUILayout.LabelField("Failed category", PoseMatchProfile.Label(failed));
            if (result.ConstraintCount > 0)
                EditorGUILayout.LabelField("Constraints", $"{result.PassedConstraints}/{result.ConstraintCount} passing");
            if (result.WorstJoint != null)
                EditorGUILayout.LabelField($"Largest error: {result.WorstError:F1}°", result.WorstJoint);
            if (result.FirstMissingJoint != null)
                EditorGUILayout.LabelField("First missing", result.FirstMissingJoint);

            if (workbench.Preview == null)
            {
                if (GUILayout.Button("Show avatar preview"))
                    workbench.ShowPreview();
            }
            else
            {
                if (GUILayout.Button("Frame preview in Scene view"))
                    SceneView.lastActiveSceneView?.Frame(new Bounds(workbench.Preview.transform.position + Vector3.up, Vector3.one * 2), false);
                if (GUILayout.Button("Hide avatar preview"))
                    workbench.HidePreview();
            }
        }

        EditorGUILayout.HelpBox(workbench.Status, MessageType.None);
        if (GUILayout.Button("Open pose folder"))
            EditorUtility.RevealInFinder(workbench.PoseDirectory);
    }
}
