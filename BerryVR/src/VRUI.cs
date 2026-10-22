using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Valve.VR;

namespace BerryVR
{
    /// <summary>Marker added to canvases converted by the VR UI layer.</summary>
    public sealed class VRPanelMarker : MonoBehaviour
    {
    }

    /// <summary>
    /// Converts the game's screen-space canvases into world-space panels and
    /// drives them with a laser pointer from the right controller.
    ///
    /// The canvases are deliberately NOT re-parented: they stay in the scene
    /// hierarchy (so the game can enable/destroy them normally) and are simply
    /// positioned/scaled in world space every frame from a head-relative anchor.
    /// </summary>
    public class VRUI : MonoBehaviour
    {
        private sealed class Panel
        {
            public Canvas canvas;
            public RectTransform rect;
            public GraphicRaycaster raycaster;
            public CanvasScaler scaler;
            public AdaptiveCanvasScaler[] adaptives;
            public bool sized;
            public int order;
            public float depth;
        }

        /// <summary>
        /// An authored world-space canvas (PC screen, slot machine, ...) that is
        /// left exactly as the game built it and only made clickable by the VR
        /// laser. It is never moved, rescaled or re-sorted.
        /// </summary>
        private sealed class WorldPanel
        {
            public Canvas canvas;
            public GraphicRaycaster raycaster;
            public Camera cam;
        }

        private VRManager _mgr;
        private CVRCompositor _compositor;
        private Camera _eventCam;
        private string _scene = "";

        private readonly List<Panel> _panels = new List<Panel>();
        private readonly List<WorldPanel> _worldPanels = new List<WorldPanel>();
        private readonly HashSet<int> _ignoredWorldCanvases = new HashSet<int>();
        private readonly List<RaycastResult> _results = new List<RaycastResult>();

        private PointerEventData _ped;
        private EventSystem _pedEventSystem;
        private GameObject _hover;
        private GameObject _pressed;
        private GameObject _drag;
        private RaycastResult _lastHitResult;
        private Vector2 _lastPointerPos;
        private float _scrollCooldown;

        // Press state for this frame: from the trigger, or from the index
        // fingertip touching a panel.
        private bool _pressEdge, _releaseEdge, _pressHeld;
        private bool _pokeTouching;
        private float _pokeDepth;
        private const float PokeHoverRange = 0.07f;  // fingertip distance that takes over the laser
        private const float PokeTouch = 0.008f;      // depth that counts as a press
        private const float PokeRelease = 0.02f;     // hysteresis before releasing
        private bool _hadUi;
        private bool _menuWasOpen;
        private float _nextRescan;
        private float _nextModuleCheck;
        private int _layoutLogs;

        private Vector3 _panelPos;
        private Quaternion _panelRot = Quaternion.identity;
        private bool _placed;
        private float _placeAt;
        private float _lookAwaySince = -1f;
        private bool _layoutReady;
        private bool _hudLayout;
        private float _layoutWidth = MenuWidth;

        private GameObject _laserGo;
        private LineRenderer _laser;
        private GameObject _dot;
        private Renderer _dotRenderer;
        private Material _laserMat;
        private Material _dotWorldMat;
        private Material _dotUiMat;

        private const float MenuDistance = 1.85f;
        private const float MenuWidth = 2.10f;
        private const float MenuDrop = 0.07f;
        private const float HudDistance = 1.60f;
        private const float HudWidth = 1.45f;
        private const float HudDrop = 0.04f;
        private const float RescanInterval = 1.5f;

        private static readonly Color WorldColor = new Color(1f, 1f, 1f, 0.30f);
        private static readonly Color UiColor = new Color(0.25f, 0.85f, 1f, 0.95f);
        private static readonly Color DotWorldColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color DotUiColor = new Color(0.25f, 0.85f, 1f, 1f);

        public void Init(VRManager mgr, CVRCompositor compositor)
        {
            _mgr = mgr;
            _compositor = compositor;
            CreateLaser();
        }

        public void Refresh(string scene, Camera cam)
        {
            _scene = scene;
            _eventCam = cam;
            _hover = null;
            _pressed = null;
            _drag = null;
            _hadUi = false;
            _menuWasOpen = false;
            _ped = null;
            _pedEventSystem = null;
            _placed = false;
            _layoutReady = false;
            _placeAt = 0f;
            _lookAwaySince = -1f;

            _panels.Clear();
            _worldPanels.Clear();
            _ignoredWorldCanvases.Clear();

            Rescan(true, true);
            DisableInputModules();

            BerryVRPlugin.Log.LogInfo("[VR] UI canvases converted: " + _panels.Count + " in " + scene);
        }

        public void SnapInFront()
        {
            _placed = false;
            _placeAt = 0f;
            _lookAwaySince = -1f;
        }

        public void OnSceneUnloaded()
        {
            _panels.Clear();
            _worldPanels.Clear();
            _ignoredWorldCanvases.Clear();
            _eventCam = null;
            _hover = null;
            _pressed = null;
            _drag = null;
            _ped = null;
            _pedEventSystem = null;
            _placed = false;
            _layoutReady = false;
        }

        private bool MenuOpen
        {
            get
            {
                if (_scene != "Game") return true;
                return GenericMenuManager.Singleton != null &&
                       GenericMenuManager.Singleton.menuState == GenericMenuManager.MenuState.open;
            }
        }

        public bool IsMenuOpen => MenuOpen;

        /// <summary>True while the laser dot is drawn by the UI layer.</summary>
        public bool LaserVisible { get; private set; }

        public void Tick()
        {
            if (_mgr == null || !_mgr.Active) return;

            if (Time.unscaledTime >= _nextModuleCheck)
            {
                _nextModuleCheck = Time.unscaledTime + 3f;
                DisableInputModules();
            }

            bool menuOpen = MenuOpen;
            bool enteredMenu = menuOpen && !_menuWasOpen;
            _menuWasOpen = menuOpen;

            // The gameplay HUD lives in the same canvas as the pause/shop
            // menus. Keep the canvas visible during the whole Game scene, just
            // like the flat game: the VCR intro and its full-screen backer are
            // part of this canvas, so hiding it during Intro/Cutscene would
            // hide the intro itself. Only wait for the GameManager before
            // showing anything, to avoid a one-frame flash while the scene
            // initializes.
            bool gameReady = _scene != "Game" || GameManager.Singleton != null;
            bool show = gameReady;
            bool hudOnly = show && !menuOpen;

            bool hudLayout = show && !menuOpen;
            if (!_layoutReady || hudLayout != _hudLayout)
            {
                _layoutReady = true;
                _hudLayout = hudLayout;
                _placed = false;
                _placeAt = 0f;
                _lookAwaySince = -1f;
            }

            if (enteredMenu)
            {
                _placed = false;
                _placeAt = 0f;
                _lookAwaySince = -1f;
            }

            if (_mgr.Head == null)
            {
                HideLaser();
                SuppressGameplay(false);
                return;
            }

            Rescan(false, false);
            SetPanelsVisible(show);
            if (!show)
            {
                HideLaser();
                ClearPointer();
                SuppressGameplay(false);
                return;
            }

            if (hudOnly)
                VRPatches.HideCrosshairs();

            // When a menu opens the panel must be in front of the player on
            // the very first rendered frame, even if they walked far away.
            if (enteredMenu && !_placed) PlacePanel(true);
            UpdatePanelPlacement();
            _eventCam = _mgr.LeftCamera != null ? _mgr.LeftCamera : _eventCam;
            ApplyLayout();

            if (_eventCam == null) return;

            EnsurePointer();

            // The laser is only shown over interactive targets: in menus it
            // drives the game panels, during gameplay it lets the player click
            // authored world-space UI (the PC screen, the slot machine, ...)
            // without duplicating the tracked aim indicator. If the controller
            // poses are not tracked, fall back to a head gaze pointer so menus
            // remain usable (the trigger is still readable through the legacy
            // path).
            bool canInteract = _mgr.Input != null && (_mgr.ControllersTracked || _mgr.Head != null);
            if (canInteract)
            {
                UpdateLaserAndInteraction(menuOpen);
                if (menuOpen) HandleScroll();
            }
            else
            {
                HideLaser();
                ClearPointer();
                SuppressGameplay(false);
            }
        }

        /// <summary>
        /// Blocks new gameplay trigger presses while a UI target is under the
        /// aim ray, so clicking a panel never throws the held object. The raw
        /// trigger value stays available to the UI layer for the click itself.
        /// </summary>
        private void SuppressGameplay(bool active)
        {
            if (_mgr == null || _mgr.Input == null) return;
            _mgr.Input.SuppressGameplayTrigger = active;
            if (!active) _mgr.Input.ExternalScroll = 0f;
        }

        // ------------------------------------------------------------------
        // Canvas discovery / conversion
        // ------------------------------------------------------------------

        private void Rescan(bool force, bool verbose)
        {
            if (!force && Time.unscaledTime < _nextRescan) return;
            _nextRescan = Time.unscaledTime + RescanInterval;

            for (int i = _panels.Count - 1; i >= 0; i--)
            {
                var p = _panels[i];
                if (p == null || p.canvas == null || p.rect == null ||
                    p.canvas.gameObject == null || p.raycaster == null)
                {
                    _panels.RemoveAt(i);
                }
            }

            for (int i = _worldPanels.Count - 1; i >= 0; i--)
            {
                var w = _worldPanels[i];
                if (w == null || w.canvas == null || w.canvas.gameObject == null || w.raycaster == null)
                    _worldPanels.RemoveAt(i);
            }

            var found = Resources.FindObjectsOfTypeAll<Canvas>();
            int added = 0;
            int worldAdded = 0;

            for (int i = 0; i < found.Length; i++)
            {
                var cv = found[i];
                if (cv == null || !cv.isRootCanvas) continue;

                var go = cv.gameObject;
                if (go == null) continue;

                var sc = go.scene;
                if (!sc.IsValid() || !sc.isLoaded) continue;

                bool tagged = go.GetComponent<VRPanelMarker>() != null;
                if (!tagged && cv.renderMode == RenderMode.WorldSpace)
                {
                    // Authored 3D UI (world-space props, floating text): leave it
                    // exactly as the game built it. Canvases that contain real
                    // clickable controls get laser support below without being
                    // moved.
                    TryAddWorldPanel(cv, ref worldAdded);
                    continue;
                }

                if (!tagged)
                {
                    go.AddComponent<VRPanelMarker>();
                    added++;
                }

                // The game sometimes recreates a canvas with the same GameObject
                // but screen-space mode (or a new canvas appears at runtime).
                if (cv.renderMode != RenderMode.WorldSpace)
                    cv.renderMode = RenderMode.WorldSpace;
                cv.worldCamera = _eventCam;
                cv.scaleFactor = 1f;

                var rt = cv.transform as RectTransform;
                if (rt == null) continue;

                var gr = go.GetComponent<GraphicRaycaster>();
                if (gr == null) gr = go.AddComponent<GraphicRaycaster>();
                gr.enabled = true;

                if (!HasPanel(cv))
                {
                    // Make sure the eye cameras can see whatever layer the game
                    // put this canvas on. The standard UI layer is included as
                    // well because children may live on it.
                    int layer = go.layer;
                    int uiLayer = LayerMask.NameToLayer("UI");
                    if (_mgr != null)
                    {
                        if (_mgr.LeftCamera != null)
                        {
                            _mgr.LeftCamera.cullingMask |= 1 << layer;
                            if (uiLayer >= 0) _mgr.LeftCamera.cullingMask |= 1 << uiLayer;
                        }
                        if (_mgr.RightCamera != null)
                        {
                            _mgr.RightCamera.cullingMask |= 1 << layer;
                            if (uiLayer >= 0) _mgr.RightCamera.cullingMask |= 1 << uiLayer;
                        }
                    }

                    var p = new Panel
                    {
                        canvas = cv,
                        rect = rt,
                        raycaster = gr,
                        scaler = go.GetComponent<CanvasScaler>(),
                        adaptives = go.GetComponentsInChildren<AdaptiveCanvasScaler>(true)
                    };
                    NormalizeRootRect(p, true);
                    DisableScalers(p);
                    _panels.Add(p);
                }
            }

            _panels.Sort(ComparePanels);
            for (int i = 0; i < _panels.Count; i++)
            {
                _panels[i].order = i;
                // Highest sorting order sits closest to the viewer. The small Z
                // offsets also keep overlapping canvases stable for the eyes.
                _panels[i].depth = i * 0.004f;
            }

            if (verbose)
                BerryVRPlugin.Log.LogInfo("[VR] UI rescan: " + _panels.Count + " panels (" + added + " new), " +
                    _worldPanels.Count + " world panels (" + worldAdded + " new).");
        }

        /// <summary>
        /// Registers an authored world-space canvas as laser-clickable when it
        /// contains real controls. Classification runs once per canvas and is
        /// cached, so the cost stays out of the steady-state frame budget.
        /// </summary>
        private void TryAddWorldPanel(Canvas cv, ref int worldAdded)
        {
            var go = cv.gameObject;
            int id = cv.GetInstanceID();
            if (_ignoredWorldCanvases.Contains(id)) return;
            if (HasWorldPanel(cv)) return;

            var gr = go.GetComponent<GraphicRaycaster>();
            if (gr == null || !HasInteractiveContent(go))
            {
                _ignoredWorldCanvases.Add(id);
                return;
            }

            gr.enabled = true;
            _worldPanels.Add(new WorldPanel
            {
                canvas = cv,
                raycaster = gr,
                cam = cv.worldCamera
            });
            worldAdded++;
        }

        private bool HasWorldPanel(Canvas cv)
        {
            for (int i = 0; i < _worldPanels.Count; i++)
            {
                var w = _worldPanels[i];
                if (w != null && w.canvas == cv) return true;
            }
            return false;
        }

        private static bool HasInteractiveContent(GameObject root)
        {
            var mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < mbs.Length; i++)
            {
                var mb = mbs[i];
                if (mb == null) continue;
                if (mb is IPointerClickHandler || mb is IPointerDownHandler ||
                    mb is IPointerUpHandler || mb is IDragHandler || mb is IScrollHandler)
                    return true;
            }
            return false;
        }

        private static int ComparePanels(Panel a, Panel b)
        {
            int c = PanelPriority(b.canvas).CompareTo(PanelPriority(a.canvas));
            if (c != 0) return c;

            bool da = a.canvas.gameObject.scene.name == "DontDestroyOnLoad";
            bool db = b.canvas.gameObject.scene.name == "DontDestroyOnLoad";
            if (da != db) return da ? -1 : 1;

            return b.canvas.GetInstanceID().CompareTo(a.canvas.GetInstanceID());
        }

        private static int PanelPriority(Canvas c)
        {
            int p = c.sortingOrder;
            // The settings panel is a separate DontDestroyOnLoad canvas that can
            // be opened on top of any other menu. Keep it in front even if the
            // scene author used the same sorting order everywhere.
            string n = c.gameObject.name;
            if (n.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0) p += 10000;
            return p;
        }

        private bool HasPanel(Canvas cv)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                var p = _panels[i];
                if (p != null && p.canvas == cv) return true;
            }
            return false;
        }

        private static void NormalizeRootRect(Panel p, bool force)
        {
            if (p.rect == null) return;
            if (!force && p.rect.rect.width >= 10f && p.rect.rect.height >= 10f) return;

            var scaler = p.scaler;
            Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920f, 1080f);
            if (reference.x < 10f) reference.x = 1920f;
            if (reference.y < 10f) reference.y = 1080f;

            p.rect.anchorMin = new Vector2(0.5f, 0.5f);
            p.rect.anchorMax = new Vector2(0.5f, 0.5f);
            p.rect.pivot = new Vector2(0.5f, 0.5f);
            p.rect.anchoredPosition = Vector2.zero;
            p.rect.sizeDelta = reference;
            p.sized = true;
        }

        private static void DisableScalers(Panel p)
        {
            if (p == null) return;
            if (p.scaler != null && p.scaler.enabled)
                p.scaler.enabled = false;
            if (p.adaptives != null)
            {
                for (int i = 0; i < p.adaptives.Length; i++)
                {
                    var a = p.adaptives[i];
                    if (a != null && a.enabled) a.enabled = false;
                }
            }
        }

        private void SetPanelsVisible(bool visible)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                var p = _panels[i];
                if (p != null && p.canvas != null && p.canvas.enabled != visible)
                    p.canvas.enabled = visible;
            }
        }

        // ------------------------------------------------------------------
        // Placement
        // ------------------------------------------------------------------

        private void UpdatePanelPlacement()
        {
            var head = _mgr.Head;
            if (head == null) return;

            float now = Time.unscaledTime;

            if (!_placed)
            {
                if (now < _placeAt) return;
                PlacePanel(true);
                return;
            }

            // The gameplay HUD is head-locked: it follows the head yaw every
            // frame so the player always finds their timers where they expect
            // them. Menus use the lazy placement below so reading them does
            // not feel glued to the face.
            if (_hudLayout)
            {
                ComputePanelTarget(head, out Vector3 hudTarget, out Quaternion hudRot);
                _panelPos = hudTarget;
                _panelRot = hudRot;
                return;
            }

            // Keep the panel reachable when a seated player turns a long way,
            // but do not glue it to the head: looking around stays natural.
            Vector3 toPanel = _panelPos - head.position;
            toPanel.y = 0f;
            Vector3 fwd = head.forward;
            fwd.y = 0f;

            if (toPanel.sqrMagnitude > 0.04f && fwd.sqrMagnitude > 0.04f)
            {
                float angle = Vector3.Angle(fwd.normalized, toPanel.normalized);
                if (angle > 55f)
                {
                    if (_lookAwaySince < 0f) _lookAwaySince = now;
                    else if (now - _lookAwaySince > 1.1f)
                    {
                        PlacePanel(false);
                        _lookAwaySince = -1f;
                    }
                }
                else
                {
                    _lookAwaySince = -1f;
                }
            }
        }

        private void PlacePanel(bool instant)
        {
            var head = _mgr.Head;
            if (head == null) return;

            _layoutWidth = _hudLayout ? HudWidth : MenuWidth;
            ComputePanelTarget(head, out Vector3 target, out Quaternion rot);

            if (instant || !_placed)
            {
                _panelPos = target;
                _panelRot = rot;
            }
            else
            {
                float a = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
                _panelPos = Vector3.Lerp(_panelPos, target, a);
                _panelRot = Quaternion.Slerp(_panelRot, rot, a);
            }
            _placed = true;
        }

        private void ComputePanelTarget(Transform head, out Vector3 target, out Quaternion rot)
        {
            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            float distance = _hudLayout ? HudDistance : MenuDistance;
            float drop = _hudLayout ? HudDrop : MenuDrop;

            target = head.position + fwd * distance;
            target.y = head.position.y - drop;
            rot = Quaternion.LookRotation(fwd, Vector3.up);
        }

        private void ApplyLayout()
        {
            if (!_placed || _panels.Count == 0) return;

            for (int i = 0; i < _panels.Count; i++)
            {
                var p = _panels[i];
                if (p == null || p.canvas == null || p.rect == null) continue;
                if (!p.sized || p.rect.rect.width < 10f || p.rect.rect.height < 10f)
                {
                    NormalizeRootRect(p, true);
                    if (p.rect.rect.width < 10f || p.rect.rect.height < 10f) continue;
                }

                DisableScalers(p);

                // A menu transition can rebuild the canvas or switch it back to
                // screen space. Keep every managed panel world-space with the
                // left eye as its event camera.
                if (p.canvas.renderMode != RenderMode.WorldSpace)
                    p.canvas.renderMode = RenderMode.WorldSpace;
                if (p.canvas.worldCamera != _eventCam)
                    p.canvas.worldCamera = _eventCam;
                if (p.canvas.scaleFactor != 1f)
                    p.canvas.scaleFactor = 1f;

                // Once converted to world space, the authored canvas sorting
                // no longer separates an Overlay options canvas from the
                // ScreenSpaceCamera HUD. Drive it from our priority order so
                // the front-most panel is always drawn last.
                int wantedOrder = 20000 - p.order;
                if (p.canvas.sortingOrder != wantedOrder)
                    p.canvas.sortingOrder = wantedOrder;

                Vector3 pos = _panelPos + _panelRot * new Vector3(0f, 0f, p.depth);
                p.canvas.transform.SetPositionAndRotation(pos, _panelRot);

                if (_layoutLogs < 12)
                {
                    _layoutLogs++;
                    BerryVRPlugin.Log.LogInfo("[VR] Panel '" + p.canvas.name + "'" +
                        " rect=" + p.rect.rect.size.ToString("F0") +
                        " scale=" + p.rect.lossyScale.ToString("F4") +
                        " pos=" + pos.ToString("F3") +
                        " rot=" + _panelRot.eulerAngles.ToString("F1") +
                        " cam=" + (_eventCam != null ? _eventCam.transform.position.ToString("F3") : "null") +
                        " camFwd=" + (_eventCam != null ? _eventCam.transform.forward.ToString("F2") : "null"));
                }

                // The canvases keep their scene parents, which may carry a
                // scale. Compensate so the world-space width is exact.
                Transform parent = p.rect.parent;
                Vector3 lossy = parent != null ? parent.lossyScale : Vector3.one;
                float sx = Mathf.Max(1e-5f, Mathf.Abs(lossy.x));
                float sy = Mathf.Max(1e-5f, Mathf.Abs(lossy.y));
                float sz = Mathf.Max(1e-5f, Mathf.Abs(lossy.z));
                float s = _layoutWidth / p.rect.rect.width;
                p.rect.localScale = new Vector3(s / sx, s / sy, s / sz);
            }
        }

        private void ClearPointer()
        {
            if (_hover != null)
            {
                SendExit(_hover);
                _hover = null;
            }
            _pressed = null;
            _drag = null;
            _hadUi = false;
            _lastHitResult = default;
        }

        private void DisableInputModules()
        {
            foreach (var es in Resources.FindObjectsOfTypeAll<EventSystem>())
            {
                if (es == null || es.gameObject == null) continue;
                var sc = es.gameObject.scene;
                if (!sc.IsValid() || !sc.isLoaded) continue;

                foreach (var m in es.GetComponents<BaseInputModule>())
                {
                    if (m != null && m.enabled)
                        m.enabled = false;
                }
            }
        }

        private void EnsurePointer()
        {
            if (_ped != null && _pedEventSystem == EventSystem.current) return;

            var es = EventSystem.current;
            if (es == null)
            {
                var go = new GameObject("BerryVR_EventSystem");
                go.AddComponent<EventSystem>();
                es = go.GetComponent<EventSystem>();
                BerryVRPlugin.Log.LogInfo("[VR] Created UI EventSystem.");
            }

            _pedEventSystem = es;
            _ped = new PointerEventData(es)
            {
                pointerId = -1,
                button = PointerEventData.InputButton.Left
            };
            _pressed = null;
            _drag = null;
            _hover = null;
        }

        // ------------------------------------------------------------------
        // Laser / UGUI interaction
        // ------------------------------------------------------------------

        private void UpdateLaserAndInteraction(bool menuOpen)
        {
            if (_ped == null || _eventCam == null) return;

            Transform aim = _mgr.ControllersTracked && _mgr.RightAim != null ? _mgr.RightAim : _mgr.Head;
            if (aim == null) return;

            var ray = new Ray(aim.position, aim.forward);

            RaycastResult best = default;
            bool hasHit = false;
            bool hitWorldPanel = false;
            Vector3 bestWorld = Vector3.zero;
            Vector2 bestScreen = Vector2.zero;
            _lastHitResult = default;

            bool poke = TryPoke(menuOpen, ref best, ref bestWorld, ref bestScreen, ref hasHit, ref hitWorldPanel);
            UpdatePressState(poke);

            // Converted screen-space panels are only interactive in menus: the
            // gameplay HUD sits right in front of the player and must not eat
            // clicks during play. A fingertip touch already chose its target.
            if (!poke && menuOpen)
            {
                for (int pi = 0; pi < _panels.Count; pi++)
                {
                    var p = _panels[pi];
                    if (p == null || p.canvas == null || !p.canvas.isActiveAndEnabled) continue;
                    if (!p.canvas.gameObject.activeInHierarchy) continue;
                    if (p.raycaster == null || !p.raycaster.isActiveAndEnabled) continue;

                    if (TryHitCanvas(p.canvas, p.raycaster, _eventCam, ray,
                            ref best, ref bestWorld, ref bestScreen, ref hasHit))
                        hitWorldPanel = false;
                }
            }

            // Authored world-space UI (the PC screen, the slot machine, ...)
            // stays wherever the game placed it and is reachable at any time.
            // In menus a hit on the converted game panel takes priority over
            // any world-space prop in the way (the player may stand right next
            // to the PC when the pause menu opens).
            if (!poke && (!menuOpen || !hasHit))
            {
                for (int wi = 0; wi < _worldPanels.Count; wi++)
                {
                    var w = _worldPanels[wi];
                    if (w == null || w.canvas == null || w.raycaster == null) continue;
                    if (!w.canvas.isActiveAndEnabled || !w.canvas.gameObject.activeInHierarchy) continue;
                    if (!w.raycaster.isActiveAndEnabled) w.raycaster.enabled = true;

                    var cam = w.cam;
                    if (cam == null) cam = w.raycaster.eventCamera;
                    if (cam == null) cam = _eventCam;

                    if (TryHitCanvas(w.canvas, w.raycaster, cam, ray,
                            ref best, ref bestWorld, ref bestScreen, ref hasHit))
                        hitWorldPanel = true;
                }
            }

            if (hasHit)
            {
                _lastHitResult = best;
                _ped.position = bestScreen;
                _ped.pointerCurrentRaycast = best;
                if (best.worldPosition != Vector3.zero) bestWorld = best.worldPosition;

                HandleHover(best.gameObject);
                HandleTrigger(best.gameObject, bestWorld);
                HandleDrag();

                // Touching with the finger needs no beam, only the dot.
                ShowLaser(poke ? bestWorld : LaserStart(aim), bestWorld, true);
                SuppressGameplay(true);
                if (_mgr.Input != null)
                    _mgr.Input.ExternalScroll = hitWorldPanel ? _mgr.Input.Turn.y : 0f;
            }
            else
            {
                _lastHitResult = default;
                _ped.pointerCurrentRaycast = default;
                if (_hover != null)
                {
                    SendExit(_hover);
                    _hover = null;
                }
                HandleTrigger(null, Vector3.zero);

                if (menuOpen)
                {
                    Vector3 start = LaserStart(aim);
                    Vector3 end = aim.position + aim.forward * 8f;
                    if (Physics.Raycast(ray, out var wh, 12f, ~0, QueryTriggerInteraction.Ignore))
                        end = wh.point;
                    ShowLaser(start, end, false);
                }
                else
                {
                    HideLaser();
                }
                SuppressGameplay(false);
            }
        }

        /// <summary>The beam is drawn from the glove's index fingertip when it is visible.</summary>
        private Vector3 LaserStart(Transform aim)
        {
            if (_mgr.RightIndexTipValid && aim == _mgr.RightAim)
                return _mgr.RightIndexTip;
            return aim.position;
        }

        /// <summary>
        /// Finger touch: when the right index fingertip is within a few
        /// centimetres in front of a panel, it drives the pointer instead of
        /// the laser, along the panel normal.
        /// </summary>
        private bool TryPoke(bool menuOpen, ref RaycastResult best, ref Vector3 bestWorld,
            ref Vector2 bestScreen, ref bool hasHit, ref bool hitWorldPanel)
        {
            if (VRSettings.FingerTouchUI != null && !VRSettings.FingerTouchUI.Value) return false;
            if (!_mgr.RightIndexTipValid) return false;
            Vector3 tip = _mgr.RightIndexTip;

            Canvas bestCanvas = null;
            GraphicRaycaster bestRaycaster = null;
            Camera bestCam = null;
            bool bestIsWorld = false;
            float bestDepth = 0f;
            float bestAbs = float.MaxValue;

            if (menuOpen)
            {
                for (int i = 0; i < _panels.Count; i++)
                {
                    var p = _panels[i];
                    if (p == null || p.canvas == null || !p.canvas.isActiveAndEnabled) continue;
                    if (p.raycaster == null || !p.raycaster.isActiveAndEnabled) continue;
                    ConsiderPoke(p.canvas, p.raycaster, _eventCam, false, tip,
                        ref bestCanvas, ref bestRaycaster, ref bestCam, ref bestIsWorld, ref bestDepth, ref bestAbs);
                }
            }
            for (int i = 0; i < _worldPanels.Count; i++)
            {
                var w = _worldPanels[i];
                if (w == null || w.canvas == null || w.raycaster == null) continue;
                if (!w.canvas.isActiveAndEnabled || !w.canvas.gameObject.activeInHierarchy) continue;
                var cam = w.cam != null ? w.cam : (w.raycaster.eventCamera != null ? w.raycaster.eventCamera : _eventCam);
                ConsiderPoke(w.canvas, w.raycaster, cam, true, tip,
                    ref bestCanvas, ref bestRaycaster, ref bestCam, ref bestIsWorld, ref bestDepth, ref bestAbs);
            }
            if (bestCanvas == null) return false;

            Vector3 n = bestCanvas.transform.forward;
            var ray = new Ray(tip - n * 0.1f, n);
            bool hit = false;
            RaycastResult r = default;
            Vector3 world = Vector3.zero;
            Vector2 screen = Vector2.zero;
            if (!TryHitCanvas(bestCanvas, bestRaycaster, bestCam, ray, ref r, ref world, ref screen, ref hit) || !hit)
                return false;

            best = r;
            bestWorld = world;
            bestScreen = screen;
            hasHit = true;
            hitWorldPanel = bestIsWorld;
            _pokeDepth = bestDepth;
            return true;
        }

        private static void ConsiderPoke(Canvas canvas, GraphicRaycaster raycaster, Camera cam, bool isWorld,
            Vector3 tip, ref Canvas bestCanvas, ref GraphicRaycaster bestRaycaster, ref Camera bestCam,
            ref bool bestIsWorld, ref float bestDepth, ref float bestAbs)
        {
            if (cam == null) return;
            // Canvas +Z points away from the viewer: negative depth is in front.
            float depth = Vector3.Dot(tip - canvas.transform.position, canvas.transform.forward);
            if (depth < -PokeHoverRange || depth > 0.04f) return;
            float abs = Mathf.Abs(depth);
            if (abs >= bestAbs) return;
            bestAbs = abs;
            bestDepth = depth;
            bestCanvas = canvas;
            bestRaycaster = raycaster;
            bestCam = cam;
            bestIsWorld = isWorld;
        }

        private void UpdatePressState(bool poke)
        {
            if (poke)
            {
                bool touching = _pokeTouching ? _pokeDepth > -PokeRelease : _pokeDepth > -PokeTouch;
                _pressEdge = touching && !_pokeTouching;
                _releaseEdge = !touching && _pokeTouching;
                _pressHeld = touching;
                _pokeTouching = touching;
                return;
            }

            if (_pokeTouching)
            {
                // The finger slid off the panel while pressing.
                _pokeTouching = false;
                _pressEdge = false;
                _releaseEdge = true;
                _pressHeld = false;
                return;
            }

            _pressEdge = _mgr.Input.RightTriggerPressed;
            _releaseEdge = _mgr.Input.RightTriggerReleased;
            _pressHeld = _mgr.Input.RightTriggerValue > 0.5f;
        }

        /// <summary>
        /// Intersects the aim ray with a canvas plane and runs its raycaster.
        /// Returns true when one of its graphics became the new best target.
        /// </summary>
        private bool TryHitCanvas(Canvas canvas, GraphicRaycaster raycaster, Camera cam,
            Ray ray, ref RaycastResult best, ref Vector3 bestWorld, ref Vector2 bestScreen, ref bool hasHit)
        {
            if (canvas == null || raycaster == null || cam == null) return false;

            var plane = new Plane(canvas.transform.forward, canvas.transform.position);
            if (!plane.Raycast(ray, out float enter)) return false;
            if (enter < 0.05f || enter > 6.5f) return false;
            // Only the front face (canvas +Z pointing away from the viewer) is
            // interactive.
            if (Vector3.Dot(canvas.transform.forward, ray.direction) < 0.05f) return false;

            Vector3 world = ray.GetPoint(enter);
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return false;

            var pos = new Vector2(screen.x, screen.y);
            _ped.position = pos;
            _results.Clear();
            raycaster.Raycast(_ped, _results);

            // Hover tooltips (the upgrade description, ...) pop up right under
            // the laser. They are not interactive themselves, so when they
            // cover a control let the ray pass through to the control; else
            // the hover flips between the two every frame and both flicker.
            bool anyInteractive = false;
            for (int i = 0; i < _results.Count; i++)
            {
                if (IsInteractive(_results[i].gameObject)) { anyInteractive = true; break; }
            }

            bool improved = false;
            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                if (r.gameObject == null) continue;
                if (anyInteractive && !IsInteractive(r.gameObject)) continue;
                // GraphicRaycaster distances are measured from the event
                // camera, so they are directly comparable between panels.
                if (!hasHit || r.distance < best.distance)
                {
                    best = r;
                    bestWorld = world;
                    bestScreen = pos;
                    hasHit = true;
                    improved = true;
                }
            }
            return improved;
        }

        private static bool IsInteractive(GameObject go)
        {
            if (go == null) return false;
            return ExecuteEvents.GetEventHandler<IPointerEnterHandler>(go) != null ||
                   ExecuteEvents.GetEventHandler<IPointerDownHandler>(go) != null ||
                   ExecuteEvents.GetEventHandler<IPointerClickHandler>(go) != null;
        }

        private void HandleHover(GameObject go)
        {
            // Track the element that handles the hover, not the exact graphic
            // under the ray: moving from a button's background to its label
            // must not send exit + enter to the same button.
            if (go != null)
            {
                var handler = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(go);
                if (handler != null) go = handler;
            }

            if (go == _hover)
            {
                if (_hover != null) SendMove(_hover);
                return;
            }

            if (_hover != null) SendExit(_hover);
            _hover = go;

            if (go != null)
            {
                SendEnter(go);
                if (_mgr.Input != null) _mgr.Input.PulseRight(450);
            }
        }

        private void HandleTrigger(GameObject go, Vector3 hitWorld)
        {
            if (_mgr.Input == null || _ped == null) return;

            if (_pressEdge)
            {
                if (go == null)
                {
                    _pressed = null;
                    _drag = null;
                    return;
                }

                _pressed = go;
                _lastPointerPos = _ped.position;
                _ped.pressPosition = _ped.position;
                _ped.clickTime = Time.unscaledTime;
                _ped.clickCount = 1;
                _ped.dragging = false;
                _ped.useDragThreshold = false;
                _ped.eligibleForClick = true;
                _ped.pointerPressRaycast = _lastHitResult;
                _ped.rawPointerPress = go;

                var down = ExecuteEvents.ExecuteHierarchy(go, _ped, ExecuteEvents.pointerDownHandler);
                if (down != null) _pressed = down;
                else
                {
                    var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
                    if (click != null) _pressed = click;
                }
                _ped.pointerPress = _pressed;

                var dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(go);
                if (dragTarget != null)
                {
                    _drag = dragTarget;
                    _ped.pointerDrag = dragTarget;
                    _ped.dragging = true;
                    ExecuteEvents.Execute(dragTarget, _ped, ExecuteEvents.initializePotentialDrag);
                    ExecuteEvents.Execute(dragTarget, _ped, ExecuteEvents.beginDragHandler);
                }
                _mgr.Input.PulseRight(1100);
            }
            else if (_releaseEdge)
            {
                bool wasDragging = _ped.dragging;

                if (_pressed != null)
                {
                    if (go != null) _ped.pointerCurrentRaycast = _lastHitResult;
                    else _ped.pointerCurrentRaycast = default;

                    ExecuteEvents.Execute(_pressed, _ped, ExecuteEvents.pointerUpHandler);

                    if (!wasDragging && go != null && _pressed != null &&
                        (go == _pressed || go.transform.IsChildOf(_pressed.transform)))
                    {
                        ExecuteEvents.Execute(_pressed, _ped, ExecuteEvents.pointerClickHandler);
                    }
                    _mgr.Input.PulseRight(700);
                }

                if (_drag != null)
                {
                    ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.endDragHandler);
                    _ped.dragging = false;
                    _ped.pointerDrag = null;
                }

                _ped.pointerPress = null;
                _ped.rawPointerPress = null;
                _ped.eligibleForClick = false;
                _pressed = null;
                _drag = null;
            }
        }

        private void HandleDrag()
        {
            if (_pressed == null || _drag == null || _ped == null) return;
            if (!_pressHeld) return;

            Vector2 delta = _ped.position - _lastPointerPos;
            _ped.delta = delta;
            _lastPointerPos = _ped.position;
            _ped.dragging = true;
            _ped.pointerDrag = _drag;
            ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.dragHandler);
        }

        private void HandleScroll()
        {
            if (_ped == null || _hover == null) return;

            if (_scrollCooldown > 0f)
            {
                _scrollCooldown -= Time.unscaledDeltaTime;
                return;
            }

            float y = _mgr.Input.Turn.y;
            if (Mathf.Abs(y) < 0.65f) return;

            var handler = ExecuteEvents.GetEventHandler<IScrollHandler>(_hover);
            if (handler != null)
            {
                // One notch is a few canvas units; 30 was far too coarse for the
                // game's ScrollRects.
                _ped.scrollDelta = new Vector2(0f, Mathf.Sign(y) * 8f);
                ExecuteEvents.Execute(handler, _ped, ExecuteEvents.scrollHandler);
            }
            _scrollCooldown = 0.14f;
        }

        private void SendEnter(GameObject go)
        {
            if (go == null) return;
            var handler = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(go);
            if (handler != null) ExecuteEvents.Execute(handler, _ped, ExecuteEvents.pointerEnterHandler);
        }

        private void SendExit(GameObject go)
        {
            if (go == null) return;
            var handler = ExecuteEvents.GetEventHandler<IPointerExitHandler>(go);
            if (handler != null) ExecuteEvents.Execute(handler, _ped, ExecuteEvents.pointerExitHandler);
        }

        private void SendMove(GameObject go)
        {
            if (go == null) return;
            var handler = ExecuteEvents.GetEventHandler<IPointerMoveHandler>(go);
            if (handler != null) ExecuteEvents.Execute(handler, _ped, ExecuteEvents.pointerMoveHandler);
        }

        // ------------------------------------------------------------------
        // Visuals
        // ------------------------------------------------------------------

        private void CreateLaser()
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null)
            {
                BerryVRPlugin.Log.LogWarning("[VR] No shader found for the UI laser.");
                return;
            }

            // Keep the material white: the LineRenderer vertex colours carry
            // the alpha, and multiplying both would make the laser invisible.
            _laserMat = new Material(sh) { name = "BerryVR_Laser" };
            SetColor(_laserMat, Color.white);
            _dotWorldMat = new Material(sh) { name = "BerryVR_DotWorld" };
            SetColor(_dotWorldMat, DotWorldColor);
            _dotUiMat = new Material(sh) { name = "BerryVR_DotUi" };
            SetColor(_dotUiMat, DotUiColor);

            _laserGo = new GameObject("BerryVR_Laser");
            _laserGo.transform.SetParent(transform, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) _laserGo.layer = uiLayer;
            _laser = _laserGo.AddComponent<LineRenderer>();
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.startWidth = 0.0035f;
            _laser.endWidth = 0.0012f;
            _laser.numCapVertices = 4;
            _laser.sharedMaterial = _laserMat;
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.receiveShadows = false;
            _laser.textureMode = LineTextureMode.Stretch;
            _laser.startColor = WorldColor;
            _laser.endColor = WorldColor;
            _laser.sortingOrder = 30000;
            _laser.enabled = false;

            _dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dot.name = "BerryVR_Dot";
            if (uiLayer >= 0) _dot.layer = uiLayer;
            Destroy(_dot.GetComponent<Collider>());
            _dot.transform.SetParent(transform, false);
            _dot.transform.localScale = Vector3.one * 0.012f;
            _dotRenderer = _dot.GetComponent<Renderer>();
            if (_dotRenderer != null)
            {
                _dotRenderer.sharedMaterial = _dotWorldMat;
                _dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _dotRenderer.receiveShadows = false;
                _dotRenderer.sortingOrder = 30000;
            }
            _dot.SetActive(false);
        }

        private static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private void ShowLaser(Vector3 start, Vector3 end, bool ui)
        {
            LaserVisible = true;
            if (_laser == null) return;

            _laser.enabled = true;
            _laser.SetPosition(0, start);
            _laser.SetPosition(1, end);

            var c = ui ? UiColor : WorldColor;
            _laser.startColor = c;
            _laser.endColor = c;

            if (_dot != null)
            {
                _dot.SetActive(true);
                Vector3 dir = end - start;
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
                _dot.transform.position = end - dir * 0.01f;
                _dot.transform.localScale = Vector3.one * (ui ? 0.014f : 0.010f);
                if (_dotRenderer != null)
                    _dotRenderer.sharedMaterial = ui ? _dotUiMat : _dotWorldMat;
            }
        }

        private void HideLaser()
        {
            LaserVisible = false;
            if (_laser != null) _laser.enabled = false;
            if (_dot != null) _dot.SetActive(false);
        }
    }
}
