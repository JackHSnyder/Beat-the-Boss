// Saves the three gameplay move assignments on this device. The Loadout panel edits them.

using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum MoveSlot { Attack, DashLeft, DashRight }

    internal static class MoveLoadout
    {
        internal static readonly MoveSlot[] Slots = { MoveSlot.Attack, MoveSlot.DashLeft, MoveSlot.DashRight };

        internal static string Get(MoveSlot slot) => PlayerPrefs.GetString(Key(slot), "");

        internal static void Assign(MoveSlot slot, string moveName)
        {
            PlayerPrefs.SetString(Key(slot), moveName);
            PlayerPrefs.Save();
        }

        internal static void Clear()
        {
            foreach (MoveSlot slot in Slots)
                PlayerPrefs.DeleteKey(Key(slot));
            PlayerPrefs.Save();
        }

        private static string Key(MoveSlot slot) => "MoveLoadout." + slot;
    }
}
