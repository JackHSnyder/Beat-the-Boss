# Encounter timeline editor

## Open and test

Open **Assets/Scenes/BossEditorScene.unity** and enter Play mode, or choose **Boss Editor** from a level's details panel in the main menu. The scene is included in Build Settings. Opening Boss Editor from a level and leaving for Recordings or Main Menu use the shared scene fade transition.

- **Desktop:** a wide timeline below the arena preview and a separate node inspector on the right. Boss view and Arena view switch between close-up and overview cameras; Zoom and Orbit inspect the performance. Space plays/pauses; left/right arrows step by 1/30 second.
- **VR:** use a floating observer and the timeline and details panels beside the arena with the existing Meta ray/poke/gaze controls. BossEditorScene disables the observer's gravity, grounding, and locomotion event connections, and hides the old viewing platform; headset and controller tracking stay active. BossScene keeps its normal locomotion. A separate **View & Navigation** panel on the left contains Boss/Arena view, Closer/Further, Orbit, Reset panels, Recordings, and Main menu. View controls move the VR observer while retaining tracked head movement and a level horizon; panels follow each viewing-position change. Reset panels or right-controller B brings all three panels alongside your current heading. Initial placement waits for tracked headset data and the incoming scene transition to finish, then uses the current eye height. The observer does not act as the target: a separate dummy player stands on the arena circle.

Orbit right moves to the observer's right when facing the center; left moves left. All three world-space panels retain their position and rotation relative to the viewing rig while orbiting or changing viewpoints. **Reset panels / Recenter**, including controller B, shows a three-second countdown before using the headset's then-current position and heading. RecordingScene uses the same countdown. Its **Exit** button returns through the normal fade to the scene that opened Recordings, with the existing unsaved-change confirmation. Opening RecordingScene directly falls back to Main Menu. MainMenuScene and RecordingScene also use FloatingObserver to disable gravity and grounding; BossScene retains normal locomotion.

## Timeline

Click a track at the desired time to select it and position the playhead, then choose **Add node**. Select a node to edit it in the separate details panel. Animation nodes begin unassigned; choose **Saved animation** in their details to select a file. **Behavior** chooses the node mode, and only properties relevant to that mode are shown. Protected recordings in Resources/BuiltInContent take priority over same-named device files. Other device animations take priority over scene fallback assets. **Refresh** reloads changed trims and bone exclusions. The sample PEW PECK was converted from the older recorded take into a standalone animation asset; the boss no longer reads Moves at runtime.

Tracks:

- **Animation:** clips sized to `(saved playback end - saved playback start) / speed`. Trim and included/excluded bones come from the animation saved in RecordingScene. Entrance timing is configurable; exit blends return to idle within the clip duration. Overlapping clips use the most recently started animation; equal-time events use their list order.
- **Attacks:** add a node and choose **Fire orb** for a projectile launch at the playhead. Place it at the firing moment of an animation such as PEW PECK. The orb leaves the Boss Controller's **Orb Launch Point** in the direction the boss is facing. The editor previews the flight as you play or scrub.
- **Spike row** in an attack node starts a red warning strip in the boss's current direction. The marked area stays fixed and extends just beyond the arena circle. Spikes rise after the warning fades and deal one hit if the player is inside during the strike. Edit **Assets/Prefabs/Boss Spike Row.prefab** to tune the warning, dimensions, and damage.
- **Shoot laser** in an attack node draws a narrow red line on the floor in the boss's facing direction, then fires a fast red bolt along that fixed path. The warning brightens before fading. Aim locks when the node starts, so the bolt follows the path the player was shown even if the boss turns afterwards. Previewing and scrubbing show the warning and bolt without applying damage.
  Edit **Assets/Prefabs/Boss Laser.prefab** to change warning time/fade, warning width, projectile speed/range/length/width, collision layers, or damage. The defaults are a 1.2-second warning, 0.1-second fade, 60 m/s speed, 30 m range, and 20 damage. Place the node 1.3 seconds before the desired firing moment of the gun animation. **Laser Launch Point** on the controller sets the muzzle position; both scenes use the existing boss muzzle. Swept collision checks stop at solid obstacles and apply player damage once.
- **Rotation:** Face Angle, Follow Player, and Stop Following are point events. Arena angles follow MoveCircle: 0 is +X and 90 is +Z. Following changes facing, not boss position.
Animation nodes also offer **Change idle**, **Stop idle**, and **Resume idle** behaviors; there is no separate Idle track. **Encounter > Starting idle** assigns the initial idle animation, which otherwise continues underneath masked animation clips.
- **Dummy:** angle points place the dummy on the arena circle for follow-player previews. These authoring-only events do not move the real player in BossScene.

Click a node to select it. Drag to change its start time, or use the Start time controls in its details. Click the ruler or empty track space, or use the seek slider, to jump through the sequence. Turn-to-angle and Dummy nodes expose angle controls in 15-degree increments; Follow Player and Stop Following do not. Play Animation nodes expose speed controls. Duplicate/Delete and Undo/Redo are available. Snap cycles through 0.1, 0.25, 0.5, 1 second, and off. Earlier/Later pan; Zoom +/- changes the visible time range. Stop returns to zero; Pause holds the whole preview. **Encounter** contains looping, minimum length, starting idle, and dummy start angle settings.

**Transition timing** on Face Angle, Play Animation, and Change Idle nodes chooses **Start at node** or **Finish at node**. Set **Transition seconds** to control the turn or blend duration. Start at node begins the transition at the timestamp. Finish at node begins beforehand, reaching the exact angle or first animation frame at the timestamp; animation playback then begins. A thin strip on the track shows the transition interval. Follow and Stop Following do not need these controls. Animation transitions blend from the current pose, including when another clip is interrupted.

Looping repeats the instructions without resetting facing, animation phase, idle/pause/follow state, foot placement, or dummy position. Active projectiles and attack warnings continue across the boundary; attack lifetimes do not extend a looping timeline. A Finish at node transition near the beginning starts in the previous loop's tail. On the first traversal there is no previous tail: it uses the time available, and a node at zero establishes its initial target immediately. Later nodes can interrupt a transition.

Seeking uses the same 60 Hz simulation as playback, with substeps at node boundaries and exact requested timestamps. Manual scrubbing reconstructs the first traversal when moving backward or after looping. Animation state, idle phase, facing, and foot placement are restored together. Scrubbing does not fire gameplay UnityEvents. The first seek far into a long sequence can take longer because it simulates from the beginning.

## Save and use in a fight

The level panel's **New Level** button opens BossEditorScene with a blank, unsaved timeline. **Save** publishes it as a level file in `Application.persistentDataPath/BossTimelines`. **Refresh Levels** scans this folder and rebuilds the list, skipping unreadable files. Protected built-in levels and recordings remain available on a fresh install, and same-named device files cannot replace them. Built-in levels open read-only; new levels remain editable. See ASSET_LIBRARY.md for packaging and Quest details.

Click the timeline's name above the graph, or **Encounter > Name / Rename timeline**, to name or rename it. Desktop typing and an in-panel VR keyboard are supported. Choose Done to apply the draft name and Save to write it. The timeline filename and displayed level name match. Renames preserve the stable level ID, authored slots, and player loadout assignments; a name already used by another file is rejected. New levels copy the example's authored slot definitions, which players cannot change.

**Files** opens saved timelines and can start another blank timeline. Undo/Redo works within the current draft, including name edits. New drafts are considered unsaved even before adding nodes. Leaving for Recordings or Main Menu, loading another file, or starting another timeline prompts before discarding unsaved edits. The confirmation dims and blocks every editor panel. Cancel preserves the draft; Discard continues without writing it. Use Save explicitly to keep changes. Stopping Unity Play mode itself does not save.

Selecting a level for Play or Boss Editor carries that exact timeline into the destination scene. The bundled example uses the shipped Inspector cues. Direct scene starts retain the controller's default timeline selection fallback. Old timeline JSON remains compatible and gains stable level metadata when next saved.

Gameplay uses the real player for Follow Player and skips Dummy track cues. Timeline files reference animation names, so device recordings must accompany a timeline when moving it to another device. Bundled Animations on the controller provide shipped fallbacks. Both scenes include the standalone PEW PECK asset.

Each level's details panel includes **Choose Loadout**. Players assign saved moves to the slots authored for that level. Assignments persist by level ID and slot ID. The example level imports the old global Attack and Dash assignments on first use.

Level selection uses enlarged buttons with full rectangular touch targets. Short lists do not capture scrolling. On longer lists, level-row input tolerates small sideways hand movements and forwards deliberate drags to the scroll view without selecting a level. Opening the loadout keeps the outer panels angled inward toward the player, aligning their inner bottom corners to the middle panel's extended bottom edge with 12 cm gaps at the scene's current scale. Closing it restores the two-panel presentation.

Menu buttons share **Assets/Resources/UI/Menu Button Style.asset**: palette, font, corners, padding, and visual controller. Main Menu, Levels, loadouts, and generated recording/editor buttons keep the UISet components and use this shared appearance. Primary Play actions and editing actions retain distinct palette roles. **Assets/Animations/MenuButtonVisual.controller** animates the Content child so hover/press feedback cannot shrink the transparent full-size hit area. Level-row scroll protection and panel positions are independent of the style.

For bundled levels, create a **Beat the Boss > Level Loadout** asset with a unique stable level ID, title, scene path, and any number of slots. Each slot has a stable ID and display name. An optional gameplay action connects the slot to Attack, Dash Left, or Dash Right. **None** supports custom slots through Move Detection's **Level Slot Move Performed** event and **On Level Slot Performed** Inspector event, which report the slot ID. Keep IDs stable when renaming labels. User-created levels copy the menu's default authored slot definitions into their timeline file and use its stable level ID for assignments.

Assign the same asset to the level's entry in MainMenuScene and to its Move Detection component's **Default Level Loadout**. Entering through **Play Level** selects that level's assignments. The default supports opening the gameplay scene directly. Slot creation, naming, and gameplay actions are authored in the Inspector.

The editor save format supports 120-second event positions/minimum durations, speeds from 0.1x to 4x, and up to 500 events. It saves attack points with the animation, rotation, idle, and dummy cues. Inspector-authored cues can also use UnityEvents.

Edit **Assets/Prefabs/Boss Orb.prefab** to change the projectile's size, appearance, speed, lifetime, or damage. During gameplay, an orb hit calls `EncounterPlayerDamage.TakeDamage` once and despawns the orb. Hook the receiver's **On Damaged** event into other gameplay or UI.

In BossScene, **Encounter Player Damage** exposes **Recovery Delay** and **Recovery Per Second**. Each hit restarts the delay. After that much time without a hit, health regenerates up to its maximum. A broad red halo strengthens as health falls and fades during recovery, with a clear center and an earlier intensity ramp so small hits remain visible. Maximum Vignette Opacity controls its peak strength. At zero health, the game fades back to the main menu.

## Recording idle

Use RecordingScene's **Animations** page to record, trim, and choose the included bones for any idle animation. Choose it under **Encounter > Starting idle**, or use a **Change idle** animation node. The existing **Boss Idle Recorder** Inspector component now also saves to the standalone animation library. No detection Move is required. Without a valid idle recording, unaffected body parts stay in their neutral pose.

## Rigs and feet

BossScene contains **Boss > Stylized Character** with 51 mapped bones. Other rigs use BossRig with explicit universal IDs and neutral-pose transforms. Disable other animation drivers on manually assigned rigs. Calibration assumes the same anatomical neutral posture as the player rig.

Procedural Feet alternates lifted steps, holds the other foot planted, and uses two-bone IK. Step angle, duration, and height are adjustable on Boss Controller. Lower-body animations fade out procedural control. This is a flat-floor turning approximation without terrain probing, collision avoidance, or captured balance motion.

## Verification

The local Unity checks cover standalone animation loading, trim/speed lengths, excluded wrist vs animated finger, reverse seek, dummy point events, and suppressed gameplay callbacks. A Play-mode check covers panel creation, reverse seeking the full mapped character and procedural feet, and play/pause. Run the playback checks through **Tools > Beat the Boss > Check timeline playback**, or the scene check through **Check editor scene in Play mode**. The scene check opens BossEditorScene, runs briefly, captures overview and close-up previews in Temp, and leaves Play mode. Headset interaction and comfort require an in-device test.
