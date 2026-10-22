using UnityEngine;
using UnityEngine.Rendering;

namespace BerryVR
{
    /// <summary>
    /// Head-locked comfort vignette: a black ring that narrows the periphery
    /// while the player moves or turns with the sticks. Physical head motion
    /// never triggers it, only artificial locomotion.
    /// </summary>
    public sealed class ComfortVignette
    {
        private const int Segments = 48;
        // Radii are expressed as tangents of the view angle, so the ring keeps
        // the same apparent size at any distance from the eye.
        private const float OpenTan = 1.25f;    // inner edge when barely active
        private const float ClosedTan = 0.42f;  // inner edge at full strength
        private const float FeatherTan = 0.38f;
        private const float OuterTan = 4f;

        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Vector3[] _verts = new Vector3[Segments * 3];
        private readonly MeshRenderer _renderer;
        private float _amount;
        private float _distance = 0.1f;

        public ComfortVignette(Transform head)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return;

            _go = new GameObject("BerryVR_ComfortVignette");
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) _go.layer = uiLayer;
            _go.transform.SetParent(head, false);

            _mesh = new Mesh { name = "BerryVR_Vignette" };
            var colors = new Color32[_verts.Length];
            var tris = new int[Segments * 12];
            for (int i = 0; i < Segments; i++)
            {
                colors[i * 3] = new Color32(0, 0, 0, 0);       // inner, transparent
                colors[i * 3 + 1] = new Color32(0, 0, 0, 255); // end of feather
                colors[i * 3 + 2] = new Color32(0, 0, 0, 255); // outer

                int n = (i + 1) % Segments;
                int t = i * 12;
                for (int ring = 0; ring < 2; ring++)
                {
                    int a = i * 3 + ring, b = n * 3 + ring;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = b; tris[t++] = a + 1; tris[t++] = b + 1;
                }
            }
            Rebuild(OpenTan);
            _mesh.colors32 = colors;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);

            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            var mat = new Material(sh) { name = "BerryVR_Vignette", renderQueue = 4000 };
            _renderer.sharedMaterial = mat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        /// <summary>
        /// Feeds this frame's artificial motion (0..1). The ring fades in
        /// quickly and out slowly so it never pops.
        /// </summary>
        public void Tick(float motion, float strength, float nearClip, float dt)
        {
            if (_go == null) return;

            float target = Mathf.Clamp01(motion) * Mathf.Clamp01(strength);
            float rate = target > _amount ? 8f : 3f;
            _amount = Mathf.MoveTowards(_amount, target, rate * dt);

            bool show = _amount > 0.01f;
            if (_renderer.enabled != show) _renderer.enabled = show;
            if (!show) return;

            // The ring is centred on the head, not on each eye: keep it far
            // enough that the ~3 cm eye offset barely shifts it, and past the
            // near plane.
            float dist = Mathf.Max(0.35f, nearClip * 1.25f + 0.01f);
            if (!Mathf.Approximately(dist, _distance)) _distance = dist;
            Rebuild(Mathf.Lerp(OpenTan, ClosedTan, _amount));
        }

        public void Hide()
        {
            _amount = 0f;
            if (_renderer != null) _renderer.enabled = false;
        }

        private void Rebuild(float innerTan)
        {
            float d = _distance;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                _verts[i * 3] = dir * (innerTan * d) + Vector3.forward * d;
                _verts[i * 3 + 1] = dir * ((innerTan + FeatherTan) * d) + Vector3.forward * d;
                _verts[i * 3 + 2] = dir * (OuterTan * d) + Vector3.forward * d;
            }
            _mesh.vertices = _verts;
        }
    }
}
