using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Carries the chosen level across scene fades, including a return from Recordings.
    internal static class EncounterLevelSession
    {
        internal enum Selection { Default, New, Bundled, Saved }
        internal static Selection Mode { get; private set; }
        internal static string TimelineName { get; private set; }
        internal static string BundledTitle { get; private set; }
        internal static LevelLoadout Template { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Mode=Selection.Default; TimelineName=BundledTitle=null; Template=null; }

        internal static void New(LevelLoadout template)
        { Mode=Selection.New; TimelineName=BundledTitle=null; Template=template; }
        internal static void Saved(string name, LevelLoadout template)
        { Mode=Selection.Saved; TimelineName=name; BundledTitle=null; Template=template; }
        internal static void Bundled(string title, LevelLoadout template)
        { Mode=Selection.Bundled; TimelineName=null; BundledTitle=title; Template=template; }
    }
}
