using UnityEngine;
using UnityEngine.Rendering;

namespace BerryVR
{
    /// <summary>
    /// Chunky cartoon gloves driven by the SteamVR hand skeleton. Every part is
    /// a primitive (capsule fingers, ellipsoid palm, cylinder cuff) placed on
    /// the tracked joints each frame, lit with a clone of one of the game's own
    /// URP/Lit materials and wrapped in an inverted-hull outline so they read
    /// like the game's low-poly cartoon props.
    /// </summary>
    public sealed class CartoonGlove
    {
        private sealed class Part
        {
            public Transform body;
            public Transform outline;
        }

        private const float OutlineWidth = 0.0022f;
        private const float PalmThickness = 0.040f;

        private static readonly float[] FingerRadius = { 0.0115f, 0.0105f, 0.0108f, 0.0101f, 0.0090f };
        private static readonly int[] FingerStart =
        {
            HandSkeleton.Thumb, HandSkeleton.Index, HandSkeleton.Middle, HandSkeleton.Ring, HandSkeleton.Pinky
        };

        private readonly GameObject _root;
        private readonly Part _palm;
        private readonly Part _cuff;
        private readonly Part[] _segments = new Part[15];
        private readonly Vector3[] _world = new Vector3[HandSkeleton.BoneCount];

        public bool Visible => _root != null && _root.activeSelf;

        /// <summary>World position of the index fingertip after the last Apply.</summary>
        public Vector3 IndexTip { get; private set; }

        /// <summary>World position of the hollow of the fist, where a held handle sits.</summary>
        public Vector3 FistCenter { get; private set; }

        public CartoonGlove(Transform parent, string name, Material skin, Material cuff, Material outline)
        {
            _root = new GameObject(name);
            _root.transform.SetParent(parent, false);

            _palm = MakePart(PrimitiveType.Sphere, skin, outline);
            _cuff = MakePart(PrimitiveType.Cylinder, cuff, outline);
            for (int i = 0; i < _segments.Length; i++)
                _segments[i] = MakePart(PrimitiveType.Capsule, skin, outline);

            _root.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
        }

        /// <summary>Places the glove on the skeleton, given the root pose in world space.</summary>
        public void Apply(HandSkeleton hand, Vector3 rootPos, Quaternion rootRot, float thickness = 1f)
        {
            for (int i = 0; i < HandSkeleton.BoneCount; i++)
                _world[i] = rootPos + rootRot * hand.pos[i];

            IndexTip = _world[HandSkeleton.Index + 4];
            Vector3 wrist = _world[HandSkeleton.Wrist];
            Vector3 kIndex = _world[HandSkeleton.Index + 1];
            Vector3 kMiddle = _world[HandSkeleton.Middle + 1];
            Vector3 kRing = _world[HandSkeleton.Ring + 1];
            Vector3 kPinky = _world[HandSkeleton.Pinky + 1];
            Vector3 knuckles = (kIndex + kMiddle + kRing + kPinky) * 0.25f;

            Vector3 fwd = knuckles - wrist;
            float palmLength = fwd.magnitude;
            if (palmLength < 1e-4f) return;
            fwd /= palmLength;

            Vector3 side = kIndex - kPinky;
            float palmWidth = side.magnitude;
            side = Vector3.ProjectOnPlane(side, fwd);
            if (side.sqrMagnitude < 1e-8f) return;
            side.Normalize();
            Vector3 normal = Vector3.Cross(fwd, side);

            // Palm: an ellipsoid spanning wrist to knuckles, wide enough to
            // cover the base of all four fingers.
            Quaternion palmRot = Quaternion.LookRotation(fwd, normal);
            Vector3 palmCenter = (wrist + knuckles) * 0.5f;
            // Halfway between the palm and the middle finger's second joint:
            // inside the curl of the fingers when the hand is closed.
            FistCenter = Vector3.Lerp(palmCenter, _world[HandSkeleton.Middle + 2], 0.5f);
            Place(_palm, palmCenter, palmRot,
                new Vector3(palmWidth + 2f * FingerRadius[1] * thickness + 0.006f, PalmThickness * thickness, palmLength + 0.03f));

            // Cuff: a short flared cylinder behind the wrist, axis along the arm.
            Quaternion cuffRot = Quaternion.LookRotation(normal, fwd);
            Place(_cuff, wrist - fwd * 0.03f, cuffRot,
                new Vector3(palmWidth + 0.045f * thickness, 0.022f, PalmThickness * thickness + 0.03f));

            int s = 0;
            for (int f = 0; f < 5; f++)
            {
                // Thumb chain: metacarpal, proximal, distal, tip. Other
                // fingers skip their metacarpal, which sits inside the palm.
                int first = f == 0 ? FingerStart[f] : FingerStart[f] + 1;
                float r = FingerRadius[f] * thickness;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 a = _world[first + j];
                    Vector3 b = _world[first + j + 1];
                    // Pull the tip in so the rounded cap ends at the fingertip.
                    if (j == 2) b = Vector3.MoveTowards(b, a, r * 0.6f);
                    PlaceCapsule(_segments[s++], a, b, r);
                }
            }
        }

        private void PlaceCapsule(Part p, Vector3 a, Vector3 b, float radius)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            Quaternion rot = len > 1e-5f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            float height = len + 2f * radius;
            // Unity's capsule is 2 units tall with a 0.5 radius.
            Place(p, (a + b) * 0.5f, rot, new Vector3(2f * radius, height * 0.5f, 2f * radius), true);
        }

        private static void Place(Part p, Vector3 pos, Quaternion rot, Vector3 size, bool capsule = false)
        {
            p.body.SetPositionAndRotation(pos, rot);
            p.body.localScale = size;
            if (p.outline == null) return;

            p.outline.SetPositionAndRotation(pos, rot);
            // Grow by a constant world width so the outline does not get
            // thicker on long parts. Capsule height is half-scaled.
            float w = 2f * OutlineWidth;
            p.outline.localScale = new Vector3(size.x + w, size.y + (capsule ? OutlineWidth : w), size.z + w);
        }

        private Part MakePart(PrimitiveType type, Material mat, Material outlineMat)
        {
            var p = new Part { body = MakeRenderer(type, mat, false) };
            if (outlineMat != null) p.outline = MakeRenderer(type, outlineMat, true);
            return p;
        }

        private Transform MakeRenderer(PrimitiveType type, Material mat, bool outline)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = outline ? "Outline" : type.ToString();
            Object.Destroy(go.GetComponent<Collider>());
            // The shop switches the world camera to the UI layer only; keeping
            // the hands on that layer makes them survive.
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;
            go.transform.SetParent(_root.transform, false);

            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = !outline;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            return go.transform;
        }

        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------

        /// <summary>
        /// Clones one of the game's URP/Lit materials so the shader variant is
        /// guaranteed to exist in the build, then recolours it.
        /// </summary>
        public static Material MakeLitMaterial(string name, Color color, float smoothness)
        {
            Material template = null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null) continue;
                if (m.shader.name != "Universal Render Pipeline/Lit") continue;
                if (m.IsKeywordEnabled("_EMISSION") || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) continue;
                template = m;
                if (m.name.StartsWith("Gnome")) break; // plain painted plastic look
            }

            Material mat;
            if (template != null) mat = new Material(template);
            else
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return null;
                mat = new Material(sh);
            }

            mat.name = name;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
            mat.DisableKeyword("_EMISSION");
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        /// <summary>Black front-culled material for inverted-hull outlines.</summary>
        public static Material MakeOutlineMaterial()
        {
            // The game's own "Black_Unlit_Backfaced" is a transparent variant;
            // an opaque copy sorts reliably against the glove itself.
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return null;
            var mat = new Material(sh) { name = "BerryVR_Outline" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.black);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Front);
            return mat;
        }
    }
}
