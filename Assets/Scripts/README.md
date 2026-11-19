# Script layout

Scripts are grouped by feature and responsibility. Folder changes preserve the scripts' Unity GUIDs; namespaces and class names do not need to match these folders.

## BossFight

- `Core` — boss encounter controller and skeleton/animation rig.
- `Attacks` — boss projectiles, lasers, and spike rows.
- `Player` — player firing, aiming, damage, targets, and movement around the arena.
- `Progression` — encounter completion, scoring, and saved level progress.
- `Timeline` — timeline file storage, encounter selection, and music playback.
- `TimelineEditor` — in-game timeline controls, pointer interactions, preview environment, and idle recording. These scripts run in the built game.
- `Content` — music and character catalog and asset loading.
- `UI` — score meter shared by gameplay and results.
- `Editor/Inspectors` — Unity custom inspectors.
- `Editor/Setup` — scene/content setup, character preparation, and content packaging tools.
- `Editor/Checks` — validation and regression checks run from the Unity Editor.
- `BossSystem.md` — encounter editor and gameplay documentation.

`TimelineEditor` is deliberately outside Unity's special `Editor` folder so the in-game editor remains available on Quest. Only Unity development tools belong under `Editor`.

## MainMenu

- `Navigation` — switching between menu sections.
- `Levels` — level selection, boss previews, completion results, and move loadouts.
- `Content` — browsing the music and character library.
- `Settings` — master volume and reduced transitions.
- `Presentation` — shared button appearance/layout, menu height, credits, and logo motion.

## Other systems

- `PoseSystem` retains its existing `Avatar`, `Tracking`, `Poses`, `Moves`, `UI`, and `Editor` groups.
- `SceneHandling` contains scene transitions, observer behavior, background audio, and exit controls.
- `AssemblyInfo.cs` remains at the root because it applies to the whole script assembly.

Move scripts through Unity's Project window or `AssetDatabase.MoveAsset` to keep their `.meta` files and scene/prefab references intact.
