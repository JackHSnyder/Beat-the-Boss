# Level songs and characters

Level timeline JSON stores `songId`, `bossModelId`, and `songDelay` (seconds from the start of the encounter). It never embeds audio, prefabs, meshes, or textures. Old levels default to no song, zero delay, and StylizedCharacter. Missing songs are silent; missing boss models use StylizedCharacter while preserving the requested ID so reinstalling the asset repairs the level.

The built-in catalog is `Assets/Resources/Game Asset Catalog.asset`. It registers `song-ethreal`, `song-chill`, the protected `character-stylized`, `character-realistic` (RealisticCharacter), and `character-tony` (Tony). Player character selection is saved on the device independently of the model selected for a level's boss. All catalog songs and characters are protected built-in content. StylizedCharacter remains the fallback.

## Manually adding files

Use the game's save folder (`Application.persistentDataPath`, also shown in Recording Settings). Inside it:

```
DownloadedAssets/
  Songs/
    My Song.wav
    Another Song.mp3
  CharacterModels/
    character-my-model/
      asset.json
      character.bundle
```

Songs support WAV, MP3, and OGG. The filename supplies the displayed title. Their ID is a SHA-256 hash of the file contents, so changing a filename preserves level references. Replacing the audio contents creates a different ID. Use Refresh in the assets panel or editor picker after adding files. There is no in-game import/upload control.

Characters are Unity AssetBundles containing a configured prefab and all its dependencies. Copying a raw `.prefab` or `.fbx` into a built game cannot load its Unity materials, scripts, and references. In Unity, select a character prefab and use **Tools > Beat the Boss > Export selected character package**. Copy `asset.json` and `character.bundle` from the reported `Temp/CharacterPackages/<id>` folder to a folder under `DownloadedAssets/CharacterModels` on the target device. Build for the target platform: Windows and Quest/Android bundles are different. Use the same Unity version and installed runtime scripts as the game.

The exporter uses the prefab's persistent Unity GUID to produce its character ID. Keep its `.meta` file when updating a model. Example manifest:

```json
{
  "version": 1,
  "id": "character-my-model",
  "title": "My Character",
  "prefab": "assets/characters/my character.prefab"
}
```

To preserve the game's existing animations and tracking, character prefabs must have a configured Meta `CharacterRetargeter` and all the mapped bones used by StylizedCharacter, including fingers. A `CharacterBoneMap` component on the retargeter can alias native transforms to those established names without renaming the source skeleton. Its index-tip aliases support aiming and poking; per-bone aim corrections preserve the existing attack directions across different bind axes. The mapped native transforms are also used for recording previews, body masking, and boss animation playback. Incompatible rigs are rejected when equipped or selected.

RealisticCharacter and Tony are saved as mapped prefabs under `Assets/Characters/`. Only their required Meta sample dependencies are imported under `Assets/Characters/MetaSamples/`, with the source license retained. Tony's source sample renders a head and hands, so its saved prefab adds the Realistic sample's wetsuit body, fitted and rebound to Tony's skeleton in `Tony Body.asset`. **Tools > Beat the Boss > Set up additional Meta characters** can recreate their mappings, fitted body, and catalog entries. Their optional facial-expression and eye-gaze drivers are disabled; body tracking uses their existing Meta configurations.

For the live player, mapped hands use ISDK's tracked knuckle/tip positions for every finger, with neutral-palm calibration and per-frame restoration of position/rotation/scale corrections. Explicit custom finger mappings remain authoritative. `firstPersonHiddenRenderers` lists each model's face, eyes, and mouth; `LocalAvatarHeadVisibility` hides those renderers only while the owning camera rig renders. Preview copies, bosses, and other cameras retain their complete heads.

Finger fitting preserves thickness: the proximal/middle joints are positioned rather than uniformly scaled, and only the distal bone's length axis can change within 25% of its native length. Forearm twist helpers distribute the tracked wrist's roll along the forearm while preserving the palm pose.

`PlayerAvatarDriver` exposes **First-person chest clearance** settings. By default, downward gaze smoothly retracts the upper spine by up to 0.12 metres and leans it back up to 8 degrees, ramping between 20 and 70 degrees of downward head pitch with a 0.12-second response. Hip/foot placement and tracked hand poses stay anchored. The visual adjustment is restored before each tracking update, does not move the camera, and is skipped during recording model tests; it does not change saved boss animations.

Near vertical or inverted head orientations, body heading and chest-clearance direction stop following the headset's projected forward vector. The last reliable torso pose, scale, and neck-to-head offset are held instead; real headset translation and deliberate player-origin movement still carry the body. Different entry/exit thresholds prevent jitter around vertical, and normal tracking blends back over 0.25 seconds once the headset is upright enough. This applies to both the universal tracking driver and Meta retargeting, without changing saved animations or boss playback.

## Deleting and playback

Selecting a row highlights it. Delete asks for confirmation. Only newly added device files can be removed. Packaged songs and character models cannot be deleted or hidden; old removal records are ignored. Deleting an equipped downloaded character restores StylizedCharacter. Levels retain deleted asset IDs and show missing assets in the editor; reinstall the original file/package or select a replacement.

The editor's Encounter settings choose a song, its delay, and the boss model. These changes participate in Save, Undo/Redo, and unsaved-changes prompts. The song plays once, starting at its delay, and continues across event timeline loops. Editor playback follows play/pause and scrubbing. Restarting the encounter restarts its song delay. Master Volume affects encounter music.


## Packaged recordings and Quest builds

The current playable save library is snapshotted into `Assets/Resources/BuiltInContent`:

- 3 poses: ELBOW L, ELBOW R, GUN R.
- 3 moves: DashL, DashR, SHOOT R.
- 4 animations: DASH L, DASH R, IDLE, SHOOT RIGHT.
- 2 levels: EXAMPLE, EXAMPLE 2, including their saved move assignments as defaults.
- The Resources game asset catalog includes ethreal, chill, StylizedCharacter, RealisticCharacter, and Tony, with all entries protected.

Unity includes these Resources and their native audio/prefab dependencies in normal builds, including Android/Quest. Runtime JSON loads through `Resources.LoadAll<TextAsset>`, so it does not use desktop paths or filesystem access inside the APK. No save-folder transfer, first-run file extraction, network connection, or Windows AssetBundle is required. A fresh installation uses the packaged level loadouts; users can still choose different equipped moves and characters.

Built-in names take precedence over same-named device saves, appear once in file lists, and cannot be edited, renamed, overwritten, or deleted through the app. Recording panels retain preview/test controls. Built-in levels can be viewed and played in the boss editor, with editing disabled. New recordings and new levels remain device saves and remain editable. Original PC save files are preserved. Obsolete legacy backups, old unsupported save folders, logs, and device calibration/preferences are not game content and are not imported.

The snapshot is checked into the project and does not depend on the build machine's persistent save folder. To deliberately refresh it later, stop Play Mode and use **Tools > Beat the Boss > Package current saved content as built-in**. This imports the current recording JSON, resolves saved rename references, captures the current level move assignments, and protects the catalog. The tool adds/updates recordings; removing a shipped recording requires explicitly removing its project asset. Music/models are authored in the game asset catalog; downloaded external media must first be imported as native project assets for the target platform.

Use **Tools > Beat the Boss > Check built-in content** to validate recording data, level references, and write/delete protection before building. This does not replace testing an Android build on a Quest headset.
