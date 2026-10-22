BERRY BURY BERRY VR - USER GUIDE
================================

BEFORE YOU START
----------------
1. Start SteamVR and connect your headset (Virtual Desktop, Link, ...).
2. Wait until the headset shows the SteamVR home/void.
3. Launch the game normally (Steam or the exe).
   - If OpenVR starts, stereo VR rendering begins automatically.
   - Otherwise the game stays in flat mode (nothing breaks).
4. The log is here: BepInEx\LogOutput.log
   Look for "[VR] OpenVR initialized" to confirm VR mode.

CONTROLS (Oculus Touch / Index / Vive)
--------------------------------------
Right controller
  Grip               : pick up / put down / hold (vacuum)
  Trigger            : throw / use / shoot (pop gun, vacuum) / click UI
  A                  : jump
  B                  : tap = drop the held object | hold 0.65 s = Gaping Maw
  Right stick click  : Gaping Maw (outside build mode)
                       = rotate 90 degrees (in build mode)
  Right stick        : turn (in game), smooth or snap depending on settings

Left controller
  Grip               : sprint
  Trigger            : Air Blast
  X                  : tap = Shop | hold 0.6 s = Berry Blitz
  Y                  : tap = pause | hold 0.35 s = Move Hole
  Menu (system)      : tap = pause | hold 0.8 s = recenter
  Left stick         : move
  Left stick click   : Berry Blitz. To crouch, physically crouch.

Keyboard
  F1                 : recenter (fallback if SteamVR captures the menu button)

UI
--
- In game, point the right controller at the PC screen (or any interactive
  3D UI): the cyan laser appears and the trigger clicks. The right stick
  scrolls the PC pages.
- In menus (pause, options, shop), a cyan laser follows the right controller:
  trigger = click, right stick up/down = scroll lists.
- If the controllers are not tracked, the laser falls back to head gaze.

TROUBLESHOOTING
---------------
- No stereo rendering: check for "OpenVR initialized" in the log.
- Odd position / facing the wrong way: press F1 or hold the menu button.
- SteamVR captures the menu button: use Y (tap) to pause and F1 to recenter.
- The held object drops while a menu is open: expected, game input is off
  in menus.
- Low performance: the game renders two views; lower the in-game quality.

SETTINGS (BepInEx\config\berryvr.plugin.cfg, created on first launch)
---------------------------------------------------------------------
[Comfort]  TurnMode = Smooth | Snap, SmoothTurnSpeed, SnapTurnAngle
           Vignette = false by default (true = darken edges while moving)
           VignetteStrength (0-1)
           HeightOffset (meters, -0.6 to 0.8): raise/lower your view (seated)
[Feel]     Haptics = true/false: vibrations (grab, release, target, UI)
           FingerTouchUI = true/false: press screens with your index finger
[Visuals]  CartoonGloves = true/false, GloveScale (0.6-1.6)
           GunOffsetRight / GunOffsetUp / GunOffsetForward (cm): pop gun
           position in the right hand
Edit the file while the game is closed, then restart it.

VISUALS AND INTERACTIONS
------------------------
- Cartoon gloves tracked down to each finger (SteamVR hand skeleton). If you
  customized this game's controls in SteamVR, restore the default bindings
  to enable the gloves.
- No permanent laser: the object you point at gets the game's selection
  outline.
- Game screens (PC, ...): bring your index finger within a few cm to aim,
  touch the screen to click. The trigger still works from a distance.
- The menu laser starts at the glove's index fingertip.
- Throwing uses the peak speed of your gesture (not a smoothed average), so
  quick throws go harder and straighter.
