// Builds the in-game controls for editing and testing move sequences, action sets, and timers.
// PoseGamePanel hosts it, and MoveWorkbench handles drafts and test runs.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    internal sealed class MovePanel
    {
        private readonly PosePanelLayout layout;
        private readonly Transform root;
        private readonly MoveWorkbench moves;
        private readonly PoseWorkbench poses;
        private readonly Action<string, string, Action<string>> editName;
        private RectTransform content;
        private TMP_Text status;
        private int stepIndex = -1;
        private int actionIndex = -1;
        private int stepPage;
        private int actionPage;
        private int pickerPage;
        private string pickerTitle;
        private (string label, string id, PoseCategory? category)[] pickerItems;
        private Action<string> pick;
        private bool pickingBone;
        private PoseCategory pickerCategory;

        public MovePanel(PosePanelLayout layout, Transform root, MoveWorkbench moves, PoseWorkbench poses,
            Action<string, string, Action<string>> editName)
        {
            this.layout = layout;
            this.root = root;
            this.moves = moves;
            this.poses = poses;
            this.editName = editName;
            status = layout.Label(root, "", 0, 372, 632, 64, 17);
            Draw();
        }

        // Update live feedback separately from the controls so frame updates do not rebuild the editor.
        public void Refresh()
        {
            MoveDetector detector = moves.Detector;
            if (detector == null)
            {
                status.text = moves.Status;
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            double cooldown = detector.CooldownRemaining(now);
            string progress = cooldown > 0 ? $"Cooldown {cooldown:0.00}s" :
                $"Step {detector.StepIndex + 1}/{detector.StepCount}  ·  {detector.ActionsInWindow} actions in window";
            status.text = $"{progress}  ·  Global {Seconds(detector.GlobalRemaining(now))}  ·  Temp {Seconds(detector.TemporaryRemaining(now))}\n" +
                $"Passed {moves.Passes}  ·  Timed out {moves.Timeouts}  ·  {moves.Status}";
        }

        private static string Seconds(double? value) => value.HasValue ? $"{value.Value:0.00}s" : "off";

        private void Draw()
        {
            if (content != null)
            {
                content.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(content.gameObject);
            }
            content = layout.Rect(root, "Move editor controls", 0, 0, 632, 372);

            if (pick != null)
                DrawPicker();
            else if (moves.Draft == null)
                DrawLibrary();
            else if (actionIndex >= 0)
                DrawAction();
            else if (stepIndex >= 0)
                DrawStep();
            else
                DrawMove();
            Refresh();
        }

        private void DrawLibrary()
        {
            Title("Moves");
            Button("New move", 408, 0, 224, () => { moves.NewMove(); ResetEditor(); Draw(); });
            Button("Open saved move", 0, 54, 632, OpenSaved);
        }

        private void DrawMove()
        {
            MoveDefinition move = moves.Draft;
            Button("New", 0, 0, 78, () => { moves.NewMove(); ResetEditor(); Draw(); });
            Button("Open", 86, 0, 78, OpenSaved);
            Button(move.name, 172, 0, 270, () => editName("Move name", move.name, value =>
            {
                move.name = value;
                Draw();
            }));
            Button("Save", 450, 0, 82, () => { moves.Save(); Draw(); });
            Button(moves.Detector == null ? "Test" : "Stop", 540, 0, 92, () =>
            {
                if (moves.Detector == null) moves.StartTest(); else moves.StopTest();
                Draw();
            }, true);

            Number("Global timer", $"{move.timeLimit:0.00}s", 49, 0.1f, 1,
                delta => move.timeLimit = Mathf.Max(0.01f, Adjust(move.timeLimit, delta)));
            Number("Cooldown", $"{move.cooldown:0.00}s", 95, 0.1f, 1,
                delta => move.cooldown = Mathf.Max(0, Adjust(move.cooldown, delta)));
            Button("Add pose", 0, 145, 306, () => AddStep(MoveStepKind.Pose));
            Button("Add action set", 318, 145, 314, () => AddStep(MoveStepKind.ActionSet));
            DrawRows(move.steps.Count, stepPage, 3, 193, index =>
                $"{index + 1}. {StepLabel(move.steps[index])}", index =>
                { stepIndex = index; actionPage = 0; Draw(); }, page => stepPage = page);
        }

        private void DrawStep()
        {
            MoveStep step = CurrentStep;
            Button("Back", 0, 0, 82, () => { stepIndex = -1; Draw(); });
            layout.Label(content, $"Step {stepIndex + 1}  ·  {step.kind switch { MoveStepKind.Pose => "Pose", _ => "Action set" }}",
                92, 0, 248, 40, 21, true);
            Button("↑", 345, 0, 48, () => MoveStepBy(-1));
            Button("↓", 401, 0, 48, () => MoveStepBy(1));
            Button("Remove step", 458, 0, 174, () =>
            {
                moves.Draft.steps.RemoveAt(stepIndex);
                stepIndex = -1;
                Draw();
            });

            Button($"Temp timer  ·  {step.timerMode}  ›", 0, 51, 252, () =>
            {
                step.timerMode = (MoveTimerMode)(((int)step.timerMode + 1) % 3);
                Draw();
            });
            if (step.timerMode != MoveTimerMode.None)
                Number("", $"{step.timerSeconds:0.00}s", 51, 0.1f, 1,
                    delta => step.timerSeconds = Mathf.Max(0, Adjust(step.timerSeconds, delta)), labelVisible: false);

            if (step.kind == MoveStepKind.Pose)
            {
                Button(string.IsNullOrEmpty(step.poseName) ? "Choose saved pose" : step.poseName + "  ›", 0, 109, 632,
                    () => OpenPicker("Choose saved pose", poses.ListPoses(), name => step.poseName = name));
                return;
            }

            Number("Action window", $"{step.actionWindow:0.00}s", 102, 0.05f, 0.5f,
                delta => step.actionWindow = Mathf.Max(0, Adjust(step.actionWindow, delta)));
            Button("Add action", 0, 150, 632, () =>
            {
                step.actions.Add(new MoveAction { sampleSeconds = 0.2f, directionMatters = true });
                actionIndex = step.actions.Count - 1;
                Draw();
            });
            DrawRows(step.actions.Count, actionPage, 3, 197, index =>
            {
                MoveAction action = step.actions[index];
                return $"{index + 1}. {action.kind}  ·  {action.boneId ?? "Choose bone"}";
            }, index => { actionIndex = index; Draw(); }, page => actionPage = page);
        }

        private void DrawAction()
        {
            MoveAction action = CurrentStep.actions[actionIndex];
            Button("Back", 0, 0, 82, () => { actionIndex = -1; Draw(); });
            Button($"{action.kind} velocity  ›", 92, 0, 319, () => ChangeAction(value =>
            {
                value.kind = value.kind == MoveVelocityKind.Position ? MoveVelocityKind.Rotation : MoveVelocityKind.Position;
                return value;
            }));
            Button("Remove action", 421, 0, 211, () =>
            {
                CurrentStep.actions.RemoveAt(actionIndex);
                actionIndex = -1;
                Draw();
            });
            Button(action.boneId ?? "Choose bone", 0, 48, 632, OpenBones);
            Number("Sample time", $"{action.sampleSeconds:0.00}s", 96, 0.05f, 0.5f, delta => ChangeAction(value =>
            {
                value.sampleSeconds = Mathf.Max(0.01f, Adjust(value.sampleSeconds, delta));
                return value;
            }, redraw: false));
            Button(action.directionMatters ? "Direction matters" : "Either direction", 0, 143, 632, () => ChangeAction(value =>
            {
                value.directionMatters = !value.directionMatters;
                return value;
            }));
            bool rotation = action.kind == MoveVelocityKind.Rotation;
            for (int axis = 0; axis < 3; axis++)
            {
                int index = axis;
                Number($"{(char)('X' + axis)} minimum", $"{action.velocity[axis]:0.##} {(rotation ? "°/s" : "m/s")}",
                    190 + axis * 45, rotation ? 1 : 0.01f, rotation ? 45 : 1, delta => ChangeAction(value =>
                    {
                        value.velocity[index] = Adjust(value.velocity[index], delta);
                        return value;
                    }, redraw: false));
            }
            layout.Label(content, "Player-local axes  ·  Zero skips an axis  ·  Negative values reverse direction",
                0, 326, 632, 38, 16);
        }

        private void AddStep(MoveStepKind kind)
        {
            moves.Draft.steps.Add(new MoveStep { kind = kind });
            stepIndex = moves.Draft.steps.Count - 1;
            actionPage = 0;
            Draw();
        }

        private void MoveStepBy(int delta)
        {
            int index = stepIndex + delta;
            var steps = moves.Draft.steps;
            if ((uint)index >= steps.Count)
                return;
            (steps[stepIndex], steps[index]) = (steps[index], steps[stepIndex]);
            stepIndex = index;
            Draw();
        }

        private void OpenSaved() => OpenPicker("Saved moves", moves.ListMoves(), name => { moves.Load(name); ResetEditor(); });

        private void OpenPicker(string title, string[] items, Action<string> action)
        {
            pickerTitle = title;
            pickerItems = Array.ConvertAll(items, id => (id, id, (PoseCategory?)null));
            pickerPage = 0;
            pick = action;
            Draw();
        }

        private void DrawPicker()
        {
            Button("Back", 0, 0, 82, () =>
            {
                if (pickingBone && pickerCategory != PoseCategory.All)
                    OpenBoneCategory(PoseMatchProfile.Parent(pickerCategory));
                else
                {
                    pick = null;
                    pickingBone = false;
                    Draw();
                }
            });
            layout.Label(content, pickerTitle, 94, 0, 530, 40, 22, true);
            DrawRows(pickerItems.Length, pickerPage, 5, 50, index => pickerItems[index].label, index =>
            {
                var choice = pickerItems[index];
                if (choice.category is PoseCategory category)
                {
                    OpenBoneCategory(category);
                    return;
                }
                Action<string> accept = pick;
                pick = null;
                pickingBone = false;
                accept(choice.id);
                Draw();
            }, page => pickerPage = page);
        }

        private void OpenBones()
        {
            pickingBone = true;
            pick = id => ChangeAction(value => { value.boneId = id; return value; }, redraw: false);
            OpenBoneCategory(PoseCategory.All);
        }

        private void OpenBoneCategory(PoseCategory category)
        {
            pickerCategory = category;
            var direct = new List<string>();
            var descendants = new HashSet<PoseCategory>();
            foreach (SkeletonBone joint in UniversalSkeleton.Bones)
            {
                PoseCategory group = joint.Category;
                if (group == category)
                    direct.Add(joint.Id);
                while (group != PoseCategory.All && group != category)
                {
                    PoseCategory parent = PoseMatchProfile.Parent(group);
                    if (parent == category)
                        descendants.Add(group);
                    group = parent;
                }
            }
            direct.Sort(StringComparer.Ordinal);
            var choices = new List<(string label, string id, PoseCategory? category)>();
            foreach (PoseCategory child in PoseMatchProfile.Children(category))
                if (descendants.Contains(child))
                    choices.Add((PoseMatchProfile.Label(child) + "  ›", null, child));
            foreach (string id in direct)
                choices.Add((id, id, null));
            pickerTitle = category == PoseCategory.All ? "Choose bone" : PoseMatchProfile.Label(category);
            pickerItems = choices.ToArray();
            pickerPage = 0;
            Draw();
        }

        private void DrawRows(int count, int page, int size, int y, Func<int, string> label, Action<int> select,
            Action<int> setPage)
        {
            int pages = Mathf.Max(1, (count + size - 1) / size);
            page = Mathf.Clamp(page, 0, pages - 1);
            setPage(page);
            if (count == 0)
                layout.Label(content, "No items", 0, y, 632, 40);
            for (int i = page * size; i < Math.Min(count, (page + 1) * size); i++)
            {
                int index = i;
                Button(label(i), 0, y + (i % size) * 43, 632, () => select(index));
            }
            if (pages <= 1)
                return;
            Button("‹", 0, y + size * 43 + 2, 66, () => { setPage(page - 1); Draw(); });
            layout.Label(content, $"Page {page + 1} of {pages}", 83, y + size * 43 + 2, 455, 37, 17);
            Button("›", 566, y + size * 43 + 2, 66, () => { setPage(page + 1); Draw(); });
        }

        private void Number(string label, string value, int y, float fine, float coarse, Action<float> change,
            bool labelVisible = true)
        {
            if (labelVisible)
                layout.Label(content, label, 0, y, 232, 40);
            Button("−−", 261, y, 57, () => { change(-coarse); Draw(); });
            Button("−", 326, y, 45, () => { change(-fine); Draw(); });
            TMP_Text number = layout.Label(content, value, 377, y, 137, 40, 18);
            number.alignment = TextAlignmentOptions.Midline;
            Button("+", 522, y, 45, () => { change(fine); Draw(); });
            Button("++", 575, y, 57, () => { change(coarse); Draw(); });
        }

        private void ChangeAction(Func<MoveAction, MoveAction> change, bool redraw = true)
        {
            CurrentStep.actions[actionIndex] = change(CurrentStep.actions[actionIndex]);
            if (redraw)
                Draw();
        }

        private void ResetEditor()
        {
            stepIndex = actionIndex = -1;
            stepPage = actionPage = 0;
        }

        private MoveStep CurrentStep => moves.Draft.steps[stepIndex];
        private static float Adjust(float value, float delta) => Mathf.Round((value + delta) * 100) / 100;
        private static string StepLabel(MoveStep step) => step.kind == MoveStepKind.Pose
            ? step.poseName ?? "Choose pose" : $"Action set  ·  {step.actions.Count} actions";
        private void Title(string title) => layout.Label(content, title, 0, 0, 390, 40, 23, true);
        private void Button(string text, float x, float y, float width, UnityEngine.Events.UnityAction action, bool primary = false) =>
            layout.Button(content, text, x, y, width, 39, action, primary);
    }
}
