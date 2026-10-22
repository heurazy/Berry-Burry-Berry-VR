using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using Valve.VR;

namespace BerryVR
{
    [StructLayout(LayoutKind.Explicit, Size = 16 + 4 * 21)]
    public struct BerryVRControllerState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('B', 'V', 'R', 'C');

        [FieldOffset(0)] [InputControl(name = "leftStick", layout = "Vector2")] public Vector2 leftStick;
        [FieldOffset(8)] [InputControl(name = "rightStick", layout = "Vector2")] public Vector2 rightStick;

        [FieldOffset(16)] [InputControl(name = "jump", layout = "Button")] public float jump;
        [FieldOffset(20)] [InputControl(name = "sprint", layout = "Button")] public float sprint;
        [FieldOffset(24)] [InputControl(name = "crouch", layout = "Button")] public float crouch;
        [FieldOffset(28)] [InputControl(name = "throw", layout = "Button")] public float throwBtn;
        [FieldOffset(32)] [InputControl(name = "pickup", layout = "Button")] public float pickup;
        [FieldOffset(36)] [InputControl(name = "drop", layout = "Button")] public float drop;
        [FieldOffset(40)] [InputControl(name = "airblast", layout = "Button")] public float airblast;
        [FieldOffset(44)] [InputControl(name = "shop", layout = "Button")] public float shop;
        [FieldOffset(48)] [InputControl(name = "movehole", layout = "Button")] public float movehole;
        [FieldOffset(52)] [InputControl(name = "menu", layout = "Button")] public float menu;
        [FieldOffset(56)] [InputControl(name = "blitz", layout = "Button")] public float blitz;
        [FieldOffset(60)] [InputControl(name = "maw", layout = "Button")] public float maw;
        [FieldOffset(64)] [InputControl(name = "rotate", layout = "Button")] public float rotate;
        [FieldOffset(68)] [InputControl(name = "destroy", layout = "Button")] public float destroy;
        [FieldOffset(72)] [InputControl(name = "recenter", layout = "Button")] public float recenter;
        [FieldOffset(76)] [InputControl(name = "tracked", layout = "Button")] public float tracked;
        [FieldOffset(80)] [InputControl(name = "leftGripValue", layout = "Axis")] public float leftGripValue;
        [FieldOffset(84)] [InputControl(name = "rightGripValue", layout = "Axis")] public float rightGripValue;
        [FieldOffset(88)] [InputControl(name = "leftTriggerValue", layout = "Axis")] public float leftTriggerValue;
        [FieldOffset(92)] [InputControl(name = "rightTriggerValue", layout = "Axis")] public float rightTriggerValue;
        [FieldOffset(96)] [InputControl(name = "scroll", layout = "Axis")] public float scroll;
    }

    [InputControlLayout(stateType = typeof(BerryVRControllerState), displayName = "Berry VR Controller")]
    public class BerryVRController : InputDevice
    {
        public Vector2Control leftStick { get; private set; }
        public Vector2Control rightStick { get; private set; }
        public ButtonControl jump { get; private set; }
        public ButtonControl sprint { get; private set; }
        public ButtonControl crouch { get; private set; }
        public ButtonControl throwBtn { get; private set; }
        public ButtonControl pickup { get; private set; }
        public ButtonControl drop { get; private set; }
        public ButtonControl airblast { get; private set; }
        public ButtonControl shop { get; private set; }
        public ButtonControl movehole { get; private set; }
        public ButtonControl menu { get; private set; }
        public ButtonControl blitz { get; private set; }
        public ButtonControl maw { get; private set; }
        public ButtonControl rotate { get; private set; }
        public ButtonControl destroy { get; private set; }
        public ButtonControl recenter { get; private set; }
        public AxisControl leftGripValue { get; private set; }
        public AxisControl rightGripValue { get; private set; }
        public AxisControl leftTriggerValue { get; private set; }
        public AxisControl rightTriggerValue { get; private set; }
        public AxisControl scroll { get; private set; }

        protected override void FinishSetup()
        {
            leftStick = GetChildControl<Vector2Control>("leftStick");
            rightStick = GetChildControl<Vector2Control>("rightStick");
            jump = GetChildControl<ButtonControl>("jump");
            sprint = GetChildControl<ButtonControl>("sprint");
            crouch = GetChildControl<ButtonControl>("crouch");
            throwBtn = GetChildControl<ButtonControl>("throw");
            pickup = GetChildControl<ButtonControl>("pickup");
            drop = GetChildControl<ButtonControl>("drop");
            airblast = GetChildControl<ButtonControl>("airblast");
            shop = GetChildControl<ButtonControl>("shop");
            movehole = GetChildControl<ButtonControl>("movehole");
            menu = GetChildControl<ButtonControl>("menu");
            blitz = GetChildControl<ButtonControl>("blitz");
            maw = GetChildControl<ButtonControl>("maw");
            rotate = GetChildControl<ButtonControl>("rotate");
            destroy = GetChildControl<ButtonControl>("destroy");
            recenter = GetChildControl<ButtonControl>("recenter");
            leftGripValue = GetChildControl<AxisControl>("leftGripValue");
            rightGripValue = GetChildControl<AxisControl>("rightGripValue");
            leftTriggerValue = GetChildControl<AxisControl>("leftTriggerValue");
            rightTriggerValue = GetChildControl<AxisControl>("rightTriggerValue");
            scroll = GetChildControl<AxisControl>("scroll");
            base.FinishSetup();
        }
    }

    /// <summary>
    /// SteamVR hand skeleton (31 bones, model space relative to <see cref="root"/>).
    /// </summary>
    public sealed class HandSkeleton
    {
        public const int BoneCount = 31;
        public const int Wrist = 1;
        // First bone of each finger chain; each finger has 5 bones
        // (metacarpal .. tip) except the thumb which has 4.
        public const int Thumb = 2, Index = 6, Middle = 11, Ring = 16, Pinky = 21;

        public bool valid;
        public VRPose root;
        public readonly Vector3[] pos = new Vector3[BoneCount];
        public readonly Quaternion[] rot = new Quaternion[BoneCount];
    }

    public struct VRPose
    {
        public bool valid;
        public Vector3 pos;
        public Quaternion rot;

        public static VRPose FromMatrix(HmdMatrix34_t m)
        {
            // Same conversion Valve uses in SteamVR_Utils.RigidTransform:
            // flip the Z axis (OpenVR is right handed with -Z forward).
            Vector3 p = new Vector3(m.m3, m.m7, -m.m11);
            Quaternion q;
            float m00 = m.m0, m01 = m.m1, m02 = -m.m2;
            float m10 = m.m4, m11 = m.m5, m12 = -m.m6;
            float m20 = -m.m8, m21 = -m.m9, m22 = m.m10;
            float tr = m00 + m11 + m22;
            if (tr > 0f)
            {
                float s = Mathf.Sqrt(tr + 1f) * 2f;
                q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return new VRPose { valid = true, pos = p, rot = q };
        }
    }

    /// <summary>
    /// Bridges SteamVR input (action based, with a legacy fallback) to a virtual
    /// UnityEngine.InputSystem device. The game itself keeps using its own action
    /// map; we only append bindings to our device.
    /// </summary>
    public class VRInputManager
    {
        public bool ActionMode { get; private set; }
        public bool HasControllers { get; private set; }
        public BerryVRController Device { get; private set; }

        public Vector2 Move { get; private set; }
        public Vector2 Turn { get; private set; }
        public bool MenuTapped { get; private set; }
        public bool RecenterRequested { get; private set; }

        public float LeftGripValue => _gripL;
        public float RightGripValue => _gripR;
        public float LeftTriggerValue => _trigL;
        public float RightTriggerValue => _trigR;
        public bool RightTriggerPressed { get; private set; }
        public bool RightTriggerReleased { get; private set; }
        public bool LeftTriggerPressed { get; private set; }
        public bool LeftTriggerReleased { get; private set; }

        public VRPose HmdPose;
        public VRPose LeftGrip, LeftAim, RightGrip, RightAim;

        /// <summary>SteamVR hand skeletons; invalid without action input.</summary>
        public readonly HandSkeleton LeftHand = new HandSkeleton();
        public readonly HandSkeleton RightHand = new HandSkeleton();

        /// <summary>
        /// Set every frame by the UI layer while the aim ray is over an
        /// interactive panel. New gameplay trigger presses are swallowed so
        /// clicking UI never throws the held object; a trigger that was already
        /// held keeps its state so no spurious press/release edge is emitted.
        /// </summary>
        public bool SuppressGameplayTrigger;

        /// <summary>Value fed to the game's ScrollPC action (PC page up/down).</summary>
        public float ExternalScroll;

        public int LeftDeviceIndex { get; private set; } = -1;
        public int RightDeviceIndex { get; private set; } = -1;

        private CVRSystem _sys;
        private CVRInput _input;
        private string _manifestPath;
        private bool _manifestTried;

        private ulong _setHandle;
        private ulong _actMove, _actTurn, _actJump, _actSprint, _actPickup, _actThrow, _actAirblast;
        private ulong _actDrop, _actShop, _actMoveHole, _actMenu, _actStickL, _actStickR;
        private ulong _poseLeftGrip, _poseLeftAim, _poseRightGrip, _poseRightAim;
        private ulong _skelLeft, _skelRight;
        private ulong _hapticLeft, _hapticRight;
        private readonly VRBoneTransform_t[] _boneBuf = new VRBoneTransform_t[HandSkeleton.BoneCount];

        private InputActions _boundActions;
        private bool _loggedBindingFailure;
        private float _actionInitTime;
        private bool _actionDataSeen;
        private bool _loggedActionInactive;

        // Raw controls
        private Vector2 _move, _turn;
        private float _gripL, _gripR, _trigL, _trigR;
        private bool _btnA, _btnB, _btnX, _btnY, _btnMenu, _btnStickL, _btnStickR;
        private bool _legacyATouched, _legacyBTouched;

        // Edge detection for the raw buttons
        private bool _prevA, _prevB, _prevX, _prevY, _prevMenu, _prevStickR, _prevStickL;
        private float _xPressTime = -1f, _bPressTime = -1f, _menuPressTime = -1f, _yPressTime = -1f;
        private bool _xLongFired, _bLongFired, _menuLongFired, _yLongFired;
        private bool _pulseShop, _pulseBlitz, _pulseMaw, _pulseMenu, _pulseRecenter;

        private float _prevGripL, _prevGripR;

        public void Init(CVRSystem sys, CVRInput input, string manifestPath)
        {
            _sys = sys;
            _input = input;
            _manifestPath = manifestPath;
            TryInitActions();
            EnsureDevice();
        }

        private void EnsureDevice()
        {
            if (Device != null && Device.added)
                return;
            try
            {
                InputSystem.RegisterLayout<BerryVRController>();
                Device = InputSystem.AddDevice<BerryVRController>("BerryVRController");
                BerryVRPlugin.Log.LogInfo("[VR] Virtual input device created: " + (Device != null));
            }
            catch (Exception e)
            {
                BerryVRPlugin.Log.LogError("[VR] Failed to create virtual input device: " + e);
            }
        }

        private void TryInitActions()
        {
            if (_manifestTried) return;
            _manifestTried = true;
            if (_input == null)
            {
                BerryVRPlugin.Log.LogWarning("[VR] IVRInput unavailable, using legacy controller input.");
                return;
            }
            try
            {
                var err = _input.SetActionManifestPath(_manifestPath);
                if (err != EVRInputError.None)
                {
                    BerryVRPlugin.Log.LogWarning("[VR] SetActionManifestPath failed: " + err + " (" + _manifestPath + ")");
                    return;
                }
                if (!GetSet(_setHandle)) return;
                if (!GetAct("/actions/gameplay/in/move", ref _actMove)) return;
                GetAct("/actions/gameplay/in/turn", ref _actTurn);
                GetAct("/actions/gameplay/in/jump", ref _actJump);
                GetAct("/actions/gameplay/in/sprint", ref _actSprint);
                GetAct("/actions/gameplay/in/pickup", ref _actPickup);
                GetAct("/actions/gameplay/in/throw", ref _actThrow);
                GetAct("/actions/gameplay/in/airblast", ref _actAirblast);
                GetAct("/actions/gameplay/in/drop", ref _actDrop);
                GetAct("/actions/gameplay/in/shop", ref _actShop);
                GetAct("/actions/gameplay/in/movehole", ref _actMoveHole);
                GetAct("/actions/gameplay/in/menu", ref _actMenu);
                GetAct("/actions/gameplay/in/stick_left", ref _actStickL);
                GetAct("/actions/gameplay/in/stick_right", ref _actStickR);
                GetAct("/actions/gameplay/in/grip_left", ref _poseLeftGrip);
                GetAct("/actions/gameplay/in/aim_left", ref _poseLeftAim);
                GetAct("/actions/gameplay/in/grip_right", ref _poseRightGrip);
                GetAct("/actions/gameplay/in/aim_right", ref _poseRightAim);
                GetAct("/actions/gameplay/in/skeleton_left", ref _skelLeft);
                GetAct("/actions/gameplay/in/skeleton_right", ref _skelRight);
                GetAct("/actions/gameplay/out/haptic_left", ref _hapticLeft);
                GetAct("/actions/gameplay/out/haptic_right", ref _hapticRight);
                ActionMode = true;
                _actionInitTime = Time.unscaledTime;
                BerryVRPlugin.Log.LogInfo("[VR] SteamVR action input ready.");
            }
            catch (Exception e)
            {
                BerryVRPlugin.Log.LogWarning("[VR] Action input init exception: " + e.Message);
            }
        }

        private bool GetSet(ulong handle)
        {
            ulong h = 0;
            var err = _input.GetActionSetHandle("/actions/gameplay", ref h);
            if (err != EVRInputError.None || h == 0)
            {
                BerryVRPlugin.Log.LogWarning("[VR] GetActionSetHandle failed: " + err);
                return false;
            }
            _setHandle = h;
            return true;
        }

        private bool GetAct(string name, ref ulong handle)
        {
            ulong h = 0;
            var err = _input.GetActionHandle(name, ref h);
            if (err != EVRInputError.None || h == 0)
            {
                BerryVRPlugin.Log.LogWarning("[VR] GetActionHandle failed for " + name + ": " + err);
                return false;
            }
            handle = h;
            return true;
        }

        public void BindGameActions(InputActions actions)
        {
            if (actions == null || _boundActions == actions)
                return;
            try
            {
                var map = actions.PlayerActionMap.Get();
                bool wasEnabled = map.enabled;
                if (wasEnabled) map.Disable();
                Bind(map, "Movement", "<BerryVRController>/leftStick");
                Bind(map, "Jump", "<BerryVRController>/jump");
                Bind(map, "Sprint", "<BerryVRController>/sprint");
                Bind(map, "Crouch", "<BerryVRController>/crouch");
                Bind(map, "Throw", "<BerryVRController>/throw");
                Bind(map, "PickUpAndUse", "<BerryVRController>/pickup");
                Bind(map, "Drop", "<BerryVRController>/drop");
                Bind(map, "AirBlast", "<BerryVRController>/airblast");
                Bind(map, "Shop", "<BerryVRController>/shop");
                Bind(map, "MoveHole", "<BerryVRController>/movehole");
                Bind(map, "Escape", "<BerryVRController>/menu");
                Bind(map, "BerryBlitz", "<BerryVRController>/blitz");
                Bind(map, "GapingMaw", "<BerryVRController>/maw");
                Bind(map, "Rotate", "<BerryVRController>/rotate");
                Bind(map, "DestroyBuildable", "<BerryVRController>/destroy");
                Bind(map, "ScrollPC", "<BerryVRController>/scroll");
                if (wasEnabled) map.Enable();
                _boundActions = actions;
                BerryVRPlugin.Log.LogInfo("[VR] Game input actions bound to BerryVRController.");
            }
            catch (Exception e)
            {
                if (!_loggedBindingFailure)
                {
                    _loggedBindingFailure = true;
                    BerryVRPlugin.Log.LogError("[VR] Failed to bind game actions: " + e);
                }
            }
        }

        private static void Bind(InputActionMap map, string action, string path)
        {
            var a = map.FindAction(action, false);
            if (a == null) return;
            a.AddBinding(path);
        }

        public void Update(float dt, TrackedDevicePose_t[] devicePoses)
        {
            EnsureDevice();

            _prevA = _btnA; _prevB = _btnB; _prevX = _btnX; _prevY = _btnY;
            _prevMenu = _btnMenu; _prevStickR = _btnStickR; _prevStickL = _btnStickL;
            _prevGripL = _gripL; _prevGripR = _gripR;
            _pulseShop = _pulseBlitz = _pulseMaw = _pulseMenu = false;
            RecenterRequested = false;

            // Always read the legacy controller state first: it provides a
            // complete baseline (and valid button/pose fallbacks) if SteamVR's
            // action bindings are missing or a driver only exposes the old
            // controller API. Action data overrides it channel by channel.
            ReadLegacy(devicePoses);
            if (ActionMode)
                ReadActions(devicePoses);

            if (ActionMode && !_actionDataSeen && !_loggedActionInactive &&
                Time.unscaledTime - _actionInitTime > 4f)
            {
                _loggedActionInactive = true;
                BerryVRPlugin.Log.LogWarning(
                    "[VR] SteamVR actions are not reporting data (bindings not loaded?); legacy controller input is in use.");
            }

            HasControllers = RightGrip.valid || RightAim.valid;
            Move = _move;
            Turn = _turn;

            // ---- synthesize the virtual device state ----
            bool buildMode = false;
            try { buildMode = GameManager.Singleton != null && GameManager.Singleton.buildModeState == PlayerBuildModeState.BuildMode; }
            catch { }

            bool gripLHeld = _gripL > 0.55f;
            bool gripRHeld = _gripR > 0.55f;
            bool trigLHeld = _trigL > 0.55f;
            bool trigRHeld = _trigR > 0.55f;

            // While the aim ray is over an interactive UI panel, new gameplay
            // trigger presses are swallowed so clicking UI never throws the
            // held item. A trigger that was already held when the hover began
            // keeps its channel, so no spurious press/release edge is emitted.
            bool gameplayTrigger;
            if (SuppressGameplayTrigger && !_lastGameplayTrigger)
                gameplayTrigger = false;
            else
                gameplayTrigger = trigRHeld;
            _lastGameplayTrigger = gameplayTrigger;

            RightTriggerPressed = _trigR > 0.55f && _prevTrigR <= 0.55f;
            RightTriggerReleased = _trigR <= 0.55f && _prevTrigR > 0.55f;
            LeftTriggerPressed = _trigL > 0.55f && _prevTrigL <= 0.55f;
            LeftTriggerReleased = _trigL <= 0.55f && _prevTrigL > 0.55f;
            _prevTrigL = _trigL; _prevTrigR = _trigR;

            bool aPressed = _btnA && !_prevA;
            bool bPressed = _btnB && !_prevB;
            bool xPressed = _btnX && !_prevX;
            bool yPressed = _btnY && !_prevY;
            bool stickRPressed = _btnStickR && !_prevStickR;
            bool stickLPressed = _btnStickL && !_prevStickL;

            // Left stick click: Berry Blitz ("Fruits en folie"). Crouching is
            // done by physically crouching in VR.
            if (stickLPressed)
                _pulseBlitz = true;
            bool menuPressed = _btnMenu && !_prevMenu;

            // Left X: tap = shop, hold = Berry Blitz.
            if (xPressed)
            {
                _xPressTime = Time.unscaledTime;
                _xLongFired = false;
            }
            if (_btnX && _xPressTime >= 0f && !_xLongFired && Time.unscaledTime - _xPressTime > 0.6f)
            {
                _xLongFired = true;
                _pulseBlitz = true;
            }
            if (!_btnX && _xPressTime >= 0f)
            {
                if (!_xLongFired)
                    _pulseShop = true;
                _xPressTime = -1f;
            }

            // Right B: press = drop, hold = Gaping Maw.
            if (bPressed)
            {
                _bPressTime = Time.unscaledTime;
                _bLongFired = false;
            }
            if (_btnB && _bPressTime >= 0f && !_bLongFired && Time.unscaledTime - _bPressTime > 0.65f)
            {
                _bLongFired = true;
                if (!buildMode) _pulseMaw = true;
            }
            if (!_btnB) _bPressTime = -1f;

            // Right stick click: rotate in build mode, otherwise the Gaping Maw.
            if (stickRPressed && !buildMode)
                _pulseMaw = true;

            // Left Y: tap = pause menu, hold = move the hole. This gives a
            // pause button that works even when SteamVR reserves the menu
            // button. MoveHole only engages after the hold delay so a tap can
            // never move the hole by accident.
            if (yPressed)
            {
                _yPressTime = Time.unscaledTime;
                _yLongFired = false;
            }
            if (_btnY && _yPressTime >= 0f && !_yLongFired && Time.unscaledTime - _yPressTime > 0.35f)
                _yLongFired = true;
            if (!_btnY && _yPressTime >= 0f)
            {
                if (!_yLongFired) _pulseMenu = true;
                _yPressTime = -1f;
            }

            bool moveHoleHeld = _btnY && _yLongFired;

            // Menu: tap = pause, hold = recenter.
            if (menuPressed)
            {
                _menuPressTime = Time.unscaledTime;
                _menuLongFired = false;
            }
            if (_btnMenu && _menuPressTime >= 0f && !_menuLongFired && Time.unscaledTime - _menuPressTime > 0.8f)
            {
                _menuLongFired = true;
                RecenterRequested = true;
                _pulseRecenter = true;
            }
            if (!_btnMenu && _menuPressTime >= 0f)
            {
                if (!_menuLongFired) _pulseMenu = true;
                _menuPressTime = -1f;
            }

            if (Device == null)
                return;

            var s = new BerryVRControllerState
            {
                leftStick = _move,
                rightStick = _turn,
                jump = _btnA ? 1f : 0f,
                sprint = gripLHeld ? 1f : 0f,
                crouch = 0f,
                throwBtn = gameplayTrigger ? 1f : 0f,
                pickup = gripRHeld ? 1f : 0f,
                drop = (_btnB || bPressed) ? 1f : 0f,
                airblast = trigLHeld ? 1f : 0f,
                shop = _pulseShop ? 1f : 0f,
                movehole = moveHoleHeld ? 1f : 0f,
                menu = _pulseMenu ? 1f : 0f,
                blitz = _pulseBlitz ? 1f : 0f,
                maw = _pulseMaw ? 1f : 0f,
                rotate = (buildMode && stickRPressed) ? 1f : 0f,
                destroy = (buildMode && (_btnB || bPressed)) ? 1f : 0f,
                recenter = _pulseRecenter ? 1f : 0f,
                tracked = HasControllers ? 1f : 0f,
                leftGripValue = _gripL,
                rightGripValue = _gripR,
                leftTriggerValue = _trigL,
                rightTriggerValue = _trigR,
                scroll = ExternalScroll
            };

            // --- synthetic one-frame buttons ---
            // Because the game reads WasPressedThisFrame, a single frame pulse is
            // enough. InputState.Change applies it immediately.
            try
            {
                InputSystem.QueueStateEvent(Device, s);
            }
            catch (Exception e)
            {
                if (!_loggedBindingFailure)
                {
                    _loggedBindingFailure = true;
                    BerryVRPlugin.Log.LogError("[VR] QueueStateEvent failed: " + e);
                }
            }
        }
        private float _prevTrigL, _prevTrigR;
        private bool _lastGameplayTrigger;

        private void ReadActions(TrackedDevicePose_t[] devicePoses)
        {
            try
            {
                var set = new VRActiveActionSet_t
                {
                    ulActionSet = _setHandle,
                    ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle,
                    ulSecondaryActionSet = 0,
                    unPadding = 0,
                    nPriority = 0
                };
                _input.UpdateActionState(new[] { set }, (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t)));

                bool any = false;
                if (TryReadAnalog(_actMove, out var move)) { _move = move; any = true; }
                if (TryReadAnalog(_actTurn, out var turn)) { _turn = turn; any = true; }
                if (TryReadAnalog1(_actSprint, out var gripL)) { _gripL = gripL; any = true; }
                if (TryReadAnalog1(_actPickup, out var gripR)) { _gripR = gripR; any = true; }
                if (TryReadAnalog1(_actAirblast, out var trigL)) { _trigL = trigL; any = true; }
                if (TryReadAnalog1(_actThrow, out var trigR)) { _trigR = trigR; any = true; }
                if (TryReadDigital(_actJump, out var jump)) { _btnA = jump; any = true; }
                if (TryReadDigital(_actDrop, out var drop)) { _btnB = drop; any = true; }
                if (TryReadDigital(_actShop, out var shop)) { _btnX = shop; any = true; }
                if (TryReadDigital(_actMoveHole, out var moveHole)) { _btnY = moveHole; any = true; }
                if (TryReadDigital(_actMenu, out var menu)) { _btnMenu = menu; any = true; }

                if (TryReadDigital(_actStickL, out var stickL)) { _btnStickL = stickL; any = true; }
                if (TryReadDigital(_actStickR, out var stickR)) { _btnStickR = stickR; any = true; }

                // Pose actions override the legacy render poses, but a missing
                // pose action keeps the render pose read by ReadLegacy.
                var lg = ReadPose(_poseLeftGrip, devicePoses);
                if (lg.valid) { LeftGrip = lg; any = true; }
                var la = ReadPose(_poseLeftAim, devicePoses);
                if (la.valid) { LeftAim = la; any = true; }
                var rg = ReadPose(_poseRightGrip, devicePoses);
                if (rg.valid) { RightGrip = rg; any = true; }
                var ra = ReadPose(_poseRightAim, devicePoses);
                if (ra.valid) { RightAim = ra; any = true; }

                ReadSkeleton(_skelLeft, LeftHand);
                ReadSkeleton(_skelRight, RightHand);

                if (any && !_actionDataSeen)
                    BerryVRPlugin.Log.LogInfo("[VR] SteamVR action input is reporting data" +
                        (LeftHand.valid || RightHand.valid ? " (hand skeletons active)." : "."));
                if (any) _actionDataSeen = true;

                // If an aim pose is missing, reuse the grip pose. If a grip
                // pose is missing, reuse the aim pose. This keeps the hands and
                // the aim ray usable even on drivers with partial pose support.
                if (!RightGrip.valid) RightGrip = RightAim;
                if (!RightAim.valid) RightAim = RightGrip;
                if (!LeftGrip.valid) LeftGrip = LeftAim;
                if (!LeftAim.valid) LeftAim = LeftGrip;
            }
            catch (Exception e)
            {
                BerryVRPlugin.Log.LogWarning("[VR] Action read failed, switching to legacy: " + e.Message);
                ActionMode = false;
                ReadLegacy(devicePoses);
            }
        }

        private bool TryReadAnalog(ulong h, out Vector2 value)
        {
            value = Vector2.zero;
            if (h == 0) return false;
            var d = new InputAnalogActionData_t();
            var err = _input.GetAnalogActionData(h, ref d, (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t)), 0);
            if (err != EVRInputError.None || !d.bActive) return false;
            value = new Vector2(d.x, d.y);
            if (value.magnitude < 0.18f) value = Vector2.zero;
            else if (value.magnitude > 1f) value.Normalize();
            return true;
        }

        private void ReadSkeleton(ulong h, HandSkeleton hand)
        {
            hand.valid = false;
            if (h == 0) return;

            var d = new InputPoseActionData_t();
            var err = _input.GetPoseActionDataRelativeToNow(h, ETrackingUniverseOrigin.TrackingUniverseStanding, 0f,
                ref d, (uint)Marshal.SizeOf(typeof(InputPoseActionData_t)), 0);
            if (err != EVRInputError.None || !d.bActive || !d.pose.bPoseIsValid) return;

            // Without the controller: the hand closes into a natural fist on
            // grip instead of wrapping an invisible controller.
            err = _input.GetSkeletalBoneData(h, EVRSkeletalTransformSpace.Model,
                EVRSkeletalMotionRange.WithoutController, _boneBuf);
            if (err != EVRInputError.None) return;

            hand.root = VRPose.FromMatrix(d.pose.mDeviceToAbsoluteTracking);
            for (int i = 0; i < HandSkeleton.BoneCount; i++)
            {
                // Same Z mirror as the tracked poses so bones and root agree.
                var b = _boneBuf[i];
                hand.pos[i] = new Vector3(b.position.v0, b.position.v1, -b.position.v2);
                hand.rot[i] = new Quaternion(-b.orientation.x, -b.orientation.y, b.orientation.z, b.orientation.w);
            }
            hand.valid = true;
        }

        private bool TryReadAnalog1(ulong h, out float value)
        {
            value = 0f;
            if (h == 0) return false;
            var d = new InputAnalogActionData_t();
            var err = _input.GetAnalogActionData(h, ref d, (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t)), 0);
            if (err != EVRInputError.None || !d.bActive) return false;
            value = Mathf.Clamp01(d.x);
            return true;
        }

        private bool TryReadDigital(ulong h, out bool value)
        {
            value = false;
            if (h == 0) return false;
            var d = new InputDigitalActionData_t();
            var err = _input.GetDigitalActionData(h, ref d, (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t)), 0);
            if (err != EVRInputError.None || !d.bActive) return false;
            value = d.bState;
            return true;
        }

        private VRPose ReadPose(ulong h, TrackedDevicePose_t[] devicePoses)
        {
            if (h != 0)
            {
                var d = new InputPoseActionData_t();
                var err = _input.GetPoseActionDataRelativeToNow(h, ETrackingUniverseOrigin.TrackingUniverseStanding, 0f,
                    ref d, (uint)Marshal.SizeOf(typeof(InputPoseActionData_t)), 0);
                if (err == EVRInputError.None && d.bActive && d.pose.bPoseIsValid)
                    return VRPose.FromMatrix(d.pose.mDeviceToAbsoluteTracking);
            }
            return default;
        }

        private void ReadLegacy(TrackedDevicePose_t[] devicePoses)
        {
            _move = Vector2.zero;
            _turn = Vector2.zero;
            _gripL = _gripR = _trigL = _trigR = 0f;
            _btnA = _btnB = _btnX = _btnY = _btnMenu = false;
            _btnStickL = _btnStickR = false;
            LeftGrip = LeftAim = default;
            LeftHand.valid = RightHand.valid = false;
            RightGrip = RightAim = default;

            if (_sys == null) return;
            UpdateIndices();

            ReadLegacyController(LeftDeviceIndex, true, devicePoses);
            ReadLegacyController(RightDeviceIndex, false, devicePoses);

            if (LeftDeviceIndex >= 0 && devicePoses != null && LeftDeviceIndex < devicePoses.Length && devicePoses[LeftDeviceIndex].bPoseIsValid)
                LeftGrip = LeftAim = VRPose.FromMatrix(devicePoses[LeftDeviceIndex].mDeviceToAbsoluteTracking);
            if (RightDeviceIndex >= 0 && devicePoses != null && RightDeviceIndex < devicePoses.Length && devicePoses[RightDeviceIndex].bPoseIsValid)
                RightGrip = RightAim = VRPose.FromMatrix(devicePoses[RightDeviceIndex].mDeviceToAbsoluteTracking);
        }

        private void ReadLegacyController(int idx, bool left, TrackedDevicePose_t[] poses)
        {
            if (idx < 0) return;
            var st = new VRControllerState_t();
            if (!_sys.GetControllerState((uint)idx, ref st, (uint)Marshal.SizeOf(typeof(VRControllerState_t))))
                return;
            float trig = st.rAxis1.x;
            Vector2 stick = new Vector2(st.rAxis0.x, st.rAxis0.y);
            if (stick.magnitude < 0.18f) stick = Vector2.zero;
            bool grip = (st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_Grip)) != 0;
            bool a = (st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_A)) != 0;
            bool menu = (st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_ApplicationMenu)) != 0;
            bool stickClick = (st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Touchpad)) != 0;

            if (left)
            {
                _move = stick;
                _gripL = grip ? 1f : 0f;
                _trigL = trig;
                _btnX = a;             // left A/X = shop
                _btnMenu = menu;       // left menu
                _btnStickL = stickClick;
            }
            else
            {
                _turn = stick;
                _gripR = grip ? 1f : 0f;
                _trigR = trig;
                _btnA = a;             // right A = jump
                _btnB = menu;          // right B / menu = drop
                _btnStickR = stickClick;
            }
        }

        private void UpdateIndices()
        {
            if (_sys == null) return;
            try
            {
                if (LeftDeviceIndex < 0 || !_sys.IsTrackedDeviceConnected((uint)LeftDeviceIndex))
                {
                    var i = (int)_sys.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
                    if (i > 0) LeftDeviceIndex = i;
                }
                if (RightDeviceIndex < 0 || !_sys.IsTrackedDeviceConnected((uint)RightDeviceIndex))
                {
                    var i = (int)_sys.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
                    if (i > 0) RightDeviceIndex = i;
                }
                // Some drivers don't assign roles. Fall back to the first two controllers.
                if (LeftDeviceIndex < 0 || RightDeviceIndex < 0)
                {
                    for (uint i = 1; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
                    {
                        if (_sys.GetTrackedDeviceClass(i) != ETrackedDeviceClass.Controller) continue;
                        if (LeftDeviceIndex < 0) { LeftDeviceIndex = (int)i; continue; }
                        if (RightDeviceIndex < 0) { RightDeviceIndex = (int)i; }
                    }
                }
            }
            catch { }
        }

        public void PulseRight(ushort durationMicros = 1000)
        {
            Pulse(_hapticRight, false, durationMicros);
        }

        public void PulseLeft(ushort durationMicros = 1000)
        {
            Pulse(_hapticLeft, true, durationMicros);
        }

        /// <summary>
        /// Vibrates one controller. Callers pass a legacy pulse length (about
        /// 0.5-3.5 ms); it is mapped to a real vibration through the SteamVR
        /// haptic action. Legacy TriggerHapticPulse is only a fallback: its
        /// sub-millisecond pulses, limited to one per frame, are often dropped
        /// for action-based apps, which is why some vibrations went missing.
        /// </summary>
        private void Pulse(ulong action, bool left, ushort durationMicros)
        {
            if (_sys == null) return;
            if (VRSettings.Haptics != null && !VRSettings.Haptics.Value) return;

            if (ActionMode && action != 0 && _input != null)
            {
                float strength = Mathf.Clamp01(durationMicros / 3500f);
                float seconds = 0.02f + 0.06f * strength;
                float amplitude = Mathf.Lerp(0.3f, 1f, strength);
                try
                {
                    var err = _input.TriggerHapticVibrationAction(action, 0f, seconds, 160f, amplitude,
                        OpenVR.k_ulInvalidInputValueHandle);
                    if (err == EVRInputError.None) return;
                }
                catch { }
            }

            UpdateIndices();
            int idx = left ? LeftDeviceIndex : RightDeviceIndex;
            if (idx < 0) return;
            try { _sys.TriggerHapticPulse((uint)idx, 0, durationMicros); } catch { }
        }
    }
}
