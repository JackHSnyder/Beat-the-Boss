using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    internal sealed class PoseMatchPanel
    {
        private const int RowsPerPage = 4;
        private const int ListY = 181;
        private static readonly Regex BoneWords = new(@"(?<=[a-z])(?=[A-Z])|_", RegexOptions.CultureInvariant);

        private readonly PoseWorkbench workbench;
        private readonly List<(PoseCategory category, int bone, string label)> entries = new();
        private readonly Text[] rows = new Text[RowsPerPage];
        private readonly Text heading;
        private readonly Text include;
        private readonly Text live;
        private readonly Text pageCount;
        private readonly Button back;
        private readonly Button previous;
        private readonly Button next;
        private readonly Button save;
        private readonly Button revert;
        private readonly NumberRow tolerance;
        private readonly NumberRow budget;
        private PoseMatchProfile profile;
        private PoseCategory category;
        private int bone = -1;
        private int page;

        public PoseMatchPanel(PoseWorkbench workbench, PosePanelLayout layout, RectTransform root)
        {
            this.workbench = workbench;
            back = ButtonFor(layout.Button(root, "Back", 0, 0, 84, 42, Back));
            heading = layout.Label(root, "", 96, 0, 354, 42, 22, true);
            include = layout.Button(root, "Included", 462, 0, 170, 42, ToggleIncluded);
            tolerance = new NumberRow(layout, root, "Tolerance", 51, ChangeTolerance, ResetTolerance);
            budget = new NumberRow(layout, root, "Budget", 100, ChangeBudget, ResetBudget);
            live = layout.Label(root, "", 0, 148, 632, 30, 17);

            for (int i = 0; i < rows.Length; i++)
            {
                int row = i;
                rows[i] = layout.Button(root, "", 0, ListY + i * 42, 632, 39, () => OpenRow(row));
                rows[i].alignment = TextAnchor.MiddleLeft;
            }

            previous = ButtonFor(layout.Button(root, "‹", 0, 351, 66, 36, () => ChangePage(-1)));
            pageCount = layout.Label(root, "", 81, 351, 470, 36, 17);
            pageCount.alignment = TextAnchor.MiddleCenter;
            next = ButtonFor(layout.Button(root, "›", 566, 351, 66, 36, () => ChangePage(1)));
            revert = ButtonFor(layout.Button(root, "Revert changes", 0, 396, 303, 40, workbench.RevertMatchSettings));
            save = ButtonFor(layout.Button(root, "Save matching settings", 315, 396, 317, 40, workbench.SaveMatchSettings, true));
        }

        public void Refresh()
        {
            if (profile != workbench.MatchProfile)
            {
                bool samePose = profile != null && profile.Pose == workbench.LoadedPose;
                profile = workbench.MatchProfile;
                if (!samePose)
                {
                    category = PoseCategory.All;
                    bone = -1;
                    page = 0;
                }
                BuildEntries();
            }

            bool loaded = profile != null;
            SetVisible(include, loaded);
            back.gameObject.SetActive(loaded && (bone >= 0 || category != PoseCategory.All));
            tolerance.SetVisible(loaded);
            budget.SetVisible(loaded && bone < 0);
            save.interactable = revert.interactable = loaded && profile.IsDirty;

            if (loaded)
                RefreshSelection();
            else
            {
                heading.text = "Load a pose to edit";
                live.text = "";
            }

            int pageTotal = Mathf.Max(1, (entries.Count + RowsPerPage - 1) / RowsPerPage);
            previous.gameObject.SetActive(pageTotal > 1);
            next.gameObject.SetActive(pageTotal > 1);
            previous.interactable = page > 0;
            next.interactable = page + 1 < pageTotal;
            pageCount.text = pageTotal > 1 ? $"Page {page + 1} of {pageTotal}" : "";
            for (int i = 0; i < rows.Length; i++)
            {
                int index = page * RowsPerPage + i;
                bool available = index < entries.Count;
                SetVisible(rows[i], available);
                if (!available)
                    continue;

                var entry = entries[index];
                bool included = entry.bone >= 0 ? profile.IsBoneIncluded(entry.bone) : profile.IsIncluded(entry.category);
                rows[i].text = $"  {entry.label}  ·  {(included ? "Included" : "Ignored")}  ›";
                rows[i].color = included ? PosePanelLayout.Foreground : PosePanelLayout.Muted;
            }
        }

        private void RefreshSelection()
        {
            float fallback = workbench.ToleranceDegrees;
            PoseCategorySetting setting = profile.GetCategory(category);
            if (bone >= 0)
            {
                PoseBoneSetting joint = profile.GetBone(bone);
                heading.text = BoneLabel(joint.id);
                SetIncludeLabel(joint.disabled, profile.IsIncluded(category));
                tolerance.Show(profile.BoneTolerance(bone, fallback), joint.overrideTolerance, "Use default");
                float error = profile.JointError(bone);
                live.text = !profile.IsBoneIncluded(bone) ? "This bone is ignored." :
                    (profile.Pose.joints[bone].branch & workbench.IgnoreBranches) != 0 ? "Ignored by the session hand mask." :
                    float.IsNaN(error) ? "Waiting for this joint." : $"Live error {error:0.0}°";
                live.color = PosePanelLayout.Muted;
                return;
            }

            heading.text = PoseMatchProfile.Label(category);
            SetIncludeLabel(setting.disabled, category == PoseCategory.All || profile.IsIncluded(PoseMatchProfile.Parent(category)));
            tolerance.Show(profile.Tolerance(category, fallback), setting.overrideTolerance,
                category == PoseCategory.All ? "Use global" : "Use parent");
            budget.Show(setting.budgetDegrees, setting.useBudget, "Off", "Off");
            float used = profile.UsedBudget(category);
            live.text = !profile.IsIncluded(category) ? "This category and its bones are ignored." : setting.useBudget
                ? $"{used:0.0}° spent of {setting.budgetDegrees:0.#}°  ·  {(profile.HasFailed(category) ? "Budget exhausted" : "Within budget")}"
                : $"{used:0.0}° over individual limits  ·  No budget at this level";
            live.color = profile.HasFailed(category) ? PosePanelLayout.Warning : PosePanelLayout.Muted;
        }

        private void SetIncludeLabel(bool disabled, bool parentIncluded)
        {
            include.text = disabled ? "Ignored" : parentIncluded ? "Included" : "Parent ignored";
            include.color = !disabled && parentIncluded ? PosePanelLayout.Accent : PosePanelLayout.Muted;
        }

        private void BuildEntries()
        {
            entries.Clear();
            if (profile == null || bone >= 0)
                return;

            foreach (PoseCategory child in PoseMatchProfile.Children(category))
            {
                if (profile.BoneCount(child) > 0)
                    entries.Add((child, -1, $"{PoseMatchProfile.Label(child)} ({profile.BoneCount(child)})"));
            }
            for (int i = 0; i < profile.Pose.joints.Length; i++)
            {
                if (profile.CategoryOf(i) == category)
                    entries.Add((category, i, BoneLabel(profile.Pose.joints[i].id)));
            }
        }

        private void OpenRow(int row)
        {
            var entry = entries[page * RowsPerPage + row];
            category = entry.category;
            bone = entry.bone;
            page = 0;
            BuildEntries();
            Refresh();
        }

        private void Back()
        {
            if (bone >= 0)
                bone = -1;
            else
                category = PoseMatchProfile.Parent(category);
            page = 0;
            BuildEntries();
            Refresh();
        }

        private void ChangePage(int change)
        {
            page += change;
            Refresh();
        }

        private void ToggleIncluded()
        {
            if (bone >= 0)
            {
                PoseBoneSetting joint = profile.GetBone(bone);
                joint.disabled = !joint.disabled;
                profile.SetBone(bone, joint);
            }
            else
            {
                PoseCategorySetting setting = profile.GetCategory(category);
                setting.disabled = !setting.disabled;
                profile.SetCategory(category, setting);
            }
            Refresh();
        }

        private void ChangeTolerance(float change)
        {
            if (bone >= 0)
            {
                PoseBoneSetting joint = profile.GetBone(bone);
                joint.toleranceDegrees = profile.BoneTolerance(bone, workbench.ToleranceDegrees) + change;
                joint.overrideTolerance = true;
                profile.SetBone(bone, joint);
            }
            else
            {
                PoseCategorySetting setting = profile.GetCategory(category);
                setting.toleranceDegrees = profile.Tolerance(category, workbench.ToleranceDegrees) + change;
                setting.overrideTolerance = true;
                profile.SetCategory(category, setting);
            }
            Refresh();
        }

        private void ResetTolerance()
        {
            if (bone >= 0)
            {
                PoseBoneSetting joint = profile.GetBone(bone);
                joint.overrideTolerance = false;
                profile.SetBone(bone, joint);
            }
            else
            {
                PoseCategorySetting setting = profile.GetCategory(category);
                setting.overrideTolerance = false;
                profile.SetCategory(category, setting);
            }
            Refresh();
        }

        private void ChangeBudget(float change)
        {
            PoseCategorySetting setting = profile.GetCategory(category);
            setting.budgetDegrees = (setting.useBudget ? setting.budgetDegrees : 0) + change;
            setting.useBudget = true;
            profile.SetCategory(category, setting);
            Refresh();
        }

        private void ResetBudget()
        {
            PoseCategorySetting setting = profile.GetCategory(category);
            setting.useBudget = false;
            profile.SetCategory(category, setting);
            Refresh();
        }

        private static string BoneLabel(string id) => BoneWords.Replace(id.Substring(id.IndexOf('/') + 1), " ");
        private static Button ButtonFor(Text label) => label.GetComponentInParent<Button>(true);
        private static void SetVisible(Text buttonLabel, bool visible) => buttonLabel.transform.parent.gameObject.SetActive(visible);

        private sealed class NumberRow
        {
            private readonly RectTransform root;
            private readonly Text value;
            private readonly Text resetLabel;
            private readonly Button reset;

            public NumberRow(PosePanelLayout layout, Transform parent, string label, float y,
                System.Action<float> change, UnityEngine.Events.UnityAction clear)
            {
                root = layout.Rect(parent, label + " controls", 0, y, 632, 42);
                layout.Label(root, label, 0, 0, 126, 42);
                layout.Button(root, "−10", 134, 0, 60, 42, () => change(-10));
                layout.Button(root, "−1", 200, 0, 49, 42, () => change(-1));
                value = layout.Label(root, "", 254, 0, 130, 42, 17);
                value.alignment = TextAnchor.MiddleCenter;
                layout.Button(root, "+1", 391, 0, 49, 42, () => change(1));
                layout.Button(root, "+10", 446, 0, 60, 42, () => change(10));
                resetLabel = layout.Button(root, "Reset", 516, 0, 116, 42, clear);
                reset = ButtonFor(resetLabel);
            }

            public void SetVisible(bool visible) => root.gameObject.SetActive(visible);

            public void Show(float degrees, bool custom, string resetText, string unsetText = null)
            {
                value.text = custom ? $"{degrees:0.#}°" : unsetText ?? $"{degrees:0.#}° inherited";
                value.color = custom ? PosePanelLayout.Accent : PosePanelLayout.Muted;
                reset.interactable = custom;
                resetLabel.text = resetText;
            }
        }
    }
}
