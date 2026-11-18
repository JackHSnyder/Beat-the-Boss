using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [CreateAssetMenu(menuName = "Beat the Boss/Level Loadout")]
    public sealed class LevelLoadout : ScriptableObject
    {
        [Serializable]
        public sealed class Slot
        {
            [SerializeField, Tooltip("Stable identifier used for saved assignments and gameplay events.")]
            private string id;
            [SerializeField] private string title;
            [SerializeField] private MoveSlot action = MoveSlot.None;

            public string Id => id;
            public string Title => title;
            public MoveSlot Action => action;
        }

        [SerializeField, Tooltip("Unique, stable identifier for this level's saved assignments.")]
        private string levelId;
        [SerializeField] private string levelTitle;
        [SerializeField] private string scenePath;
        [SerializeField] private Slot[] slots = Array.Empty<Slot>();
        [SerializeField, Tooltip("Import the old global assignments once for the original example level.")]
        private bool importLegacyAssignments;

        public string Id => levelId;
        public string Title => levelTitle;
        public string ScenePath => scenePath;
        public IReadOnlyList<Slot> Slots => slots;
        internal bool ImportLegacyAssignments => importLegacyAssignments;

        internal static Slot[] CopySlots(IEnumerable<Slot> source)
        {
            var copy=new List<Slot>();
            if (source!=null) foreach (var slot in source)
                copy.Add(JsonUtility.FromJson<Slot>(JsonUtility.ToJson(slot)));
            return copy.ToArray();
        }
        internal static LevelLoadout FromTimeline(BossTimelineStore.Document doc, LevelLoadout template)
        {
            var level=CreateInstance<LevelLoadout>();
            level.hideFlags=HideFlags.DontSave;
            level.levelId=doc.levelId; level.levelTitle=doc.name;
            level.scenePath="Assets/Scenes/BossScene.unity";
            level.importLegacyAssignments=template!=null && template.Id==doc.levelId && template.ImportLegacyAssignments;
            level.slots=CopySlots(doc.loadoutSlots ?? (IEnumerable<Slot>)template?.Slots);
            level.Validate();
            return level;
        }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(levelId) || string.IsNullOrWhiteSpace(levelTitle) ||
                string.IsNullOrWhiteSpace(scenePath) || slots == null)
                throw new ArgumentException("Give the level loadout an ID, title, scene path, and slots.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Slot slot in slots)
                if (slot == null || string.IsNullOrWhiteSpace(slot.Id) || string.IsNullOrWhiteSpace(slot.Title) ||
                    !ids.Add(slot.Id) || (uint)slot.Action > (uint)MoveSlot.None)
                    throw new ArgumentException("Each level loadout slot needs a unique ID, a name, and a valid action.");
        }
    }
}
