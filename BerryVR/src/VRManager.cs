using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Valve.VR;

namespace BerryVR
{
    /// <summary>
    /// Owns the OpenVR session, the two eye cameras and the tracked hand proxies.
    /// Everything here is a no-op when OpenVR cannot be initialized, so the flat
    /// game keeps working exactly as before.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class VRManager : MonoBehaviour
    {
        public static VRManager Instance { get; private set; }

        public bool Active { get; private set; }
        public VRInputManager Input { get; private set; }
        public Camera LeftCamera { get; private set; }
        public Camera RightCamera { get; private set; }

        public Transform Head { get; private set; }
        public Transform LeftGrip { get; private set; }
        public Transform LeftAim { get; private set; }
        public Transform RightGrip { get; private set; }
        public Transform RightAim { get; private set; }

        public Vector3 RightGripVelocity { get; private set; }

        /// <summary>Right index fingertip from the glove skeleton, when drawn.</summary>
        public bool RightIndexTipValid { get; private set; }
        public Vector3 RightIndexTip { get; private set; }
        public Vector3 RightFistCenter { get; private set; }
        public Vector3 RightGripAngularVelocity { get; private set; }
        public bool ControllersTracked => Input != null && Input.HasControllers;

        public VRUI UI { get; private set; }

        private CVRSystem _system;
        private CVRCompositor _compositor;
        private CVRInput _vrInput;

        private RenderTexture _leftRT;
        private RenderTexture _rightRT;
        private IntPtr _leftNativeTex;
        private IntPtr _rightNativeTex;
        private uint _rtWidth, _rtHeight;

        private readonly TrackedDevicePose_t[] _renderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly TrackedDevicePose_t[] _gamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private VRPose _hmd = new VRPose { valid = true, rot = Quaternion.identity };
        private VRPose _eyeL = new VRPose { valid = true, rot = Quaternion.identity };
        private VRPose _eyeR = new VRPose { valid = true, rot = Quaternion.identity };

        private Vector3 _eyeLPos;
        private Vector3 _eyeRPos;
        private Quaternion _eyeLRot = Quaternion.identity;
        private Quaternion _eyeRRot = Quaternion.identity;
        private Matrix4x4 _eyeLProj = Matrix4x4.identity;
        private Matrix4x4 _eyeRProj = Matrix4x4.identity;
        private bool _eyePosesReady;

        private GameObject _rightCamGo;
        private GameObject _rightHandModel;
        private GameObject _leftHandModel;
        private CartoonGlove _rightGlove;
        private CartoonGlove _leftGlove;
        private bool _glovesTried;
        private float _retryAt;
        private bool _initFailedLogged;
        private bool _attemptedInit;
        private bool _submittedThisFrame;

        // Temporary isolation switches used while diagnosing the first-frame
        // GPU deadlock. They are read from the environment so the same build can
        // be launched in several configurations without rebuilding.
        private bool _noStereo;
        private bool _noSubmit;
        private bool _noMirror;
        private bool _noPoses;
        private bool _deferSubmit;
        private bool _pendingSubmit;
        private int _nativeLogCounter;
        private float _nextNativeLog;
        private int _hmdTargetFps = -1;
        private int _endRenderLogs;
        private float _nextRtRefresh;
        private int _poseLogs;

        private static bool EnvFlag(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }
        private bool _submitErrorLogged;

        private string _sceneName = "";
        private bool _gameScene;
        private bool _setupRunning;

        private Transform _player;
        private Collider _playerCollider;
        private Rigidbody _playerRb;
        private ComfortVignette _vignette;
        private float _playerPivotHeight = 1f;
        private GameObject _playerCam;      // PlayerCam FPS (Cinemachine camera, used by gameplay)
        private PlayerGrabber _grabber;

        private Vector3 _origin;
        private float _baseYaw;
        private float _homeYaw;
        private float _snapYaw;
        private float _snapCooldown;
        private bool _snapTurnEnv;
        private bool _loggedTurn;
        private float _turnActivity; // 0..1, smooth turn rate this frame (comfort vignette)
        private Vector3 _posOffset;

        /// <summary>Recentering offset plus the player's height setting.</summary>
        private Vector3 RigOffset => _posOffset +
            Vector3.up * (VRSettings.HeightOffset != null ? VRSettings.HeightOffset.Value : 0f);

        // Short history of right hand samples for throwing.
        private const int ThrowSamples = 24;
        private const float ThrowWindow = 0.12f;
        private readonly Vector3[] _throwPos = new Vector3[ThrowSamples];
        private readonly float[] _throwTime = new float[ThrowSamples];
        private int _throwHead;
        private int _throwCount;

        /// <summary>
        /// Release velocity for throws: the fastest instant of the last
        /// ~0.12 s averaged with its neighbours. A smoothed velocity lags the
        /// gesture and loses most of a quick flick.
        /// </summary>
        public Vector3 RightThrowVelocity
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                int best = -1;
                float bestSpeed = -1f;
                var vel = new Vector3[ThrowSamples];
                int n = 0;
                for (int k = 0; k < _throwCount - 1; k++)
                {
                    int i = (_throwHead - 1 - k + ThrowSamples) % ThrowSamples;
                    int j = (i - 1 + ThrowSamples) % ThrowSamples;
                    if (now - _throwTime[j] > ThrowWindow) break;
                    float dt = _throwTime[i] - _throwTime[j];
                    if (dt <= 1e-4f) continue;
                    vel[n] = (_throwPos[i] - _throwPos[j]) / dt;
                    float sp = vel[n].sqrMagnitude;
                    if (sp > bestSpeed) { bestSpeed = sp; best = n; }
                    n++;
                }
                if (best < 0) return RightGripVelocity;

                Vector3 sum = Vector3.zero;
                int c = 0;
                for (int k = Mathf.Max(0, best - 1); k <= Mathf.Min(n - 1, best + 1); k++)
                {
                    sum += vel[k];
                    c++;
                }
                Vector3 v = sum / c;
                return v.sqrMagnitude > 400f ? v.normalized * 20f : v;
            }
        }

        private Quaternion _rigYaw = Quaternion.identity;
        private Vector3 _headPos;
        private Quaternion _headRot = Quaternion.identity;

        private LineRenderer _aimLine;
        private GameObject _aimDot;
        private Renderer _aimDotRenderer;
        private Material _aimLineMat;
        private Material _aimDotMat;
        private bool _aimBuilt;
        private readonly RaycastHit[] _aimHits = new RaycastHit[8];
        private float _nextPerfCheck;

        private Vector3 _lastRightGripPos;
        private Quaternion _lastRightGripRot = Quaternion.identity;
        private float _lastHandSampleTime;
        private bool _haveHandSample;

        private object _lastLeftDevice = null;

        private static readonly FieldInfo GrabberPlayerCamField =
            typeof(PlayerGrabber).GetField("playerCam", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GrabberHeldField =
            typeof(PlayerGrabber).GetField("heldRigidBody", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo[] GrabberTargetFields =
        {
            typeof(PlayerGrabber).GetField("currentLookingAtPickup", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(PlayerGrabber).GetField("currentLookingAtWorldUsable", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(PlayerGrabber).GetField("currentLookingAtSuckUppable", BindingFlags.Instance | BindingFlags.NonPublic)
        };

        private GameObject _highlighted;
        private Material _highlightMat;
        private readonly List<Renderer> _highlightRenderers = new List<Renderer>();
        private readonly List<Transform> _attachedTools = new List<Transform>();

        private static readonly Vector3 GunLocalPos = new Vector3(0f, -0.005f, 0.085f);
        private static readonly FieldInfo GrabberGunArtField =
            typeof(PlayerGrabber).GetField("popgun_Art_Gun", BindingFlags.Instance | BindingFlags.NonPublic);
        private Transform _gunParent;
        private bool _gunWasVisible;
        private int _gunCaptures;
        private readonly List<CameraFollowLag> _followLags = new List<CameraFollowLag>();
        private bool _loggedFollowLag;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;

            _noStereo = EnvFlag("BERRYVR_NO_STEREO");
            _noSubmit = EnvFlag("BERRYVR_NO_SUBMIT");
            _noMirror = EnvFlag("BERRYVR_NO_MIRROR");
            _noPoses = EnvFlag("BERRYVR_NO_POSES");
            _deferSubmit = EnvFlag("BERRYVR_DEFER_SUBMIT");
            _snapTurnEnv = EnvFlag("BERRYVR_SNAP_TURN");
            if (_noStereo || _noSubmit || _noMirror || _noPoses || _deferSubmit)
                BerryVRPlugin.Log.LogWarning("[VR] Diagnostic mode: noStereo=" + _noStereo +
                    " noSubmit=" + _noSubmit + " noMirror=" + _noMirror + " noPoses=" + _noPoses +
                    " deferSubmit=" + _deferSubmit);

            Head = MakeNode("BerryVR_Head", transform);
            LeftGrip = MakeNode("BerryVR_LeftGrip", transform);
            LeftAim = MakeNode("BerryVR_LeftAim", transform);
            RightGrip = MakeNode("BerryVR_RightGrip", transform);
            RightAim = MakeNode("BerryVR_RightAim", transform);

            Input = new VRInputManager();
            UI = gameObject.AddComponent<VRUI>();

            string pluginDir = Path.GetDirectoryName(typeof(BerryVRPlugin).Assembly.Location) ?? "";
            if (VRNative.Init(pluginDir))
                BerryVRPlugin.Log.LogInfo("[VR] Native render-thread submit bridge loaded.");
            else
                BerryVRPlugin.Log.LogWarning("[VR] Native submit bridge unavailable: " + VRNative.LoadError);
        }

        private static Transform MakeNode(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Application.onBeforeRender += OnBeforeRender;
            TryInit();
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Application.onBeforeRender -= OnBeforeRender;
            if (Active)
            {
                try { OpenVR.Shutdown(); } catch { }
                Active = false;
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (UI != null) UI.OnSceneUnloaded();
            DestroyAttachedTools();
            _player = null;
            _playerCam = null;
            _grabber = null;
            _playerCollider = null;
            LeftCamera = null;
            RightCamera = null;
            _eyePosesReady = false;
            if (_rightCamGo != null)
            {
                Destroy(_rightCamGo);
                _rightCamGo = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sceneName = scene.name;
            _gameScene = scene.name == "Game";
            _submittedThisFrame = false;
            if (Active && !_setupRunning)
                StartCoroutine(SetupSceneRoutine(scene));
        }

        private IEnumerator SetupSceneRoutine(Scene scene)
        {
            _setupRunning = true;
            yield return null;
            yield return null;

            // The scene transition can recreate the D3D11 textures. Refresh
            // the handles before the new stereo cameras start submitting.
            RefreshNativeTextures();

            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Camera.main != null) break;
                yield return null;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                // Some scenes disable the MainCamera tag temporarily while
                // their intro runs. Fall back to the background world camera.
                Camera best = null;
                foreach (var c in Camera.allCameras)
                {
                    if (c == null || !c.isActiveAndEnabled) continue;
                    if (best == null || c.depth < best.depth) best = c;
                }
                cam = best;
            }
            if (cam == null)
            {
                BerryVRPlugin.Log.LogWarning("[VR] No camera in scene " + scene.name);
                _setupRunning = false;
                yield break;
            }

            if (_noStereo)
            {
                BerryVRPlugin.Log.LogInfo("[VR] Diagnostic: stereo camera setup skipped for " + scene.name);
                _setupRunning = false;
                yield break;
            }

            LeftCamera = cam;
            BerryVRPlugin.Log.LogInfo("[VR] Authored camera " + cam.name +
                " pos=" + cam.transform.position.ToString("F3") +
                " rot=" + cam.transform.eulerAngles.ToString("F1") +
                " fov=" + cam.fieldOfView.ToString("F1") +
                " | hmdValid=" + _hmd.valid +
                " hmdPos=" + _hmd.pos.ToString("F3") +
                " hmdRot=" + _hmd.rot.eulerAngles.ToString("F1"));
            PrepareMainCamera(cam);
            EnsureRightCamera(cam);

            // Provisional rig placement from the authored camera. In the Game
            // scene the player does not exist yet while the VCR intro plays;
            // keeping the head at the authored intro viewpoint avoids showing
            // the level from the world origin until the player spawns.
            float hmdYaw = _hmd.valid ? NormalizeAngle(_hmd.rot.eulerAngles.y) : 0f;
            _baseYaw = NormalizeAngle(cam.transform.eulerAngles.y - hmdYaw);
            _homeYaw = NormalizeAngle(_baseYaw + hmdYaw);
            _snapYaw = 0f;
            _posOffset = Vector3.zero;
            Vector3 hpos = _hmd.valid ? _hmd.pos : Vector3.zero;
            _origin = cam.transform.position - Quaternion.Euler(0f, _baseYaw, 0f) * hpos;

            if (UI != null) UI.Refresh(scene.name, cam);

            if (_gameScene)
            {
                // The player prefab is spawned asynchronously by the game scene.
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (Player.Singleton != null && GameManager.Singleton != null &&
                        GameManager.Singleton.playerCamera != null)
                        break;
                    yield return null;
                }

                // Give the game a couple of frames to place the player and its
                // camera at the authored spawn before we read the intended yaw.
                yield return null;
                yield return null;

                AttachPlayer();

                // Start facing the direction the game intended, not the random
                // direction the player happens to be facing in the room.
                hmdYaw = _hmd.valid ? _hmd.rot.eulerAngles.y : 0f;
                if (_playerCam != null)
                    _baseYaw = NormalizeAngle(_playerCam.transform.eulerAngles.y - hmdYaw);
                else
                    _baseYaw = NormalizeAngle(cam.transform.eulerAngles.y - hmdYaw);
                _homeYaw = NormalizeAngle(_baseYaw + hmdYaw);
                _snapYaw = 0f;
                _posOffset = Vector3.zero;

                // Start with the head centered over the player body no matter
                // where the player happens to stand in their tracked space.
                // Physical leaning/walking stays relative to this center.
                Vector3 startHmd = _hmd.valid ? _hmd.pos : Vector3.zero;
                _posOffset = -(Quaternion.Euler(0f, _baseYaw, 0f) *
                               new Vector3(startHmd.x, 0f, startHmd.z));

                UpdateOriginFromPlayer(cam);
            }
            else
            {
                // Menus/cutscenes keep the authored camera position, but let the
                // player lean and look around from it.
                if (UI != null) UI.SnapInFront();
            }

            BerryVRPlugin.Log.LogInfo("[VR] Scene ready: " + scene.name + " camera=" + cam.name + " player=" + (_player != null ? _player.name : "none"));
            VRAudio.SetupListener(cam, scene.name);
            _setupRunning = false;
        }

        private void PrepareMainCamera(Camera cam)
        {
            // Any head-locked/cinematic behaviour would fight our tracked pose.
            foreach (var mb in cam.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                var n = mb.GetType().Name;
                if (n == "CinemachineBrain" || n == "PlayerCamSensHandler" || n == "CameraFollowLag")
                    mb.enabled = false;
            }
            foreach (var br in cam.GetComponents<UnityEngine.EventSystems.BaseRaycaster>())
                br.enabled = false;

            cam.targetTexture = _leftRT;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.rect = new Rect(0f, 0f, 1f, 1f);

            // Post-AA (especially TAA) uses per-camera history and would fight
            // the second eye. Both eyes render raw so the pair stays identical.
            try
            {
                var uad = cam.GetComponent<UniversalAdditionalCameraData>();
                if (uad != null) uad.antialiasing = AntialiasingMode.None;
            }
            catch (Exception e)
            {
                BerryVRPlugin.Log.LogWarning("[VR] Left eye URP data: " + e.Message);
            }
        }

        private void EnsureRightCamera(Camera cam)
        {
            if (_rightCamGo != null && RightCamera != null && RightCamera.gameObject != null)
            {
                RightCamera.targetTexture = _rightRT;
                return;
            }

            _rightCamGo = new GameObject("BerryVR_RightEye");
            _rightCamGo.tag = "Untagged";
            _rightCamGo.transform.SetParent(cam.transform.parent, false);
            _rightCamGo.transform.position = cam.transform.position;
            _rightCamGo.transform.rotation = cam.transform.rotation;

            var c = _rightCamGo.AddComponent<Camera>();
            c.CopyFrom(cam);
            c.targetTexture = _rightRT;
            c.depth = cam.depth + 1f;
            c.allowHDR = false;
            c.allowMSAA = false;
            c.stereoTargetEye = StereoTargetEyeMask.None;
            c.rect = new Rect(0f, 0f, 1f, 1f);
            c.enabled = true;

            CopyCameraData(cam, c);

            RightCamera = c;
        }

        /// <summary>
        /// Mirrors the URP settings of the authored camera onto the synthetic
        /// right eye so both eyes get the same lighting, post-processing and
        /// volume blending. Camera stacking is intentionally not copied: each
        /// eye is rendered as a single base camera.
        /// </summary>
        private static void CopyCameraData(Camera src, Camera dst)
        {
            try
            {
                var srcData = src.GetComponent<UniversalAdditionalCameraData>();
                var dstData = dst.GetComponent<UniversalAdditionalCameraData>();
                if (dstData == null)
                    dstData = dst.gameObject.AddComponent<UniversalAdditionalCameraData>();

                dstData.renderPostProcessing = true;
                dstData.renderShadows = true;
                // The eye targets are authored at native resolution; URP's
                // post-AA would soften the stereo image without helping text.
                dstData.antialiasing = AntialiasingMode.None;

                if (srcData == null) return;

                dstData.renderPostProcessing = srcData.renderPostProcessing;
                dstData.renderShadows = srcData.renderShadows;
                dstData.antialiasing = AntialiasingMode.None;
                dstData.antialiasingQuality = srcData.antialiasingQuality;
                dstData.stopNaN = srcData.stopNaN;
                dstData.dithering = srcData.dithering;
                dstData.volumeLayerMask = srcData.volumeLayerMask;
                dstData.volumeTrigger = srcData.volumeTrigger;

                // URP 17 exposes the renderer through GetRenderer()/SetRenderer()
                // instead of a public index property. Copy the serialized value
                // so both eyes use the same renderer asset.
                var rendererIndex = typeof(UniversalAdditionalCameraData).GetField(
                    "m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                if (rendererIndex != null)
                {
                    try { rendererIndex.SetValue(dstData, rendererIndex.GetValue(srcData)); }
                    catch { }
                }
            }
            catch (Exception e)
            {
                BerryVRPlugin.Log.LogWarning("[VR] URP camera data copy: " + e.Message);
            }
        }

        private void TryInit()
        {
            if (Active) return;
            if (Time.realtimeSinceStartup < _retryAt) return;
            _retryAt = Time.realtimeSinceStartup + 20f;

            // The first attempt is unconditional so SteamVR can auto-start.
            // Later attempts only happen when a headset is actually present,
            // which keeps flat mode completely quiet (and avoids long init
            // timeouts every 20 seconds when no HMD is around).
            if (_attemptedInit)
            {
                bool present = true;
                try { present = OpenVR.IsHmdPresent(); }
                catch { present = true; }
                if (!present) return;
            }
            _attemptedInit = true;

            try
            {
                var err = EVRInitError.None;
                _system = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Scene);
                if (_system == null || err != EVRInitError.None)
                {
                    if (!_initFailedLogged)
                    {
                        _initFailedLogged = true;
                        BerryVRPlugin.Log.LogWarning("[VR] OpenVR init failed: " + err + ". Flat mode active; will retry every 20s.");
                    }
                    try { OpenVR.Shutdown(); } catch { }
                    return;
                }

                BerryVRPlugin.Log.LogInfo("[VR] OpenVR initialized: " + err);
                BerryVRPlugin.Log.LogInfo("[VR] Graphics: " + SystemInfo.graphicsDeviceType +
                    " | colorSpace=" + QualitySettings.activeColorSpace +
                    " | " + SystemInfo.graphicsDeviceName);
                _compositor = OpenVR.Compositor;
                _vrInput = OpenVR.Input;
                if (_compositor == null)
                {
                    BerryVRPlugin.Log.LogWarning("[VR] No compositor.");
                    OpenVR.Shutdown();
                    return;
                }

                _system.GetRecommendedRenderTargetSize(ref _rtWidth, ref _rtHeight);
                if (_rtWidth < 64 || _rtHeight < 64)
                {
                    _rtWidth = 2048;
                    _rtHeight = 2048;
                }
                // Diagnostic: use the exact native target size the runtime asks
                // for. The previous 0.9 scale + square clamp produced a
                // non-native aspect ratio, which some compositor drivers may
                // not handle correctly.
                float rtScale = 0.65f;
                _rtWidth = (uint)Mathf.Clamp(Mathf.RoundToInt(_rtWidth * rtScale), 1024, 2600);
                _rtHeight = (uint)Mathf.Clamp(Mathf.RoundToInt(_rtHeight * rtScale), 1024, 2600);
                CreateRenderTextures();

                try { _compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseStanding); }
                catch (Exception e) { BerryVRPlugin.Log.LogWarning("[VR] SetTrackingSpace: " + e.Message); }
                float hmdHz = 0f;
                try
                {
                    var propError = ETrackedPropertyError.TrackedProp_Success;
                    hmdHz = _system.GetFloatTrackedDeviceProperty(
                        OpenVR.k_unTrackedDeviceIndex_Hmd,
                        ETrackedDeviceProperty.Prop_DisplayFrequency_Float,
                        ref propError);
                }
                catch { }
                _hmdTargetFps = hmdHz > 1f ? Mathf.RoundToInt(hmdHz) : -1;
                BerryVRPlugin.Log.LogInfo("[VR] HMD refresh: " + hmdHz + " Hz | target FPS " + _hmdTargetFps);
                Application.targetFrameRate = _hmdTargetFps;
                QualitySettings.vSyncCount = 0;
                QualitySettings.maxQueuedFrames = 1;

                string manifest = Path.Combine(
                    Path.GetDirectoryName(typeof(BerryVRPlugin).Assembly.Location) ?? "",
                    "berryvr_actions.json");
                Input.Init(_system, _vrInput, manifest);
                UI.Init(this, _compositor);
                BuildAimIndicator();

                Active = true;
                _initFailedLogged = false;
                BerryVRPlugin.Log.LogInfo(string.Format("[VR] Stereo eye targets: {0}x{1}", _rtWidth, _rtHeight));

                // Rebuild for the scene we are already in.
                OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            }
            catch (Exception e)
            {
                BerryVRPlugin.Log.LogError("[VR] Init exception: " + e);
                try { OpenVR.Shutdown(); } catch { }
                Active = false;
            }
        }

        private RenderTexture CreateEyeTexture(string name)
        {
            var rt = new RenderTexture((int)_rtWidth, (int)_rtHeight, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name,
                useMipMap = false,
                antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                // Unity releases scene-local render textures during a scene
                // load. The eye targets are owned by this persistent manager,
                // so keep them alive across transitions.
                hideFlags = HideFlags.DontSave
            };
            rt.Create();
            return rt;
        }

        private void CreateRenderTextures()
        {
            ReleaseRenderTextures();
            _leftRT = CreateEyeTexture("BerryVR_LeftEye");
            _rightRT = CreateEyeTexture("BerryVR_RightEye");
            BerryVRPlugin.Log.LogInfo("[VR] Eye RT format: " + _leftRT.format +
                " graphicsFormat=" + _leftRT.graphicsFormat +
                " desc=" + _leftRT.descriptor.graphicsFormat);
            CacheNativeTextures();
        }

        /// <summary>
        /// Caches the native D3D11 handles outside of the render loop. Calling
        /// GetNativeTexturePtr from endCameraRendering makes Unity's main
        /// thread wait for GfxDevice ownership while the render thread waits
        /// for its own semaphore, which deadlocks the whole player. The
        /// handles of a RenderTexture are stable until it is released.
        /// </summary>
        private void CacheNativeTextures()
        {
            RefreshNativeTextures(true);
        }

        /// <summary>
        /// Re-reads the native handles after a scene load or device reset.
        /// Unity may silently recreate the underlying D3D11 textures when the
        /// main menu applies display settings; keeping the old handles makes
        /// the compositor return InvalidTexture/TextureUsesUnsupportedFormat.
        /// This must only run on the main thread outside the render callback.
        /// </summary>
        private void RefreshNativeTextures(bool forceLog = false)
        {
            if (_leftRT == null || _rightRT == null) return;

            bool leftCreated = _leftRT.IsCreated();
            bool rightCreated = _rightRT.IsCreated();
            if (!leftCreated)
            {
                _leftRT.Create();
                leftCreated = _leftRT.IsCreated();
                BerryVRPlugin.Log.LogWarning("[VR] Left eye RT had to be recreated.");
            }
            if (!rightCreated)
            {
                _rightRT.Create();
                rightCreated = _rightRT.IsCreated();
                BerryVRPlugin.Log.LogWarning("[VR] Right eye RT had to be recreated.");
            }
            if (!leftCreated || !rightCreated) return;

            IntPtr left = _leftRT.GetNativeTexturePtr();
            IntPtr right = _rightRT.GetNativeTexturePtr();
            if (left == IntPtr.Zero || right == IntPtr.Zero) return;

            bool changed = left != _leftNativeTex || right != _rightNativeTex;
            _leftNativeTex = left;
            _rightNativeTex = right;
            VRNative.SetTextures(left, right);

            if (changed || forceLog)
            {
                BerryVRPlugin.Log.LogInfo("[VR] Eye textures" + (changed ? " refreshed" : "") +
                    ": L=0x" + left.ToString("X") + " R=0x" + right.ToString("X"));
            }
        }

        private void ReleaseRenderTextures()
        {
            _leftNativeTex = IntPtr.Zero;
            _rightNativeTex = IntPtr.Zero;
            if (_leftRT != null) { _leftRT.Release(); Destroy(_leftRT); _leftRT = null; }
            if (_rightRT != null) { _rightRT.Release(); Destroy(_rightRT); _rightRT = null; }
        }

        private void AttachPlayer()
        {
            _player = Player.Singleton != null ? Player.Singleton.transform : null;
            if (_player == null) return;

            _playerRb = _player.GetComponent<Rigidbody>();
            if (_playerRb == null) _playerRb = _player.GetComponentInChildren<Rigidbody>();
            _playerCollider = _player.GetComponent<Collider>();
            if (_playerCollider == null) _playerCollider = _player.GetComponentInChildren<Collider>();
            if (_playerCollider != null)
                _playerPivotHeight = Mathf.Max(0.2f, _player.position.y - _playerCollider.bounds.min.y);
            else
                _playerPivotHeight = 1f;

            _playerCam = GameManager.Singleton != null ? GameManager.Singleton.playerCamera : null;
            if (_playerCam == null)
            {
                var t = FindDeep(_player, "PlayerCam FPS");
                if (t != null) _playerCam = t.gameObject;
            }

            // Gameplay code on PlayerCam FPS must not move it anymore.
            foreach (var mb in _player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var n = mb.GetType().Name;
                if (n.StartsWith("Cinemachine") || n == "PlayerCamSensHandler" || n == "CameraFollowLag" ||
                    n == "CrosshairUIRaycaster" || n == "CameraRaycaster")
                    mb.enabled = false;
            }
            foreach (var cr in Resources.FindObjectsOfTypeAll<CrosshairUIRaycaster>())
                if (cr != null) cr.enabled = false;
            foreach (var cr in Resources.FindObjectsOfTypeAll<CameraRaycaster>())
                if (cr != null) cr.enabled = false;
            if (_playerCam != null)
            {
                foreach (var mb in _playerCam.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    var n = mb.GetType().Name;
                    if (n.StartsWith("Cinemachine")) mb.enabled = false;
                }
            }

            _grabber = FindFirstObjectByType<PlayerGrabber>();
            if (_grabber != null)
            {
                if (GrabberPlayerCamField != null)
                    GrabberPlayerCamField.SetValue(_grabber, RightAim.gameObject);

                if (_playerCam != null)
                {
                    var hold = FindDeepEither("Hold Spot");
                    var vacuum = FindDeepEither("Vacuum");
                    var gun = FindDeepEither("Gun Parent");
                    var suck = FindDeepEither("Vacuum Suck Trigger");
                    var dart = FindDeepEither("Pop Gun Dart SpawnPoint");

                    AttachTo(hold, RightGrip, new Vector3(0f, 0f, 0.10f));
                    AttachTo(vacuum, RightGrip, new Vector3(0f, 0f, 0.05f));
                    // The pop gun is a small toy (about 12 cm long): centred on
                    // the grip it disappears inside the glove's fist. Carry it
                    // on the aim pose, in front of the fingers, so it is visible
                    // and points exactly where darts fly. Its scale is left alone:
                    // the game's shoot/reload feedbacks spring it back to 1.
                    AttachTo(gun, RightAim, GunLocalPos);
                    _gunParent = gun;
                    AttachTo(suck, RightGrip, new Vector3(0f, 0f, 0.38f));
                    AttachTo(dart, RightAim, new Vector3(0f, -0.005f, 0.18f));

                    // Note: "Ms Rainbow Frame" is authored cutscene/scenery art,
                    // not the first-person body. It must stay untouched.
                }
            }

            BuildHandVisuals();
            VRPatches.HideCrosshairs();
            BerryVRPlugin.Log.LogInfo("[VR] Player attached: " + (_grabber != null ? _grabber.name : "no grabber") +
                " cam=" + (_playerCam != null ? _playerCam.name : "none") +
                " pivotHeight=" + _playerPivotHeight.ToString("F2"));
        }

        private void AttachTo(Transform t, Transform parent, Vector3 localPos)
        {
            if (t == null) return;
            t.SetParent(parent, false);
            _attachedTools.Add(t);
            foreach (var f in t.GetComponentsInChildren<CameraFollowLag>(true))
            {
                f.enabled = false;
                _followLags.Add(f);
            }
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// The player's tools are re-parented under the tracked hands, which
        /// live in DontDestroyOnLoad. Every new day reloads the Game scene and
        /// spawns a new player, so the previous player's tools must go with
        /// their scene instead of piling up in the hands.
        /// </summary>
        private void DestroyAttachedTools()
        {
            int n = 0;
            for (int i = 0; i < _attachedTools.Count; i++)
            {
                var t = _attachedTools[i];
                if (t == null) continue;
                Destroy(t.gameObject);
                n++;
            }
            _attachedTools.Clear();
            _followLags.Clear();
            _gunParent = null;
            if (n > 0) BerryVRPlugin.Log.LogInfo("[VR] Released " + n + " tools of the previous player.");
        }

        private Transform FindDeepEither(string name)
        {
            var t = _playerCam != null ? FindDeep(_playerCam.transform, name) : null;
            if (t == null && _player != null) t = FindDeep(_player, name);
            return t;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private void BuildHandVisuals()
        {
            if (RightAim.Find("BerryVR_ControllerModel") != null) return;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null)
            {
                BerryVRPlugin.Log.LogWarning("[VR] No shader found for the controller models.");
                return;
            }

            var mat = new Material(sh) { name = "BerryVR_Hand" };
            SetMaterialColor(mat, new Color(0.13f, 0.13f, 0.16f, 1f));

            _rightHandModel = CreateControllerModel(RightAim, mat);
            _leftHandModel = CreateControllerModel(LeftAim, mat);
        }

        private void BuildGloves()
        {
            if (_glovesTried) return;
            _glovesTried = true;

            // Classic cartoon gloves: off-white with a berry red cuff.
            var skin = CartoonGlove.MakeLitMaterial("BerryVR_Glove", new Color(0.97f, 0.95f, 0.91f, 1f), 0.15f);
            var cuff = CartoonGlove.MakeLitMaterial("BerryVR_GloveCuff", new Color(0.86f, 0.18f, 0.32f, 1f), 0.2f);
            var outline = CartoonGlove.MakeOutlineMaterial();
            if (skin == null || cuff == null)
            {
                BerryVRPlugin.Log.LogWarning("[VR] No lit shader found for the gloves; keeping controller models.");
                return;
            }

            _rightGlove = new CartoonGlove(transform, "BerryVR_RightGlove", skin, cuff, outline);
            _leftGlove = new CartoonGlove(transform, "BerryVR_LeftGlove", skin, cuff, outline);
            BerryVRPlugin.Log.LogInfo("[VR] Cartoon gloves built (outline=" + (outline != null) + ").");
        }

        /// <summary>Returns true when the glove is drawn for this hand.</summary>
        private bool UpdateGlove(CartoonGlove glove, HandSkeleton hand, bool tracked)
        {
            bool enabled = VRSettings.CartoonGloves == null || VRSettings.CartoonGloves.Value;
            if (hand == null || !hand.valid || !tracked || !enabled)
            {
                if (glove != null) glove.SetVisible(false);
                return false;
            }
            if (glove == null)
            {
                BuildGloves();
                return false;
            }

            Vector3 rootPos = _origin + _rigYaw * hand.root.pos + RigOffset;
            Quaternion rootRot = _rigYaw * hand.root.rot;
            glove.SetVisible(true);
            glove.Apply(hand, rootPos, rootRot, VRSettings.GloveScale != null ? VRSettings.GloveScale.Value : 1f);
            return true;
        }

        private static GameObject CreateControllerModel(Transform hand, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BerryVR_ControllerModel";
            // The shop switches the world camera to the UI layer only; keeping
            // the hands (and the UI laser) on that layer makes them survive.
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(hand, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.055f);
            go.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            go.transform.localScale = new Vector3(0.038f, 0.055f, 0.13f);

            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private static void SetMaterialColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private void BuildAimIndicator()
        {
            if (_aimBuilt) return;
            _aimBuilt = true;

            Shader lineShader = Shader.Find("Sprites/Default");
            if (lineShader == null) lineShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (lineShader == null) lineShader = Shader.Find("Unlit/Color");

            Shader dotShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (dotShader == null) dotShader = Shader.Find("Unlit/Color");
            if (dotShader == null) dotShader = lineShader;

            if (lineShader == null || dotShader == null)
            {
                BerryVRPlugin.Log.LogWarning("[VR] No shader found for the aim indicator.");
                return;
            }

            _aimLineMat = new Material(lineShader) { name = "BerryVR_AimLine" };
            SetMaterialColor(_aimLineMat, Color.white);
            _aimDotMat = new Material(dotShader) { name = "BerryVR_AimDot" };
            SetMaterialColor(_aimDotMat, new Color(1f, 0.96f, 0.88f, 1f));

            var lineGo = new GameObject("BerryVR_AimLine");
            lineGo.transform.SetParent(transform, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) lineGo.layer = uiLayer;
            _aimLine = lineGo.AddComponent<LineRenderer>();
            _aimLine.useWorldSpace = true;
            _aimLine.positionCount = 2;
            _aimLine.startWidth = 0.0022f;
            _aimLine.endWidth = 0.0008f;
            _aimLine.numCapVertices = 2;
            _aimLine.sharedMaterial = _aimLineMat;
            _aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _aimLine.receiveShadows = false;
            _aimLine.textureMode = LineTextureMode.Stretch;
            _aimLine.startColor = new Color(1f, 1f, 1f, 0.14f);
            _aimLine.endColor = new Color(1f, 1f, 1f, 0.04f);
            _aimLine.sortingOrder = 30000;
            _aimLine.enabled = false;

            _aimDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _aimDot.name = "BerryVR_AimDot";
            if (uiLayer >= 0) _aimDot.layer = uiLayer;
            Destroy(_aimDot.GetComponent<Collider>());
            _aimDot.transform.SetParent(transform, false);
            _aimDot.transform.localScale = Vector3.one * 0.0075f;
            _aimDotRenderer = _aimDot.GetComponent<Renderer>();
            if (_aimDotRenderer != null)
            {
                _aimDotRenderer.sharedMaterial = _aimDotMat;
                _aimDotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _aimDotRenderer.receiveShadows = false;
                _aimDotRenderer.sortingOrder = 30000;
            }
            _aimDot.SetActive(false);
        }

        private void UpdateAimIndicator()
        {
            if (!_aimBuilt || _aimLine == null || _aimDot == null)
                return;

            GameObject target = null;
            bool show = _gameScene && UI != null && !UI.IsMenuOpen && !UI.LaserVisible &&
                        Input != null && Input.RightAim.valid && RightAim != null && _grabber != null;
            bool holding = _grabber != null && IsHoldingObject();
            if (show && !holding)
                target = GetGrabberTarget();

            if (Input != null && target != null && target != _highlighted)
                Input.PulseRight(500);

            SetHighlight(target);

            // No permanent laser during play: the game's own selection
            // outline marks what the hand points at, and a faint beam ties it
            // to the hand only while something can actually be used.
            if (target == null)
            {
                _aimLine.enabled = false;
                _aimDot.SetActive(false);
                return;
            }

            var ray = new Ray(RightAim.position, RightAim.forward);
            int count = Physics.RaycastNonAlloc(ray, _aimHits, 25f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = Vector3.zero;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = _aimHits[i];
                if (h.collider == null) continue;
                if (_player != null && h.collider.transform.IsChildOf(_player)) continue;
                if (h.distance >= best) continue;
                best = h.distance;
                point = h.point;
                found = true;
            }
            if (!found)
            {
                _aimLine.enabled = false;
                _aimDot.SetActive(false);
                return;
            }

            _aimLine.enabled = true;
            _aimLine.SetPosition(0, RightAim.position);
            _aimLine.SetPosition(1, point);
            _aimLine.startColor = new Color(1f, 0.97f, 0.85f, 0.22f);
            _aimLine.endColor = new Color(1f, 0.97f, 0.85f, 0.0f);

            _aimDot.SetActive(true);
            _aimDot.transform.position = point - ray.direction * 0.01f;
        }

        private void UpdateVignette(Camera cam)
        {
            bool enabled = VRSettings.ComfortVignette != null && VRSettings.ComfortVignette.Value;
            if (!enabled)
            {
                if (_vignette != null) _vignette.Hide();
                return;
            }
            if (_vignette == null) _vignette = new ComfortVignette(Head);

            // Only artificial motion counts: stick turning, and body speed
            // while the move stick is held (not knockbacks or head motion).
            float motion = _turnActivity;
            bool menu = UI != null && UI.IsMenuOpen;
            if (_gameScene && !menu && _playerRb != null && Input != null && Input.Move.sqrMagnitude > 0.02f)
            {
                Vector3 v = _playerRb.velocity;
                v.y = 0f;
                motion = Mathf.Max(motion, v.magnitude / 5f);
            }
            float strength = VRSettings.VignetteStrength != null ? VRSettings.VignetteStrength.Value : 0.65f;
            _vignette.Tick(motion, strength, cam.nearClipPlane, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Pins the pop gun in front of the right hand every frame and logs
        /// where it actually is each time the game shows it.
        /// </summary>
        private void KeepGunInHand()
        {
            if (_gunParent == null || RightAim == null) return;
            PinGun();

            var art = _grabber != null && GrabberGunArtField != null
                ? GrabberGunArtField.GetValue(_grabber) as GameObject
                : null;
            bool visible = art != null && art.activeInHierarchy;
            if (visible && !_gunWasVisible)
            {
                var r = art.GetComponentInChildren<Renderer>();
                string path = art.name;
                for (var t = art.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                BerryVRPlugin.Log.LogInfo("[VR] Pop gun shown: path=" + path +
                    " distToHand=" + Vector3.Distance(art.transform.position, RightAim.position).ToString("F3") +
                    " distToFist=" + (RightIndexTipValid ? Vector3.Distance(art.transform.position, RightFistCenter).ToString("F3") : "n/a") +
                    " meshToFist=" + (RightIndexTipValid && r != null ? Vector3.Distance(r.bounds.center, RightFistCenter).ToString("F3") : "n/a") +
                    " lossyScale=" + art.transform.lossyScale.ToString("F2") +
                    " renderer=" + (r != null ? r.enabled + " bounds=" + r.bounds.size.ToString("F3") : "none"));
                if (VRSettings.DebugCaptures != null && VRSettings.DebugCaptures.Value && _gunCaptures < 3)
                    StartCoroutine(CaptureLeftEye("gun" + (++_gunCaptures)));
            }
            _gunWasVisible = visible;
        }

        /// <summary>
        /// Holds the pop gun in the glove's fist. Runs in LateUpdate and again
        /// right before rendering, so no game script that moves it later in
        /// the frame (CameraFollowLag re-applies its camera-relative offset of
        /// about 10 cm right / 21 cm ahead) can win.
        /// </summary>
        private void PinGun()
        {
            if (_gunParent == null || RightAim == null) return;
            DisableFollowLag();

            if (_gunParent.parent != RightAim) _gunParent.SetParent(RightAim, false);
            _gunParent.localRotation = Quaternion.identity;
            if (RightIndexTipValid)
            {
                // Centred in the hollow of the glove's fist, barrel along the
                // aim ray. The model's pivot is near its middle, so it needs a
                // small nudge down/left to sit in the hand (tunable in config).
                float right = VRSettings.GunOffsetRight != null ? VRSettings.GunOffsetRight.Value : -2f;
                float up = VRSettings.GunOffsetUp != null ? VRSettings.GunOffsetUp.Value : -1.5f;
                float fwd = VRSettings.GunOffsetForward != null ? VRSettings.GunOffsetForward.Value : 0f;
                _gunParent.position = RightFistCenter +
                    (RightAim.right * right + RightAim.up * up + RightAim.forward * fwd) * 0.01f;
            }
            else
            {
                _gunParent.localPosition = GunLocalPos;
            }
        }

        /// <summary>
        /// CameraFollowLag (on the gun and the vacuum) rewrites their local
        /// pose every LateUpdate to its authored camera offset plus sway.
        /// Attached to a tracked hand that offset makes them float away.
        /// </summary>
        private void DisableFollowLag()
        {
            for (int i = 0; i < _followLags.Count; i++)
            {
                var f = _followLags[i];
                if (f == null || !f.enabled) continue;
                f.enabled = false;
                if (!_loggedFollowLag)
                {
                    _loggedFollowLag = true;
                    BerryVRPlugin.Log.LogInfo("[VR] Disabled CameraFollowLag on " + f.name + " (it was re-enabled).");
                }
            }
        }

        private void OnBeforeRender()
        {
            if (Active && _gameScene) PinGun();
        }

        /// <summary>
        /// Debug: saves what the left eye saw to BepInEx\BerryVR_&lt;tag&gt;.png,
        /// a few frames later, so placement issues can be checked offline.
        /// </summary>
        private IEnumerator CaptureLeftEye(string tag)
        {
            for (int i = 0; i < 6; i++) yield return null;
            yield return new WaitForEndOfFrame();
            if (_leftRT == null || !_leftRT.IsCreated()) yield break;

            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = _leftRT;
                tex = new Texture2D(_leftRT.width, _leftRT.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, _leftRT.width, _leftRT.height), 0, 0);
                tex.Apply();
                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "BerryVR_" + tag + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                BerryVRPlugin.Log.LogInfo("[VR] Saved left eye capture " + path);
            }
            catch (Exception e)
            {
                BerryVRPlugin.Log.LogWarning("[VR] Left eye capture failed: " + e.Message);
            }
            finally
            {
                RenderTexture.active = prev;
                if (tex != null) Destroy(tex);
            }
        }

        private bool IsHoldingObject()
        {
            return GrabberHeldField != null && GrabberHeldField.GetValue(_grabber) as Rigidbody != null;
        }

        private GameObject GetGrabberTarget()
        {
            for (int i = 0; i < GrabberTargetFields.Length; i++)
            {
                var f = GrabberTargetFields[i];
                if (f == null) continue;
                var go = f.GetValue(_grabber) as GameObject;
                if (go != null) return go;
            }
            return null;
        }

        /// <summary>
        /// Adds the game's selection outline (the one used on buildables) to
        /// the targeted object. Only the outline entry is removed afterwards,
        /// so material changes the game makes meanwhile are preserved.
        /// </summary>
        private void SetHighlight(GameObject target)
        {
            if (target == _highlighted) return;

            Material outline = GameManager.Singleton != null && GameManager.Singleton.prefabBank != null
                ? GameManager.Singleton.prefabBank.selectionOutline_Mat
                : null;

            for (int i = 0; i < _highlightRenderers.Count; i++)
            {
                var r = _highlightRenderers[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                int keep = 0;
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != _highlightMat) keep++;
                if (keep == mats.Length) continue;
                var trimmed = new Material[keep];
                for (int m = 0, k = 0; m < mats.Length; m++)
                    if (mats[m] != _highlightMat) trimmed[k++] = mats[m];
                r.sharedMaterials = trimmed;
            }
            _highlightRenderers.Clear();
            _highlighted = target;
            _highlightMat = outline;

            if (target == null || outline == null) return;

            // Pickups are resolved to their transform root; never outline a
            // whole scene branch if an object happens to be parented deep.
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 32) return;

            foreach (var r in renderers)
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                if (mats.Length == 0 || System.Array.IndexOf(mats, outline) >= 0) continue;
                var added = new Material[mats.Length + 1];
                mats.CopyTo(added, 0);
                added[mats.Length] = outline;
                r.sharedMaterials = added;
                _highlightRenderers.Add(r);
            }
        }

        private void UpdateOriginFromPlayer(Camera cam)
        {
            if (_player != null)
            {
                _origin = new Vector3(_player.position.x, _player.position.y - _playerPivotHeight, _player.position.z);
            }
            else
            {
                _origin = cam.transform.position - Quaternion.Euler(0f, _baseYaw, 0f) * (_hmd.valid ? _hmd.pos : Vector3.zero);
            }
        }

        private void Update()
        {
            if (!Active)
            {
                TryInit();
                return;
            }

            _submittedThisFrame = false;

            // Submit the previous frame's eye textures before asking for the
            // new poses. This keeps the texture/pose pair coherent and, more
            // importantly, never submits a texture while the render thread is
            // still inside the frame that produced it.
            if (_deferSubmit && _pendingSubmit)
            {
                _pendingSubmit = false;
                Submit(EVREye.Eye_Left, _leftNativeTex);
                Submit(EVREye.Eye_Right, _rightNativeTex);
            }

            if (Time.unscaledTime >= _nextRtRefresh)
            {
                _nextRtRefresh = Time.unscaledTime + 0.5f;
                RefreshNativeTextures();
            }

            if (VRNative.Ready && _nativeLogCounter < 30 && Time.unscaledTime >= _nextNativeLog)
            {
                _nextNativeLog = Time.unscaledTime + 2f;
                _nativeLogCounter++;
                bool lc = _leftRT != null && _leftRT.IsCreated();
                bool rc = _rightRT != null && _rightRT.IsCreated();
                BerryVRPlugin.Log.LogInfo("[VR] Native events=" + VRNative.EventCount +
                                          " submits=" + VRNative.SubmitCount +
                                          " lastError=" + VRNative.LastError +
                                          " RTcreated=" + lc + "/" + rc);
            }

            if (Time.unscaledTime >= _nextPerfCheck)
            {
                _nextPerfCheck = Time.unscaledTime + 2f;
                // OptionsManager applies the user's vsync/FPS prefs on every
                // change. In VR we render at the HMD refresh rate: running
                // faster makes Submit happen twice inside one compositor
                // frame, which SteamVR explicitly warns about.
                if (Application.targetFrameRate != _hmdTargetFps)
                    Application.targetFrameRate = _hmdTargetFps;
                if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
                if (QualitySettings.maxQueuedFrames != 1) QualitySettings.maxQueuedFrames = 1;
            }

            if (!_noPoses)
            {
                try
                {
                    var perr = _compositor.WaitGetPoses(_renderPoses, _gamePoses);
                    if (perr != EVRCompositorError.None && !_submitErrorLogged)
                    {
                        _submitErrorLogged = true;
                        BerryVRPlugin.Log.LogWarning("[VR] WaitGetPoses: " + perr);
                    }
                }
                catch (Exception e)
                {
                    if (!_submitErrorLogged)
                    {
                        _submitErrorLogged = true;
                        BerryVRPlugin.Log.LogWarning("[VR] WaitGetPoses exception: " + e.Message);
                    }
                }
            }

            if (_renderPoses[0].bPoseIsValid)
            {
                _hmd = VRPose.FromMatrix(_renderPoses[0].mDeviceToAbsoluteTracking);
            }
            _eyeL = VRPose.FromMatrix(_system.GetEyeToHeadTransform(EVREye.Eye_Left));
            _eyeR = VRPose.FromMatrix(_system.GetEyeToHeadTransform(EVREye.Eye_Right));

            Input.Update(Time.unscaledDeltaTime, _renderPoses);

            if (InputManager.Singleton != null && InputManager.Singleton.inputActions != null)
                Input.BindGameActions(InputManager.Singleton.inputActions);

            HandleSnapTurn();

            if (Input.RecenterRequested)
                Recenter();
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                Recenter();

            if (_gameScene && _player != null)
                UpdateOriginFromPlayer(LeftCamera);
        }

        private void HandleSnapTurn()
        {
            if (_snapCooldown > 0f) _snapCooldown -= Time.unscaledDeltaTime;

            // In menus the right stick scrolls, so use the left stick to turn.
            bool menu = UI != null && UI.IsMenuOpen;
            float x = menu ? Input.Move.x : Input.Turn.x;

            _turnActivity = 0f;
            if (Mathf.Abs(x) < 0.2f) return;

            bool snap = _snapTurnEnv || (VRSettings.Turn != null && VRSettings.Turn.Value == TurnMode.Snap);
            if (!_loggedTurn)
            {
                _loggedTurn = true;
                BerryVRPlugin.Log.LogInfo("[VR] Turn input " + x.ToString("F2") +
                    " (menu=" + menu + ", snap=" + snap + ").");
            }

            if (snap)
            {
                if (Mathf.Abs(x) > 0.65f && _snapCooldown <= 0f)
                {
                    float step = VRSettings.SnapTurnAngle != null ? VRSettings.SnapTurnAngle.Value : 45f;
                    RotateRig(step * Mathf.Sign(x));
                    _snapCooldown = 0.28f;
                    Input.PulseRight(900);
                }
                return;
            }

            // Smooth turn: speed ramps with stick deflection past the deadzone.
            float t = (Mathf.Abs(x) - 0.2f) / 0.8f;
            float speed = VRSettings.SmoothTurnSpeed != null ? VRSettings.SmoothTurnSpeed.Value : 140f;
            RotateRig(Mathf.Sign(x) * t * speed * Time.unscaledDeltaTime);
            _turnActivity = t;
        }

        /// <summary>
        /// Turns the rig around the head instead of the tracking origin, so a
        /// player standing off-center in their room does not slide sideways.
        /// </summary>
        private void RotateRig(float degrees)
        {
            Quaternion oldYaw = Quaternion.Euler(0f, _baseYaw + _snapYaw, 0f);
            _snapYaw = NormalizeAngle(_snapYaw + degrees);
            Quaternion newYaw = Quaternion.Euler(0f, _baseYaw + _snapYaw, 0f);

            Vector3 hpos = _hmd.valid ? _hmd.pos : Vector3.zero;
            hpos.y = 0f;
            _posOffset += oldYaw * hpos - newYaw * hpos;
        }

        public void Recenter()
        {
            if (!Active) return;

            float hmdYaw = _hmd.valid ? NormalizeAngle(_hmd.rot.eulerAngles.y) : 0f;

            // Rotate the rig so the current view faces the direction the scene
            // intended. The head itself must not translate: compensate the
            // offset introduced by the yaw change.
            Quaternion oldYaw = Quaternion.Euler(0f, _baseYaw + _snapYaw, 0f);
            _baseYaw = NormalizeAngle(_homeYaw - hmdYaw);
            _snapYaw = 0f;
            Quaternion newYaw = Quaternion.Euler(0f, _baseYaw, 0f);

            Vector3 hpos = _hmd.valid ? _hmd.pos : Vector3.zero;
            _posOffset += oldYaw * hpos - newYaw * hpos;

            if (UI != null) UI.SnapInFront();
            if (Input != null) Input.PulseRight(1500);
            BerryVRPlugin.Log.LogInfo("[VR] Recentered to home yaw " + _homeYaw.ToString("F1") + ".");
        }

        private void LateUpdate()
        {
            if (!Active || _noStereo || LeftCamera == null) return;

            // Never retrieve native texture pointers during rendering: doing
            // so deadlocks the GfxDevice against the render thread. LateUpdate
            // runs before the frame is submitted, so a lazy retry is safe.
            if (_leftNativeTex == IntPtr.Zero || _rightNativeTex == IntPtr.Zero)
                CacheNativeTextures();

            var cam = LeftCamera;

            // Some menus re-enable the flat rendering path (target texture,
            // stereo eye, partial viewport). Reassert our eye targets every
            // frame so the compositor always receives a valid stereo pair.
            if (cam.targetTexture != _leftRT) cam.targetTexture = _leftRT;
            if (cam.stereoTargetEye != StereoTargetEyeMask.None) cam.stereoTargetEye = StereoTargetEyeMask.None;
            if (cam.rect != new Rect(0f, 0f, 1f, 1f)) cam.rect = new Rect(0f, 0f, 1f, 1f);
            if (RightCamera != null)
            {
                if (RightCamera.targetTexture != _rightRT) RightCamera.targetTexture = _rightRT;
                if (RightCamera.stereoTargetEye != StereoTargetEyeMask.None) RightCamera.stereoTargetEye = StereoTargetEyeMask.None;
                if (RightCamera.rect != new Rect(0f, 0f, 1f, 1f)) RightCamera.rect = new Rect(0f, 0f, 1f, 1f);
                if (!RightCamera.enabled) RightCamera.enabled = true;

                // Gameplay code changes the left camera at runtime (the shop,
                // for example, switches its culling mask to UI only). Keep the
                // synthetic eye in sync or the two eyes would show different
                // worlds for a frame or longer.
                if (RightCamera.cullingMask != cam.cullingMask) RightCamera.cullingMask = cam.cullingMask;
                if (RightCamera.clearFlags != cam.clearFlags) RightCamera.clearFlags = cam.clearFlags;
                if (RightCamera.backgroundColor != cam.backgroundColor) RightCamera.backgroundColor = cam.backgroundColor;
                if (!Mathf.Approximately(RightCamera.nearClipPlane, cam.nearClipPlane)) RightCamera.nearClipPlane = cam.nearClipPlane;
                if (!Mathf.Approximately(RightCamera.farClipPlane, cam.farClipPlane)) RightCamera.farClipPlane = cam.farClipPlane;
                float wantedDepth = cam.depth + 1f;
                if (Mathf.Abs(RightCamera.depth - wantedDepth) > 0.001f) RightCamera.depth = wantedDepth;
            }

            _rigYaw = Quaternion.Euler(0f, _baseYaw + _snapYaw, 0f);

            Vector3 hpos = _hmd.valid ? _hmd.pos : Vector3.zero;
            Quaternion hrot = _hmd.valid ? _hmd.rot : Quaternion.identity;
            _headPos = _origin + _rigYaw * hpos + RigOffset;
            _headRot = _rigYaw * hrot;

            Head.position = _headPos;
            Head.rotation = _headRot;
            UpdateVignette(cam);
            VRAudio.Tick(cam);

            Vector3 eyeL = _headPos + _headRot * _eyeL.pos;
            Quaternion rotL = _headRot * _eyeL.rot;
            Vector3 eyeR = _headPos + _headRot * _eyeR.pos;
            Quaternion rotR = _headRot * _eyeR.rot;

            _eyeLPos = eyeL;
            _eyeLRot = rotL;
            _eyeRPos = eyeR;
            _eyeRRot = rotR;
            _eyePosesReady = true;

            if (_poseLogs < 15)
            {
                _poseLogs++;
                BerryVRPlugin.Log.LogInfo("[VR] Pose #" + _poseLogs +
                    " hmdValid=" + _hmd.valid +
                    " hmdPos=" + _hmd.pos.ToString("F3") +
                    " hmdRot=" + _hmd.rot.eulerAngles.ToString("F1") +
                    " head=" + _headPos.ToString("F3") +
                    " headRot=" + _headRot.eulerAngles.ToString("F1") +
                    " eyeL=" + eyeL.ToString("F3") +
                    " eyeLRot=" + rotL.eulerAngles.ToString("F1") +
                    " origin=" + _origin.ToString("F3") +
                    " baseYaw=" + _baseYaw.ToString("F1"));
            }

            cam.transform.position = eyeL;
            cam.transform.rotation = rotL;
            if (RightCamera != null)
            {
                RightCamera.transform.position = eyeR;
                RightCamera.transform.rotation = rotR;
            }

            if (_playerCam != null)
            {
                _playerCam.transform.position = _headPos;
                _playerCam.transform.rotation = _headRot;
            }

            Quaternion rightRot = _rigYaw * (Input.RightGrip.valid ? Input.RightGrip.rot : hrot);
            Vector3 rightPos = _origin + _rigYaw * (Input.RightGrip.valid ? Input.RightGrip.pos : hpos) + RigOffset;
            Quaternion rightAimRot = _rigYaw * (Input.RightAim.valid ? Input.RightAim.rot : rightRot);
            Vector3 rightAimPos = _origin + _rigYaw * (Input.RightAim.valid ? Input.RightAim.pos : (Input.RightGrip.valid ? Input.RightGrip.pos : hpos)) + RigOffset;

            Quaternion leftRot = _rigYaw * (Input.LeftGrip.valid ? Input.LeftGrip.rot : hrot);
            Vector3 leftPos = _origin + _rigYaw * (Input.LeftGrip.valid ? Input.LeftGrip.pos : hpos) + RigOffset;
            Quaternion leftAimRot = _rigYaw * (Input.LeftAim.valid ? Input.LeftAim.rot : leftRot);
            Vector3 leftAimPos = _origin + _rigYaw * (Input.LeftAim.valid ? Input.LeftAim.pos : (Input.LeftGrip.valid ? Input.LeftGrip.pos : hpos)) + RigOffset;

            RightGrip.position = rightPos;
            RightGrip.rotation = rightRot;
            RightAim.position = rightAimPos;
            RightAim.rotation = rightAimRot;
            LeftGrip.position = leftPos;
            LeftGrip.rotation = leftRot;
            LeftAim.position = leftAimPos;
            LeftAim.rotation = leftAimRot;

            bool rightTracked = Input.RightAim.valid;
            bool leftTracked = Input.LeftAim.valid;
            bool rightGlove = UpdateGlove(_rightGlove, Input.RightHand, rightTracked);
            RightIndexTipValid = rightGlove && _rightGlove != null;
            if (RightIndexTipValid)
            {
                RightIndexTip = _rightGlove.IndexTip;
                RightFistCenter = _rightGlove.FistCenter;
            }
            bool leftGlove = UpdateGlove(_leftGlove, Input.LeftHand, leftTracked);
            if (_rightHandModel != null) _rightHandModel.SetActive(rightTracked && !rightGlove);
            if (_leftHandModel != null) _leftHandModel.SetActive(leftTracked && !leftGlove);

            if (Input.RightGrip.valid)
            {
                TrackRightHandVelocity(rightPos, rightRot);
            }
            else
            {
                _haveHandSample = false;
                RightGripVelocity = Vector3.zero;
                RightGripAngularVelocity = Vector3.zero;
            }

            _eyeLProj = ComputeProjection(cam, EVREye.Eye_Left);
            cam.projectionMatrix = _eyeLProj;
            cam.nonJitteredProjectionMatrix = _eyeLProj;
            if (RightCamera != null)
            {
                _eyeRProj = ComputeProjection(RightCamera, EVREye.Eye_Right);
                RightCamera.projectionMatrix = _eyeRProj;
                RightCamera.nonJitteredProjectionMatrix = _eyeRProj;
            }

            UpdateAimIndicator();
            KeepGunInHand();
            if (UI != null) UI.Tick();
        }

        private void TrackRightHandVelocity(Vector3 pos, Quaternion rot)
        {
            float now = Time.realtimeSinceStartup;
            _throwPos[_throwHead] = pos;
            _throwTime[_throwHead] = now;
            _throwHead = (_throwHead + 1) % ThrowSamples;
            if (_throwCount < ThrowSamples) _throwCount++;
            if (!_haveHandSample)
            {
                _haveHandSample = true;
                _lastRightGripPos = pos;
                _lastRightGripRot = rot;
                _lastHandSampleTime = now;
                return;
            }

            float dt = Mathf.Max(0.0001f, now - _lastHandSampleTime);
            Vector3 v = (pos - _lastRightGripPos) / dt;
            if (v.sqrMagnitude > 400f) v = v.normalized * 20f; // ignore tracking pops
            float a = 1f - Mathf.Exp(-18f * dt);
            RightGripVelocity = Vector3.Lerp(RightGripVelocity, v, a);

            Quaternion dq = rot * Quaternion.Inverse(_lastRightGripRot);
            dq.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 av = axis * (angle * Mathf.Deg2Rad / dt);
            RightGripAngularVelocity = Vector3.Lerp(RightGripAngularVelocity, av, a);

            _lastRightGripPos = pos;
            _lastRightGripRot = rot;
            _lastHandSampleTime = now;
        }

        private Matrix4x4 ComputeProjection(Camera cam, EVREye eye)
        {
            float near = Mathf.Max(0.03f, cam.nearClipPlane);
            float far = Mathf.Max(near + 1f, cam.farClipPlane);
            var m = _system.GetProjectionMatrix(eye, near, far);

            // OpenVR's HmdMatrix44_t is row-major; Unity's Matrix4x4
            // constructor takes columns. Passing the rows as columns
            // transposes the projection and squashes the entire view.
            // Rebuild the z row explicitly as well: some OpenVR runtimes
            // return a z translation based on half the near plane.
            float dz = far - near;
            m.m10 = -(far + near) / dz;       // row 2, col 2
            m.m11 = -2f * far * near / dz;    // row 2, col 3
            m.m14 = -1f;                      // row 3, col 2
            m.m15 = 0f;                       // row 3, col 3

            cam.aspect = (float)_rtWidth / _rtHeight;
            return new Matrix4x4(
                new Vector4(m.m0, m.m4, m.m8, m.m12),   // column 0
                new Vector4(m.m1, m.m5, m.m9, m.m13),   // column 1
                new Vector4(m.m2, m.m6, m.m10, m.m14),  // column 2
                new Vector4(m.m3, m.m7, m.m11, m.m15)); // column 3
        }

        /// <summary>
        /// Re-applies the tracked pose and eye target right before URP renders
        /// each camera. Timeline, cinematics and gameplay scripts update camera
        /// transforms after LateUpdate; this is the last safe point to win.
        /// </summary>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!Active || camera == null || !_eyePosesReady) return;
            if (_leftRT == null || _rightRT == null) return;

            if (camera == LeftCamera)
            {
                camera.transform.SetPositionAndRotation(_eyeLPos, _eyeLRot);
                if (camera.targetTexture != _leftRT) camera.targetTexture = _leftRT;
                camera.projectionMatrix = _eyeLProj;
                camera.nonJitteredProjectionMatrix = _eyeLProj;
            }
            else if (camera == RightCamera)
            {
                camera.transform.SetPositionAndRotation(_eyeRPos, _eyeRRot);
                if (camera.targetTexture != _rightRT) camera.targetTexture = _rightRT;
                camera.projectionMatrix = _eyeRProj;
                camera.nonJitteredProjectionMatrix = _eyeRProj;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!Active || _noSubmit || _noStereo || _submittedThisFrame) return;
            if (camera != RightCamera || RightCamera == null) return;
            if (_leftRT == null || _rightRT == null) return;

            if (_endRenderLogs < 40)
            {
                _endRenderLogs++;
                BerryVRPlugin.Log.LogInfo("[VR] EndRender #" + _endRenderLogs +
                    " frame=" + Time.frameCount + " cam=" + camera.name +
                    " already=" + _submittedThisFrame);
            }

            _submittedThisFrame = true;

            // Submit from Unity's render thread. Calling OpenVR directly from
            // this main-thread callback makes the compositor client touch the
            // D3D11 device from the wrong thread, which deadlocks Present.
            if (VRNative.Ready)
            {
                VRNative.SetTextures(_leftNativeTex, _rightNativeTex);
                GL.IssuePluginEvent(VRNative.EventFunc, 1);
                return;
            }

            if (_deferSubmit)
            {
                _pendingSubmit = true;
                return;
            }
            Submit(EVREye.Eye_Left, _leftNativeTex);
            Submit(EVREye.Eye_Right, _rightNativeTex);
        }

        private void Submit(EVREye eye, IntPtr nativeTexture)
        {
            if (_noSubmit || nativeTexture == IntPtr.Zero) return;
            try
            {
                var tex = new Texture_t
                {
                    handle = nativeTexture,
                    eType = ETextureType.DirectX,
                    eColorSpace = EColorSpace.Gamma
                };
                // Unity's D3D11 render textures are stored bottom-up; flip V
                // so the compositor does not show the view upside down.
                var bounds = new VRTextureBounds_t { uMin = 0f, vMin = 1f, uMax = 1f, vMax = 0f };
                var err = _compositor.Submit(eye, ref tex, ref bounds, EVRSubmitFlags.Submit_Default);
                if (err != EVRCompositorError.None && !_submitErrorLogged)
                {
                    _submitErrorLogged = true;
                    BerryVRPlugin.Log.LogWarning("[VR] Submit " + eye + ": " + err);
                }
            }
            catch (Exception e)
            {
                if (!_submitErrorLogged)
                {
                    _submitErrorLogged = true;
                    BerryVRPlugin.Log.LogWarning("[VR] Submit exception: " + e.Message);
                }
            }
        }

        private void OnGUI()
        {
            if (!Active || _noMirror || _noStereo || _leftRT == null) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            float w = Screen.width;
            float h = w * _rtHeight / _rtWidth;
            if (h > Screen.height) { h = Screen.height; w = h * _rtWidth / _rtHeight; }
            GUI.DrawTexture(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h),
                _leftRT, ScaleMode.StretchToFill, false);
        }

        private static float NormalizeAngle(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }
    }
}
