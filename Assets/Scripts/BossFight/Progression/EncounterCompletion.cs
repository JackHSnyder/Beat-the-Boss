using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum LevelEndBasis { Timeline, Song, Points }

    // Results and the retry snapshot survive the scene fade, without writing a save file.
    internal static class LevelCompletionSession
    {
        internal sealed class Result
        {
            internal string title, reason;
            internal bool success, died;
            internal long points;
            internal int target;
            internal double elapsed, duration;
            internal LevelEndBasis basis;
            internal BossTimelineStore.Document level;
            internal LevelLoadout template;
        }
        internal static Result Last { get; private set; }
        private static bool pending;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Last = null; pending = false; }
        internal static void Store(Result result) { Last = result; pending = true; }
        internal static bool TakePresentation() { bool show = pending && Last != null; pending = false; return show; }
        internal static void Retry()
        {
            if (Last?.level == null || SceneHandler.IsTransitioning) return;
            LevelLoadout loadout = LevelLoadout.FromTimeline(Last.level, Last.template);
            EncounterLevelSession.Retry(Last.level, loadout);
            MoveLoadout.SelectLevel(loadout);
            pending = false;
            SceneHandler.TransitionTo(SceneDestination.Boss);
        }
    }

    [DefaultExecutionOrder(700)]
    [RequireComponent(typeof(BossScore))]
    public sealed class EncounterCompletion : MonoBehaviour
    {
        public bool HasEnded { get; private set; }
        private BossController boss;
        private BossScore score;
        private BossSongPlayback song;
        private EncounterPlayerDamage player;
        private BossTimelineStore.Document snapshot;
        private bool transitionStarted;

        private void Start()
        {
            boss = GetComponent<BossController>(); score = GetComponent<BossScore>();
            if (boss == null || boss.previewMode) { enabled = false; return; }
            song = GetComponent<BossSongPlayback>();
            foreach (var candidate in FindObjectsByType<EncounterPlayerDamage>())
                if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
            snapshot = BossTimelineStore.Capture(boss);
            snapshot.name = boss.LoadedLevel?.name ?? EncounterLevelSession.BundledTitle ?? boss.timelineName ?? "Level";
            snapshot.levelId = boss.LoadedLevel?.levelId ?? MoveLoadout.ActiveLevel?.Id ?? Guid.NewGuid().ToString("N");
            snapshot.loadoutSlots = LevelLoadout.CopySlots(boss.LoadedLevel?.loadoutSlots ?? MoveLoadout.ActiveLevel?.Slots);
        }

        private void LateUpdate()
        {
            if (boss == null || snapshot == null) return;
            if (HasEnded) { ReturnWhenReady(); return; }
            // Death wins if a lethal hit and a point-goal hit happen in the same frame.
            if (player != null && player.Health <= 0) { End(true); return; }
            switch (boss.endBasis)
            {
                case LevelEndBasis.Points:
                    if (score.CurrentPoints >= score.TargetPoints) End(false);
                    break;
                case LevelEndBasis.Timeline:
                    if (boss.ElapsedTime >= boss.TimelineDuration - .000001) End(false);
                    break;
                case LevelEndBasis.Song:
                    if (string.IsNullOrWhiteSpace(boss.songId) || song?.LoadError != null)
                        End(false, "Song unavailable", true);
                    else if (song?.ClipLength > 0 && boss.ElapsedTime >= boss.songDelay + song.ClipLength)
                        End(false);
                    break;
            }
        }

        public void Died() => End(true);

        private void End(bool died, string reason = null, bool forceFailure = false)
        {
            if (HasEnded || boss == null || snapshot == null || boss.previewMode) return;
            HasEnded = true;
            bool success = !died && !forceFailure && score.CurrentPoints >= score.TargetPoints;
            if (success) LevelProgress.MarkPassed(snapshot.levelId);
            double duration = boss.endBasis == LevelEndBasis.Timeline ? boss.TimelineDuration :
                boss.endBasis == LevelEndBasis.Song && song?.ClipLength > 0 ? boss.songDelay + song.ClipLength : 0;
            double elapsed = boss.ElapsedTime;
            if (!died && duration > 0) elapsed = Math.Min(elapsed, duration);
            LevelCompletionSession.Store(new LevelCompletionSession.Result
            {
                title = snapshot.name, success = success, died = died, points = score.CurrentPoints,
                target = score.TargetPoints, elapsed = elapsed, duration = duration, basis = boss.endBasis,
                reason = reason ?? (died ? "You died" : !success ? "Point target not reached" :
                    boss.endBasis == LevelEndBasis.Points ? "Point target reached" :
                    boss.endBasis == LevelEndBasis.Song ? "Song finished" : "Timeline finished"),
                level = snapshot, template = MoveLoadout.ActiveLevel ?? EncounterLevelSession.Template
            });
            boss.StopTimeline();
            if (player != null) player.StopEncounterDamage();
            ReturnWhenReady();
        }

        private void ReturnWhenReady()
        {
            if (transitionStarted || SceneHandler.IsTransitioning) return;
            transitionStarted = true;
            SceneHandler.TransitionTo(SceneDestination.MainMenu);
        }
    }
}
