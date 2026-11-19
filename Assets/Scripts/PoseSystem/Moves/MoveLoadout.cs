// Stores player assignments by level ID and authored slot ID.

using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum MoveSlot { Attack, DashLeft, DashRight, None }

    internal static class MoveLoadout
    {
        internal static LevelLoadout ActiveLevel { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => ActiveLevel = null;

        internal static void SelectLevel(LevelLoadout level)
        {
            level.Validate();
            ActiveLevel = level;
        }

        internal static LevelLoadout ForScene(string path, LevelLoadout fallback) =>
            ActiveLevel != null && ActiveLevel.ScenePath == path ? ActiveLevel : fallback;

        internal static string Get(LevelLoadout level, LevelLoadout.Slot slot)
        {
            string key = Key(level, slot);
            if (!PlayerPrefs.HasKey(key) && level.ImportLegacyAssignments && slot.Action != MoveSlot.None)
            {
                PlayerPrefs.SetString(key, PlayerPrefs.GetString("MoveLoadout." + slot.Action, slot.DefaultMove ?? ""));
                PlayerPrefs.Save();
            }
            string saved = PlayerPrefs.GetString(key, slot.DefaultMove ?? "");
            return string.IsNullOrWhiteSpace(saved) ? saved : MoveStore.ResolveName(saved);
        }

        internal static void Assign(LevelLoadout level, LevelLoadout.Slot slot, string moveName)
        {
            PlayerPrefs.SetString(Key(level, slot), moveName ?? "");
            PlayerPrefs.Save();
        }

        internal static void Clear(LevelLoadout level)
        {
            foreach (LevelLoadout.Slot slot in level.Slots)
                PlayerPrefs.SetString(Key(level, slot), "");
            PlayerPrefs.Save();
        }

        private static string Key(LevelLoadout level, LevelLoadout.Slot slot) =>
            "LevelMoveLoadout." + Uri.EscapeDataString(level.Id) + "." + Uri.EscapeDataString(slot.Id);
    }
}
