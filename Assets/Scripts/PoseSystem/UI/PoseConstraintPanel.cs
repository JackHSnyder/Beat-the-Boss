// Builds the in-game editor for position and direction constraints, including category-based bone selection.
// PoseGamePanel hosts it, and it edits the loaded pose's PoseMatchProfile.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    internal sealed class PoseConstraintPanel
    {
        private const int RowsPerPage = 5;
        private readonly PoseWorkbench workbench;
        private readonly RectTransform list;
        private readonly RectTransform detail;
        private readonly RectTransform picker;
        private readonly TMP_Text summary;
        private readonly TMP_Text[] rules = new TMP_Text[RowsPerPage];
        private readonly TMP_Text[] choices = new TMP_Text[RowsPerPage];
        private readonly List<(PoseCategory? category, string boneId)> choicesInCategory = new();
        private readonly List<string>[] bonesByCategory = new List<string>[PoseMatchProfile.CategoryCount];
        private readonly int[] categoryCounts = new int[PoseMatchProfile.CategoryCount];
        private readonly TMP_Text pickerHeading;
        private readonly TMP_Text type;
        private readonly TMP_Text source;
        private readonly TMP_Text target;
        private readonly TMP_Text sourceAxis;
        private readonly TMP_Text targetAxis;
        private readonly TMP_Text error;
        private readonly TMP_Text limit;
        private readonly TMP_Text[] coordinates = new TMP_Text[3];
        private readonly RectTransform axes;
        private readonly RectTransform point;
        private readonly Button useLive;
        private readonly Button add;
        private readonly Button revert;
        private readonly Button save;
        private readonly Pager rulePager;
        private readonly Pager bonePager;
        private PoseMatchProfile profile;
        private int selected = -1;
        private int rulePage;
        private int bonePage;
        private bool pickingTarget;
        private bool pickingBone;
        private PoseCategory pickerCategory;

        public PoseConstraintPanel(PoseWorkbench workbench, PosePanelLayout layout, RectTransform root)
        {
            this.workbench = workbench;
            list = layout.Rect(root, "Constraint list", 0, 0, 632, 389);
            detail = layout.Rect(root, "Constraint editor", 0, 0, 632, 389);
            picker = layout.Rect(root, "Bone picker", 0, 0, 632, 389);
            summary = layout.Label(list, "", 0, 0, 370, 42, 21, true);
            add = ButtonFor(layout.Button(list, "Add constraint", 412, 0, 220, 42, Add));
            for (int i = 0; i < RowsPerPage; i++)
            {
                int row = i;
                rules[i] = layout.Button(list, "", 0, 56 + i * 53, 632, 46, () =>
                {
                    selected = rulePage * RowsPerPage + row;
                    Refresh();
                });
                rules[i].alignment = TextAlignmentOptions.MidlineLeft;
                choices[i] = layout.Button(picker, "", 0, 56 + i * 53, 632, 46, () => PickBone(row));
                choices[i].alignment = TextAlignmentOptions.MidlineLeft;
            }
            rulePager = new Pager(layout, list, change => { rulePage += change; Refresh(); });
            bonePager = new Pager(layout, picker, change => { bonePage += change; Refresh(); });

            layout.Button(detail, "Back", 0, 0, 84, 42, () => { selected = -1; Refresh(); });
            type = layout.Button(detail, "Position", 96, 0, 316, 42, () =>
            {
                PoseConstraint rule = profile.Constraints[selected];
                rule.type = rule.type == PoseConstraintType.Position ? PoseConstraintType.Direction : PoseConstraintType.Position;
                Update(rule);
            });
            layout.Button(detail, "Remove constraint", 424, 0, 208, 42, Remove);
            layout.Label(detail, "Bone", 0, 53, 110, 42);
            source = layout.Button(detail, "Choose bone", 120, 53, 512, 42, () => OpenPicker(false));
            layout.Label(detail, "Target", 0, 105, 110, 42);
            target = layout.Button(detail, "Player space", 120, 105, 512, 42, () => OpenPicker(true));

            axes = layout.Rect(detail, "Direction axes", 0, 161, 632, 100);
            layout.Label(axes, "Bone axis", 0, 0, 180, 42);
            sourceAxis = layout.Button(axes, "Forward", 190, 0, 442, 42, () => ChangeAxis(false));
            layout.Label(axes, "Target axis", 0, 51, 180, 42);
            targetAxis = layout.Button(axes, "Forward", 190, 51, 442, 42, () => ChangeAxis(true));

            point = layout.Rect(detail, "Player-local point", 0, 161, 632, 100);
            layout.Label(point, "Player-local point (m)", 0, 0, 305, 42);
            useLive = ButtonFor(layout.Button(point, "Use live bone position", 315, 0, 317, 42, UseLivePoint));
            for (int axis = 0; axis < coordinates.Length; axis++)
            {
                int index = axis;
                float x = axis * 213;
                layout.Button(point, "−", x, 51, 44, 42, () => ChangePoint(index, -0.05f));
                coordinates[axis] = layout.Label(point, "", x + 48, 51, 108, 42, 17);
                coordinates[axis].alignment = TextAlignmentOptions.Midline;
                layout.Button(point, "+", x + 160, 51, 44, 42, () => ChangePoint(index, 0.05f));
            }

            layout.Label(detail, "Tolerance", 0, 270, 114, 42);
            layout.Button(detail, "− coarse", 120, 270, 100, 42, () => ChangeLimit(-1, true));
            layout.Button(detail, "−", 228, 270, 46, 42, () => ChangeLimit(-1, false));
            limit = layout.Label(detail, "", 280, 270, 191, 42);
            limit.alignment = TextAlignmentOptions.Midline;
            layout.Button(detail, "+", 477, 270, 46, 42, () => ChangeLimit(1, false));
            layout.Button(detail, "+ coarse", 531, 270, 101, 42, () => ChangeLimit(1, true));
            error = layout.Label(detail, "", 0, 327, 632, 49, 19, true);

            layout.Button(picker, "Back", 0, 0, 84, 42, ClosePicker);
            pickerHeading = layout.Label(picker, "", 100, 0, 532, 42, 21, true);
            revert = ButtonFor(layout.Button(root, "Revert changes", 0, 396, 303, 40, workbench.RevertMatchSettings));
            save = ButtonFor(layout.Button(root, "Save matching settings", 315, 396, 317, 40, workbench.SaveMatchSettings, true));
        }

        public void Refresh()
        {
            if (profile != workbench.MatchProfile)
            {
                profile = workbench.MatchProfile;
                selected = -1;
                rulePage = 0;
                pickingBone = false;
            }

            bool loaded = profile != null;
            int count = loaded ? profile.Constraints.Count : 0;
            bool choosing = pickingBone;
            list.gameObject.SetActive(selected < 0);
            detail.gameObject.SetActive(selected >= 0 && !choosing);
            picker.gameObject.SetActive(choosing);
            add.interactable = loaded;
            save.interactable = revert.interactable = loaded && profile.IsDirty;
            if (choosing)
            {
                pickerHeading.text = pickerCategory == PoseCategory.All
                    ? (pickingTarget ? "Choose a target" : "Choose a bone")
                    : PoseMatchProfile.Label(pickerCategory);
                bonePage = bonePager.Show(bonePage, choicesInCategory.Count);
                for (int row = 0; row < RowsPerPage; row++)
                {
                    int index = bonePage * RowsPerPage + row;
                    bool available = index < choicesInCategory.Count;
                    string label = "";
                    if (available)
                    {
                        var choice = choicesInCategory[index];
                        label = choice.category is PoseCategory category
                            ? $"{PoseMatchProfile.Label(category)} ({categoryCounts[(int)category]})"
                            : BoneLabel(choice.boneId);
                    }
                    ShowRow(choices[row], available, label);
                }
                return;
            }
            if (selected >= 0)
            {
                RefreshDetail(profile.Constraints[selected]);
                return;
            }

            summary.text = !loaded ? "Load a pose to add constraints" :
                count == 0 ? "No constraints" : $"{workbench.Match.PassedConstraints} of {count} constraints passing";
            rulePage = rulePager.Show(rulePage, count);
            for (int row = 0; row < RowsPerPage; row++)
            {
                int index = rulePage * RowsPerPage + row;
                string label = "";
                if (index < count)
                {
                    PoseConstraint rule = profile.Constraints[index];
                    label = $"{index + 1}. {rule.type}  ·  {BoneLabel(rule.boneId)}  ·  {StateLabel(profile.ConstraintResult(index).State)}";
                }
                ShowRow(rules[row], index < count, label);
            }
        }

        private void RefreshDetail(PoseConstraint rule)
        {
            type.text = $"{rule.type}  ›";
            source.text = BoneLabel(rule.boneId) + "  ›";
            target.text = BoneLabel(rule.targetBoneId) + "  ›";
            bool direction = rule.type == PoseConstraintType.Direction;
            axes.gameObject.SetActive(direction);
            point.gameObject.SetActive(!direction && !rule.UsesBone);
            sourceAxis.text = $"{rule.boneAxis}  ›";
            targetAxis.text = $"{rule.targetAxis}  ›";
            useLive.interactable = workbench.CurrentFrame.TryGetPlayerPose(rule.boneId, out _, out _);
            for (int axis = 0; axis < coordinates.Length; axis++)
                coordinates[axis].text = $"{(char)('X' + axis)}  {rule.localPoint[axis]:0.00}";
            limit.text = direction ? $"{rule.maxAngle:0.#}°" : $"{rule.maxDistance:0.00} m";
            PoseConstraintResult result = profile.ConstraintResult(selected);
            error.text = StateLabel(result.State);
            if (result.State is PoseConstraintState.Passed or PoseConstraintState.OutsideTolerance)
                error.text += direction ? $"  ·  {result.Error:0.0}° deviation" : $"  ·  {result.Error:0.00} m away";
            error.color = result.State == PoseConstraintState.Passed ? PosePanelLayout.Accent : PosePanelLayout.Warning;
        }

        private void Add()
        {
            profile.AddConstraint(new PoseConstraint
            {
                boneId = profile.Pose.joints[0].id, maxDistance = 0.15f, maxAngle = 20
            });
            selected = profile.Constraints.Count - 1;
            rulePage = selected / RowsPerPage;
            Refresh();
        }

        private void Remove()
        {
            profile.RemoveConstraint(selected);
            selected = -1;
            Refresh();
        }

        // The same catalog is available even while a tracking device is offline.
        private void OpenPicker(bool targetBone)
        {
            pickingTarget = targetBone;
            pickingBone = true;
            Array.Clear(categoryCounts, 0, categoryCounts.Length);
            foreach (List<string> bones in bonesByCategory)
                bones?.Clear();
            foreach (SkeletonBone joint in UniversalSkeleton.Bones)
            {
                PoseCategory category = joint.Category;
                (bonesByCategory[(int)category] ??= new List<string>()).Add(joint.Id);
                while (category != PoseCategory.All)
                {
                    categoryCounts[(int)category]++;
                    category = PoseMatchProfile.Parent(category);
                }
            }

            OpenCategory(PoseCategory.All);
        }

        private void OpenCategory(PoseCategory category)
        {
            pickerCategory = category;
            bonePage = 0;
            choicesInCategory.Clear();
            if (category == PoseCategory.All && pickingTarget)
                choicesInCategory.Add((null, ""));
            foreach (PoseCategory child in PoseMatchProfile.Children(category))
            {
                if (categoryCounts[(int)child] > 0)
                    choicesInCategory.Add((child, null));
            }
            if (bonesByCategory[(int)category] is List<string> bones)
            {
                foreach (string id in bones)
                    choicesInCategory.Add((null, id));
            }
            Refresh();
        }

        private void ClosePicker()
        {
            if (pickerCategory != PoseCategory.All)
            {
                OpenCategory(PoseMatchProfile.Parent(pickerCategory));
                return;
            }
            pickingBone = false;
            Refresh();
        }

        private void PickBone(int row)
        {
            var choice = choicesInCategory[bonePage * RowsPerPage + row];
            if (choice.category is PoseCategory category)
            {
                OpenCategory(category);
                return;
            }

            PoseConstraint rule = profile.Constraints[selected];
            if (pickingTarget)
                rule.targetBoneId = choice.boneId;
            else
                rule.boneId = choice.boneId;
            pickingBone = false;
            Update(rule);
        }

        private void ChangeAxis(bool targetBone)
        {
            PoseConstraint rule = profile.Constraints[selected];
            if (targetBone)
                rule.targetAxis = (PoseAxis)(((int)rule.targetAxis + 1) % 6);
            else
                rule.boneAxis = (PoseAxis)(((int)rule.boneAxis + 1) % 6);
            Update(rule);
        }

        private void ChangePoint(int axis, float delta)
        {
            PoseConstraint rule = profile.Constraints[selected];
            rule.localPoint[axis] += delta;
            Update(rule);
        }

        // Store the sampled position in player space, which is also the space used when evaluating the rule.
        private void UseLivePoint()
        {
            PoseConstraint rule = profile.Constraints[selected];
            if (workbench.CurrentFrame.TryGetPlayerPose(rule.boneId, out Pose bone, out _))
            {
                rule.localPoint = bone.position;
                Update(rule);
            }
        }

        private void ChangeLimit(float direction, bool coarse)
        {
            PoseConstraint rule = profile.Constraints[selected];
            if (rule.type == PoseConstraintType.Direction)
                rule.maxAngle = Mathf.Clamp(rule.maxAngle + direction * (coarse ? 10 : 1), 0, 180);
            else
                rule.maxDistance = Mathf.Max(0, rule.maxDistance + direction * (coarse ? 0.1f : 0.01f));
            Update(rule);
        }

        private void Update(PoseConstraint rule)
        {
            profile.SetConstraint(selected, rule);
            Refresh();
        }

        private static string BoneLabel(string id) => string.IsNullOrEmpty(id) ? "Player space" : id;
        private static Button ButtonFor(TMP_Text label) => label.GetComponentInParent<Button>(true);

        private static void ShowRow(TMP_Text row, bool visible, string label)
        {
            row.GetComponentInParent<Button>(true).gameObject.SetActive(visible);
            if (visible)
                row.text = "  " + label + "  ›";
        }

        private static string StateLabel(PoseConstraintState state) => state switch
        {
            PoseConstraintState.Passed => "Pass",
            PoseConstraintState.OutsideTolerance => "Outside tolerance",
            PoseConstraintState.MissingTracking => "Bone tracking unavailable",
            PoseConstraintState.Invalid => "Incomplete constraint",
            _ => "Waiting for evaluation"
        };

        private sealed class Pager
        {
            private readonly Button previous;
            private readonly Button next;
            private readonly TMP_Text text;

            public Pager(PosePanelLayout layout, Transform root, Action<int> move)
            {
                previous = ButtonFor(layout.Button(root, "‹", 0, 329, 66, 42, () => move(-1)));
                next = ButtonFor(layout.Button(root, "›", 566, 329, 66, 42, () => move(1)));
                text = layout.Label(root, "", 81, 329, 470, 42, 17);
                text.alignment = TextAlignmentOptions.Midline;
            }

            public int Show(int page, int entries)
            {
                int pages = Mathf.Max(1, (entries + RowsPerPage - 1) / RowsPerPage);
                page = Mathf.Clamp(page, 0, pages - 1);
                previous.gameObject.SetActive(pages > 1);
                next.gameObject.SetActive(pages > 1);
                previous.interactable = page > 0;
                next.interactable = page + 1 < pages;
                text.text = pages > 1 ? $"Page {page + 1} of {pages}" : "";
                return page;
            }
        }
    }
}
