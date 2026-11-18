# Level songs and characters

Level timeline JSON stores `songId`, `bossModelId`, and `songDelay` (seconds from the start of the encounter). It never embeds audio, prefabs, meshes, or textures. Old levels default to no song, zero delay, and StylizedCharacter. Missing songs are silent; missing boss models use StylizedCharacter while preserving the requested ID so reinstalling the asset repairs the level.

The built-in catalog is `Assets/Resources/Game Asset Catalog.asset`. It registers `song-ethreal`, `song-chill`, the protected `character-stylized`, `character-realistic` (RealisticCharacter), and `character-tony` (Tony). Player character selection is saved on the device independently of the model selected for a level's boss. The additional Meta characters can be deleted from the library; StylizedCharacter remains the protected fallback.

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

Selecting a row highlights it. Delete asks for confirmation. Downloaded files are removed; packaged songs are removed from the library using a persistent removal record, since installed build contents are read-only. Deleting an equipped character restores StylizedCharacter. StylizedCharacter cannot be deleted. Levels retain deleted asset IDs and show missing assets in the editor; reinstall the original file/package or select a replacement.

The editor's Encounter settings choose a song, its delay, and the boss model. These changes participate in Save, Undo/Redo, and unsaved-changes prompts. The song plays once, starting at its delay, and continues across event timeline loops. Editor playback follows play/pause and scrubbing. Restarting the encounter restarts its song delay. Master Volume affects encounter music.
