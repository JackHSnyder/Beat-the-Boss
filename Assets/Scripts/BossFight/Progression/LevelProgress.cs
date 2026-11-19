using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Player progress stays separate from the authored level and survives renames.
    internal static class LevelProgress
    {
        internal static string Key(string levelId) => "LevelProgress.Completed." + Uri.EscapeDataString(levelId);
        internal static bool HasPassed(string levelId) => !string.IsNullOrWhiteSpace(levelId) && PlayerPrefs.GetInt(Key(levelId),0)==1;
        internal static void MarkPassed(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId) || HasPassed(levelId)) return;
            PlayerPrefs.SetInt(Key(levelId),1); PlayerPrefs.Save();
        }
        internal static void Clear(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId)) return;
            PlayerPrefs.DeleteKey(Key(levelId)); PlayerPrefs.Save();
        }
    }
}
