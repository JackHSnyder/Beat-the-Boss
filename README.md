# Beat the Boss

A standalone Meta Quest VR game where your movements become the controls. Perform recorded poses and moves to fire attacks, dash around the arena, and take on a boss whose attacks and animations follow an authored timeline.

Created by **Jack Snyder** for the **Meta VR Start Developer Competition 2026**.

[Download a Quest APK](https://github.com/JackHSnyder/Beat-the-Boss/releases) · [Content and packaging guide](ASSET_LIBRARY.md) · [Script organisation](Assets/Scripts/README.md)

## Play on Meta Quest

The downloadable APK is a **Meta Quest standalone build**: it runs on the headset without a PC connection during play. The Unity source project is also included in this repository.

1. Download the `.apk` attached to a [GitHub release](https://github.com/JackHSnyder/Beat-the-Boss/releases).
2. Enable Developer Mode on your headset and connect it to your computer with a data-capable USB cable.
3. Accept the headset's USB debugging prompt, then install the APK using Meta Quest Developer Hub or Android Debug Bridge:

   ```sh
   adb install -r "BeatTheBoss.apk"
   ```

   Replace the filename with the APK you downloaded.
4. Launch the installed game from your headset's app library. Sideloaded apps may appear under **Unknown Sources**.
5. Select a level from the main menu, review its **Choose Loadout** assignments, and select **Play Level**.

Gameplay uses the moves assigned to the level's Attack, Dash Left, and Dash Right slots. Perform the corresponding gestures to trigger those actions. Dash moves carry you around the arena; your attack fires a laser toward the boss. The recording tools provide pose previews, animation playback, and move testing to help inspect the available content.

Tracking availability varies by headset and input mode. The game includes hand/head and controller/head tracking modes; controller tracking alone does not directly measure every body joint.

## Features

- **Movement-based combat:** pose and move detection drives attacks and directional dashes.
- **Universal skeleton:** Meta tracking data is mapped to shared bone IDs used by recording, avatar playback, and detection.
- **Adjustable matching:** bone tolerances, constraints, and move timing let recognition account for natural differences in how players move.
- **In-game recording tools:** capture poses and animations, define moves, and preview or test them.
- **Boss timeline editor:** arrange animations, turning, projectiles, lasers, and spike attacks, with playback, scrubbing, and undo/redo.
- **Level loadouts and progression:** choose moves for each level, track scores, and save completion progress.
- **Built-in content:** shipped recordings, levels, music, and character models are available on a fresh installation.

## Included content

| Type | Packaged content |
| --- | --- |
| Levels | EXAMPLE, EXAMPLE 2 |
| Moves | DashL, DashR, SHOOT R |
| Poses | ELBOW L, ELBOW R, GUN R |
| Animations | DASH L, DASH R, IDLE, SHOOT RIGHT |
| Music | ethreal, chill |
| Characters | StylizedCharacter, RealisticCharacter, Tony |

Built-in content cannot be edited, renamed, overwritten, or deleted within the application. Newly created recordings and levels are saved on the device and remain editable. No PC save-folder transfer is needed to use the shipped content.

## Open the Unity project

### Requirements

- **Unity 6000.6.6f1**, the version recorded in [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt).
- Unity Hub and **Android Build Support**, including **Android SDK & NDK Tools** and **OpenJDK**, for Quest builds.
- Git available to Unity for the Meta Movement package dependency.
- A compatible Meta Quest headset for standalone testing.

Clone or download the repository, then add its root folder through **Unity Hub → Projects → Add**. Open it with the matching Editor version and allow package resolution and asset importing to finish. Dependencies are declared in [Packages/manifest.json](Packages/manifest.json), including Meta XR, Unity OpenXR, Input System, and Universal Render Pipeline.

Open `Assets/Scenes/MainMenuScene.unity` as the starting scene. Editor-based XR testing requires a configured headset connection or Meta XR Simulator; it does not replace testing the standalone APK.

| Scene | Purpose |
| --- | --- |
| MainMenuScene | Level selection, loadouts, settings, credits, and content browsing |
| RecordingScene | Pose, move, and animation authoring and testing |
| BossScene | Gameplay |
| BossEditorScene | Boss encounter timeline authoring and preview |

## Build for Quest

1. Open **File → Build Profiles**, select **Meta Quest** (or Android), and switch platforms.
2. Keep the four scenes above enabled, with **MainMenuScene first**.
3. Use **IL2CPP**, **ARM64**, and **Vulkan**. Check the Android/Quest configuration in Meta's **Project Setup Tool** and Unity's **XR Project Validation**.
4. Run **Tools → Beat the Boss → Check built-in content**.
5. Leave **Export Project** and **Build App Bundle** disabled to produce an APK. Use **Development Build** for debugging; leave it disabled for a release build.
6. Select **Build**, or select a connected headset and use **Build And Run** to install and launch it as well.

The packaged content is stored in `Assets/Resources/BuiltInContent` and the Resources game asset catalog. Normal builds include it automatically. To deliberately update the recording snapshot, stop Play Mode and use **Tools → Beat the Boss → Package current saved content as built-in**. See the [content guide](ASSET_LIBRARY.md) for details.

## Project structure

```text
Assets/
  Characters/             Character prefabs and supporting assets
  Resources/              Built-in recordings, catalog, and shared UI assets
  Scenes/                 Menu, recording, gameplay, and boss editor scenes
  Scripts/
    BossFight/            Combat, player actions, progression, and timeline tools
    MainMenu/             Navigation, level selection, settings, and presentation
    PoseSystem/           Tracking, universal skeleton, poses, moves, and recording UI
    SceneHandling/        Scene transitions and shared scene behaviour
Packages/                 Unity package declarations and lockfile
ProjectSettings/          Unity project and platform configuration
```

For more detail, see the [script folder guide](Assets/Scripts/README.md), [boss system documentation](Assets/Scripts/BossFight/BossSystem.md), and [asset library documentation](ASSET_LIBRARY.md).

## Credits

- **Application development:** Jack Snyder
- **Music:** Erika Wang

Third-party assets and packages retain their respective licence terms. The imported Meta character samples include their [Meta licence](Assets/Characters/MetaSamples/META-LICENSE.md).
