// Builds the in-game controls for inspecting tracking points and editing their universal bone assignments.
// PoseGamePanel hosts it, and it uses PlayerPoseTracker to access the current points and mappings.

using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    internal sealed class TrackingMapPanel
    {
        private const int PageSize = 5;
        private readonly PlayerPoseTracker tracker;
        private readonly TMP_Text[] rows = new TMP_Text[PageSize];
        private readonly List<string> points = new();
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private readonly List<(string label, string bone, PoseCategory? group)> choices = new();
        private readonly TMP_Text heading;
        private readonly TMP_Text feedback;
        private readonly TMP_Text pageLabel;
        private readonly Button back;
        private readonly Button next;
        private readonly Button previous;
        private readonly Button assign;
        private readonly Button reset;
        private readonly Button calibrate;
        private readonly Button save;
        private string selected;
        private string message;
        private int page;
        private bool choosing;
        private PoseCategory category;

        public TrackingMapPanel(PosePanelLayout layout, RectTransform root, PlayerPoseTracker tracker)
        {
            this.tracker = tracker;
            back = Button(layout.Button(root, "Back", 0, 0, 82, 42, Back));
            heading = layout.Label(root, "", 92, 0, 540, 42, 21, true);
            for (int i = 0; i < rows.Length; i++)
            {
                int index = i;
                rows[i] = layout.Button(root, "", 0, 50 + 49 * i, 632, 43, () => Select(index));
                rows[i].alignment = TextAlignmentOptions.MidlineLeft;
            }
            previous = Button(layout.Button(root, "‹", 0, 300, 62, 42, () => { page--; Refresh(); }));
            next = Button(layout.Button(root, "›", 570, 300, 62, 42, () => { page++; Refresh(); }));
            pageLabel = layout.Label(root, "", 75, 300, 482, 42);
            pageLabel.alignment = TextAlignmentOptions.Midline;
            assign = Button(layout.Button(root, "Assign bone", 0, 349, 193, 42, BeginAssignment));
            reset = Button(layout.Button(root, "Reset point", 206, 349, 193, 42, Reset));
            calibrate = Button(layout.Button(root, "Set neutral here", 412, 349, 220, 42, Calibrate));
            save = Button(layout.Button(root, "Save mappings", 435, 396, 197, 40, Save, true));
            feedback = layout.Label(root, "", 0, 391, 422, 45, 16);
        }

        private BoneMappings Mapping => tracker.Mappings;

        public void Refresh()
        {
            if (Mapping == null)
                return;
            if (!choosing && selected == null)
            {
                // Keep the list stable through short tracking gaps so rows do not jump during selection.
                foreach (string point in tracker.Points.Points.Keys)
                    if (seen.Add(point))
                        points.Add(point);
            }

            bool detail = selected != null && !choosing;
            back.gameObject.SetActive(selected != null);
            assign.gameObject.SetActive(detail);
            reset.gameObject.SetActive(detail);
            calibrate.gameObject.SetActive(detail);
            save.gameObject.SetActive(!choosing);
            calibrate.interactable = detail && !string.IsNullOrEmpty(Mapping.AssignedBone(selected)) &&
                tracker.Points.Points.TryGetValue(selected, out TrackingPoint pointSample) && pointSample.HasOrientation;
            heading.text = choosing ? PoseMatchProfile.Label(category) : detail ? selected : "Detected tracking points";
            feedback.text = message ?? (detail ? "Calibrate with the joint in its neutral orientation." :
                points.Count == 0 ? "Waiting for tracking points." : "Select a point to change its assignment.");

            int count = choosing ? choices.Count : detail ? 0 : points.Count;
            int last = Mathf.Max(0, (count - 1) / PageSize);
            page = Mathf.Clamp(page, 0, last);
            previous.gameObject.SetActive(last > 0);
            next.gameObject.SetActive(last > 0);
            previous.interactable = page > 0;
            next.interactable = page < last;
            pageLabel.text = detail ? "Assigned to " + (Mapping.AssignedBone(selected) ?? "Unassigned") :
                last > 0 ? $"Page {page + 1} of {last + 1}" : "";
            for (int i = 0; i < rows.Length; i++)
            {
                int index = page * PageSize + i;
                rows[i].GetComponentInParent<Button>(true).gameObject.SetActive(index < count);
                if (index >= count)
                    continue;
                string label;
                if (choosing)
                    label = choices[index].label;
                else
                {
                    string point = points[index];
                    label = point + "  →  " + (Mapping.AssignedBone(point) ?? "Unassigned");
                    if (!tracker.Points.Points.ContainsKey(point))
                        label += " (offline)";
                }
                rows[i].text = "  " + label;
            }
        }

        private void Select(int row)
        {
            int index = page * PageSize + row;
            if (!choosing)
            {
                selected = points[index];
                message = null;
            }
            else
            {
                var choice = choices[index];
                if (choice.group is PoseCategory child)
                {
                    OpenCategory(child);
                    return;
                }
                Mapping.Assign(selected, choice.bone);
                choosing = false;
                message = "Assignment changed. Save to keep it.";
            }
            Refresh();
        }

        private void BeginAssignment()
        {
            choosing = true;
            message = null;
            OpenCategory(PoseCategory.All);
        }

        private void OpenCategory(PoseCategory group)
        {
            category = group;
            page = 0;
            choices.Clear();
            if (group == PoseCategory.All)
                choices.Add(("Unassigned", null, null));
            foreach (PoseCategory child in PoseMatchProfile.Children(group))
                choices.Add((PoseMatchProfile.Label(child) + "  ›", null, child));
            foreach (SkeletonBone bone in UniversalSkeleton.Bones)
                if (bone.Category == group)
                    choices.Add((bone.Id, bone.Id, null));
            Refresh();
        }

        private void Back()
        {
            if (choosing && category != PoseCategory.All)
            {
                OpenCategory(PoseMatchProfile.Parent(category));
                return;
            }
            if (choosing)
                choosing = false;
            else
                selected = null;
            message = null;
            page = 0;
            Refresh();
        }

        private void Reset()
        {
            Mapping.Reset(selected);
            message = "Default assignment restored.";
            Refresh();
        }

        private void Calibrate()
        {
            if (tracker.Points.Points.TryGetValue(selected, out TrackingPoint point) && point.HasOrientation)
            {
                Mapping.Calibrate(selected, point.Pose.rotation);
                message = "Neutral orientation captured. Save to keep it.";
                Refresh();
            }
        }

        private void Save()
        {
            try
            {
                Mapping.Save();
                message = "Tracking mappings saved.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                message = error.Message;
            }
            Refresh();
        }

        private static Button Button(TMP_Text label) => label.GetComponentInParent<Button>(true);
    }
}
