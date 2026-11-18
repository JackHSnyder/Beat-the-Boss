// Builds a pose library and a focused editor for the loaded pose, matching rules, and constraints.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace BeatTheBoss.Poses
{
    internal sealed class PosePanel
    {
        private const int PageSize = 6;
        private readonly PosePanelLayout layout;
        private readonly RectTransform root, matchingPage, constraintsPage;
        private readonly PoseWorkbench poses;
        private readonly Action<string, string, Action<string>> editName;
        private readonly Action framePreview;
        private readonly Action<Action> guard;
        private readonly Action<string,string,string,Action> confirm;
        private readonly PoseMatchPanel matching;
        private readonly PoseConstraintPanel constraints;
        private readonly TMP_Text matchingPose, constraintsPose;
        private RectTransform content;
        private TMP_Text captureName, captureDelay, preview, match, matchDetails, heading;
        private UnityEngine.UI.Button capture, previewButton, frameButton, save;
        private SavedPose lastPose;
        private string lastLegacy;
        private string[] files = Array.Empty<string>();
        private string view = "library";
        private int page;

        public PosePanel(PosePanelLayout layout, RectTransform root, PoseWorkbench poses,
            Action<string, string, Action<string>> editName, Action framePreview, Action<Action> guard, Action<string,string,string,Action> confirm)
        {
            this.layout = layout; this.root = root; this.poses = poses;
            this.editName = editName; this.framePreview = framePreview; this.guard = guard; this.confirm = confirm;
            matchingPage = layout.Rect(root, "Pose matching", 0, 0, 632, 490);
            constraintsPage = layout.Rect(root, "Pose constraints", 0, 0, 632, 490);
            matchingPose = SubpageHeader(matchingPage, "Matching");
            constraintsPose = SubpageHeader(constraintsPage, "Constraints");
            matching = new PoseMatchPanel(poses, layout, layout.Rect(matchingPage, "Matching properties", 0, 54, 632, 436));
            constraints = new PoseConstraintPanel(poses, layout, layout.Rect(constraintsPage, "Constraint properties", 0, 54, 632, 436));
            lastPose = poses.LoadedPose; lastLegacy = poses.LegacyPoseName;
            if (lastPose != null) view = "pose";
            Draw();
        }
        private TMP_Text SubpageHeader(RectTransform pageRoot, string section)
        {
            layout.Button(pageRoot, "Back to pose", 0, 0, 140, 40, () => Show("pose"));
            return layout.Label(pageRoot, section, 154, 0, 478, 40, 21, true);
        }
        public void EnterPage() { SynchronizeSelection(); Draw(); }
        public void Refresh()
        {
            if (SynchronizeSelection()) Draw();
            bool dirty = poses.IsDirty;
            if (view == "matching" || view == "constraints")
            {
                matchingPose.text = "Matching · " + poses.LoadedPose?.name + (dirty ? " *" : "");
                constraintsPose.text = "Constraints · " + poses.LoadedPose?.name + (dirty ? " *" : "");
                if (view == "matching") matching.Refresh(); else constraints.Refresh();
            }
            if (view == "capture" || view == "legacy")
            {
                captureName.text = view == "legacy" ? poses.LegacyPoseName : string.IsNullOrWhiteSpace(poses.CaptureName) ? "Enter pose name" : poses.CaptureName + " · Edit";
                captureDelay.text = $"{poses.CaptureDelay:0.#} s";
                capture.interactable = !poses.CapturePending && !string.IsNullOrWhiteSpace(view == "legacy" ? poses.LegacyPoseName : poses.CaptureName);
            }
            if (view != "pose" || poses.LoadedPose == null) return;
            heading.text = poses.LoadedPose.name + (dirty ? " *" : "");
            save.interactable = dirty;
            preview.text = poses.Preview == null ? "Show avatar preview" : "Hide avatar preview";
            previewButton.interactable = poses.LoadedPose != null;
            frameButton.interactable = poses.Preview != null && poses.Preview.activeInHierarchy;
            var result = poses.Match;
            string state = result.Missing > 0 ? "Tracking missing" :
                result.Compared == 0 && result.ConstraintCount == 0 ? "No checks selected" :
                result.IsMatch ? "Match" : result.PassedConstraints < result.ConstraintCount ? "Constraint failed" : "Outside tolerance";
            match.text = state;
            match.color = result.IsMatch ? PosePanelLayout.Accent : PosePanelLayout.Warning;
            matchDetails.text = $"{result.Compared} bones compared · {result.Missing} missing\n" +
                $"{result.OutsideTolerance} over individual limits\n" +
                $"Constraints: {result.PassedConstraints}/{result.ConstraintCount} passing";
        }
        private bool SynchronizeSelection()
        {
            if (lastPose == poses.LoadedPose && lastLegacy == poses.LegacyPoseName) return false;
            lastPose = poses.LoadedPose; lastLegacy = poses.LegacyPoseName;
            view = lastLegacy != null ? "legacy" : lastPose != null ? "pose" : "library";
            return true;
        }
        private void Show(string next) { view = next; Draw(); }
        private void Draw()
        {
            if (content != null) { content.gameObject.SetActive(false); UnityEngine.Object.Destroy(content.gameObject); }
            content = layout.Rect(root, "Pose editor controls", 0, 0, 632, 490);
            matchingPage.gameObject.SetActive(view == "matching");
            constraintsPage.gameObject.SetActive(view == "constraints");
            content.gameObject.SetActive(view != "matching" && view != "constraints");
            switch (view)
            {
                case "matching": case "constraints": break;
                case "files": DrawFiles(); break;
                case "capture": case "legacy": DrawCapture(); break;
                case "pose": DrawPose(); break;
                default: DrawLibrary(); break;
            }
            Refresh();
        }
        private void NewPose()
        {
            guard(() => { poses.BeginNewPose(); SynchronizeSelection(); Show("capture"); });
        }
        private void OpenSaved()
        {
            files = poses.ListPoses(); page = 0; Show("files");
        }
        private void DrawLibrary()
        {
            layout.Label(content, "Poses", 0, 0, 390, 40, 23, true);
            Button("New pose", 408, 0, 224, NewPose);
            Button("Open saved pose", 0, 54, 632, OpenSaved);
            layout.Label(content, "Capture a pose, then edit its bone matching and constraints.", 0, 118, 632, 76, 19);
        }
        private void DrawPose()
        {
            if (poses.LoadedPose == null) { view = "library"; DrawLibrary(); return; }
            Button("New", 0, 0, 78, NewPose);
            Button("Open", 86, 0, 78, OpenSaved);
            heading = Button(poses.LoadedPose.name, 172, 0, 340, () => editName("Pose name", poses.LoadedPose.name, value =>
                { poses.RenameDraft(value); Refresh(); }));
            save = Button("Save", 520, 0, 112, () => { poses.SavePose(); Draw(); }, true).GetComponentInParent<UnityEngine.UI.Button>(true);
            Button("Bone matching  ›", 0, 64, 310, () => Show("matching"));
            Button("Constraints  ›", 322, 64, 310, () => Show("constraints"));
            preview = Button("Show avatar preview", 0, 118, 310, () =>
            {
                if (poses.Preview == null) poses.ShowPreview(); else poses.HidePreview();
                Refresh();
            });
            previewButton = preview.GetComponentInParent<UnityEngine.UI.Button>(true);
            frameButton = Button("Frame preview", 322, 118, 310, () => framePreview()).GetComponentInParent<UnityEngine.UI.Button>(true);
            layout.Fill(layout.Rect(content, "Pose match feedback", 0, 182, 632, 180), PosePanelLayout.Surface);
            match = layout.Label(content, "", 16, 194, 600, 38, 23, true);
            matchDetails = layout.Label(content, "", 16, 240, 600, 112, 18);
            matchDetails.alignment = TextAlignmentOptions.TopLeft;
            if (poses.LoadedName != null)
                Button("Delete saved pose", 0, 384, 240, () => Delete(poses.LoadedName));
            layout.Label(content, "Preview and edit this pose, then Save to keep your changes.", 0, 438, 632, 46, 18);
        }
        private void DrawCapture()
        {
            bool legacy = view == "legacy";
            Button("Back", 0, 0, 82, () => guard(() => { SynchronizeSelection(); Show(poses.LoadedPose != null ? "pose" : "library"); }));
            layout.Label(content, legacy ? "Update old pose" : "Capture pose", 100, 0, 532, 40, 23, true);
            layout.Label(content, "Pose name", 0, 58, 140, 44);
            captureName = Button("", 150, 58, 482, () => editName("Pose name", poses.CaptureName, value => { poses.CaptureName = value; Refresh(); }));
            captureName.GetComponentInParent<UnityEngine.UI.Button>(true).interactable = !legacy;
            layout.Label(content, "Capture delay", 0, 116, 144, 44);
            Button("-", 150, 116, 48, () => { poses.CaptureDelay = Mathf.Max(0, poses.CaptureDelay-1); Refresh(); });
            captureDelay = layout.Label(content, "", 204, 116, 86, 44);
            captureDelay.alignment = TextAlignmentOptions.Midline;
            Button("+", 296, 116, 48, () => { poses.CaptureDelay++; Refresh(); });
            capture = Button(legacy ? "Recapture old pose" : "Capture pose", 356, 116, 276,
                () => { if (legacy) poses.RecaptureLegacyPose(); else poses.QueueCapture(); Refresh(); }, true).GetComponentInParent<UnityEngine.UI.Button>(true);
            layout.Label(content, legacy ? "Hold the pose, then recapture it. Preview the result before saving. Saving backs up the original file." :
                "Choose a name, start the countdown, and hold the pose. After capture, preview it and edit its matching and constraints. Choose Save to create the file.", 0, 184, 632, 118, 20);
            layout.Label(content, "Tracking and model test options are available in Settings.", 0, 326, 632, 70, 18);
        }
        private void DrawFiles()
        {
            Button("Back", 0, 0, 82, () => Show(poses.LoadedPose != null ? "pose" : "library"));
            layout.Label(content, "Open saved pose", 98, 0, 390, 40, 23, true);
            Button("Refresh", 510, 0, 122, () => { files = poses.ListPoses(); Draw(); });
            int pages = Mathf.Max(1, Mathf.CeilToInt(files.Length/(float)PageSize));
            page = Mathf.Clamp(page, 0, pages-1);
            if (files.Length == 0) layout.Label(content, "No saved poses. Choose New pose to capture one.", 0, 64, 632, 92);
            for (int i=page*PageSize; i<Math.Min(files.Length,(page+1)*PageSize); i++)
            {
                string name = files[i];
                Button(name, 0, 60+(i%PageSize)*52, 510, () => guard(() =>
                {
                    poses.Load(name);
                    if (poses.LegacyPoseName == name || poses.LoadedName == name)
                    { SynchronizeSelection(); Show(poses.LegacyPoseName == name ? "legacy" : "pose"); }
                }));
                Button("Delete", 520, 60+(i%PageSize)*52, 112, () => Delete(name));
            }
            if (pages <= 1) return;
            Button("Previous", 0, 388, 130, () => { page--; Draw(); });
            layout.Label(content, $"Page {page+1} of {pages}", 148, 388, 336, 40, 18);
            Button("Next", 502, 388, 130, () => { page++; Draw(); });
        }
        private void Delete(string name)
        {
            confirm("Delete pose?", $"Permanently delete '{name}'? Unsaved edits to it will be lost. Moves using this file may need updating.", "Delete", () =>
            {
                if (poses.DeleteSaved(name)) { SynchronizeSelection(); files = poses.ListPoses(); Show("files"); }
                else Refresh();
            });
        }
        private TMP_Text Button(string text, float x, float y, float width, UnityAction action, bool primary = false) =>
            layout.Button(content, text, x, y, width, 42, action, primary);
    }
}

