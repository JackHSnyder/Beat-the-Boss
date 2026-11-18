using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace BeatTheBoss.Poses
{
    internal sealed class AnimationPanel
    {
        private const int PageSize = 6;
        private readonly PosePanelLayout layout;
        private readonly Transform root;
        private readonly AnimationWorkbench animations;
        private readonly Action<string, string, Action<string>> editName;
        private readonly Action framePreview;
        private readonly Action<Action> guard;
        private readonly Action<string,string,string,Action> confirm;
        private readonly TMP_Text status;
        private RectTransform content;
        private RectTransform previewControls;
        private View view;
        private int page;
        private string[] saved;

        private enum View { Editor, Library, Bones }

        internal AnimationPanel(PosePanelLayout layout, Transform root, AnimationWorkbench animations,
            Action<string, string, Action<string>> editName, Action framePreview, Action<Action> guard, Action<string,string,string,Action> confirm)
        {
            this.layout = layout;
            this.root = root;
            this.animations = animations;
            this.editName = editName;
            this.framePreview = framePreview; this.guard = guard; this.confirm = confirm;
            status = layout.Label(root, "", 0, 372, 632, 64, 17);
        }

        internal void EnterPage()
        {
            view = View.Editor;
            Draw();
        }

        internal void LeavePage() => animations.LeaveEditor();

        internal void Refresh()
        {
            status.text = animations.Feedback;
            if (previewControls != null)
                previewControls.gameObject.SetActive(animations.PreviewVisible);
        }

        private void Draw()
        {
            if (content != null)
            {
                content.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(content.gameObject);
            }
            content = layout.Rect(root, "Animation controls", 0, 0, 632, 372);
            previewControls = null;
            switch (view)
            {
                case View.Library: DrawLibrary(); break;
                case View.Bones: DrawBones(); break;
                default: DrawEditor(); break;
            }
            Refresh();
        }

        private void DrawEditor()
        {
            bool hasTake = animations.Draft?.HasFrames == true;
            if (animations.IsRecording)
            {
                layout.Label(content, animations.Name, 0, 0, 632, 40, 22, true);
                Button("Stop recording", 0, 54, 310, animations.StopRecording, true);
                Button("Cancel recording", 322, 54, 310, () => confirm("Discard recording?", "Discard the take currently being recorded?", "Discard", () => { animations.CancelRecording(); Draw(); }));
                return;
            }

            Button("New", 0, 0, 78, () => guard(() => { animations.New(); Draw(); }));
            Button("Open", 86, 0, 78, () =>
            {
                animations.HidePreview();
                saved = animations.ListSaved();
                view = View.Library;
                page = 0;
            });
            Button(animations.Name + (animations.IsDirty ? " *" : ""), 172, 0, 368,
                () => editName("Animation name", animations.Name, value =>
                    animations.Name = string.IsNullOrWhiteSpace(value) ? "New animation" : value));
            var save = Button("Save", 548, 0, 84, animations.Save, hasTake);
            save.GetComponentInParent<UnityEngine.UI.Button>(true).interactable = hasTake;

            Button(hasTake ? "Record new take" : "Record animation", 0, 51, 208, () => { if (hasTake) guard(() => { animations.StartRecording(); Draw(); }); else animations.StartRecording(); }, true);
            if (animations.LoadedName != null)
                Button("Delete saved animation", 0, 316, 260, () => confirm("Delete animation?",
                    $"Permanently delete '{animations.LoadedName}'? Unsaved edits to it will be lost. Moves or timelines using this file may need updating.", "Delete", () =>
                    { if (animations.DeleteSaved()) { view = View.Editor; Draw(); } }));
            if (!hasTake)
            {
                layout.Label(content, "Uses the capture delay set on the Poses tab.", 0, 100, 632, 40, 17);
                return;
            }

            Button("Choose bones", 218, 51, 194, () =>
            {
                view = View.Bones;
                page = 0;
            });
            Button("Play preview", 422, 51, 210, () => animations.Play(animations.Draft));
            MoveAnimation take = animations.Draft;
            layout.Label(content, $"Playback range  ·  {take.Duration:0.00}s take", 0, 105, 480, 39, 21, true);
            Button("Full take", 522, 105, 110, animations.ResetRange);
            DrawBoundary(take, "Start", true, 153);
            DrawBoundary(take, "End", false, 205);

            previewControls = layout.Rect(content, "Preview controls", 0, 260, 632, 39);
            layout.Button(previewControls, "Frame preview", 0, 0, 310, 39, framePreview.Invoke);
            layout.Button(previewControls, "Hide preview", 322, 0, 310, 39, () =>
            {
                animations.HidePreview();
                Refresh();
            });
        }

        private void DrawBoundary(MoveAnimation take, string name, bool start, float y)
        {
            float Boundary() => start ? take.PlaybackStart : take.PlaybackEnd;
            TMP_Text label = layout.Label(content, $"{name}  {Boundary():0.00}s", 0, y, 154, 46, 18);
            UnityEngine.UI.Slider slider = null;
            slider = layout.Slider(content, name, 160, y, 472, 0f, take.Duration, value =>
            {
                animations.SetBoundary(start, value);
                slider.SetValueWithoutNotify(Boundary());
                label.text = $"{name}  {Boundary():0.00}s";
                Refresh();
            }, wholeNumbers: false);
            slider.SetValueWithoutNotify(Boundary());
        }

        private void DrawLibrary()
        {
            Button("Back", 0, 0, 82, () => view = View.Editor);
            layout.Label(content, "Saved animations", 94, 0, 538, 40, 22, true);
            if (saved.Length == 0)
                layout.Label(content, "No saved animations.", 0, 49, 632, 40);
            DrawRows(saved.Length, index => Button(saved[index], 0, RowY(index), 632, () =>
            {
                string name = saved[index];
                guard(() => { animations.Load(name); view = View.Editor; Draw(); });
            }));
        }

        private void DrawBones()
        {
            Button("Back", 0, 0, 82, () => view = View.Editor);
            layout.Label(content, "Bones in this take", 94, 0, 265, 40, 22, true);
            Button("Select all", 366, 0, 130, () => animations.SetAllBones(true));
            Button("Clear all", 502, 0, 130, () => animations.SetAllBones(false));
            DrawRows(animations.AvailableBones.Count, index =>
            {
                string bone = animations.AvailableBones[index];
                int y = RowY(index);
                bool included = animations.Draft.IncludesBone(bone);
                layout.Label(content, bone, 0, y, 467, 39, 18);
                Button(included ? "Included" : "Excluded", 472, y, 160,
                    () => animations.ToggleBone(bone), included);
            });
        }

        private void DrawRows(int count, Action<int> drawRow)
        {
            int pages = Mathf.Max(1, (count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            for (int index = page * PageSize; index < Math.Min(count, (page + 1) * PageSize); index++)
                drawRow(index);
            if (pages <= 1)
                return;
            Button("‹", 0, 310, 66, () => page--);
            layout.Label(content, $"Page {page + 1} of {pages}", 83, 310, 455, 39, 17);
            Button("›", 566, 310, 66, () => page++);
        }

        private static int RowY(int index) => 48 + (index % PageSize) * 43;

        private TMP_Text Button(string label, float x, float y, float width, UnityAction action, bool primary = false) =>
            layout.Button(content, label, x, y, width, 39, () =>
            {
                action.Invoke();
                if (root.gameObject.activeSelf)
                    Draw();
            }, primary);
    }
}
