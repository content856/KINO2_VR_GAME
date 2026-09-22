using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KinoVR
{
    // A closed upper dome at the room origin. The view can turn freely inside it.
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class KinoBlackEnclosure : MonoBehaviour
    {
        [Header("World geometry (metres)")]
        [Min(.1f)] public float radius = 6;
        [Tooltip("Dome apex height above world Y = 0.")]
        [Min(.1f)] public float height = 5;
        [Min(0)] public float floorY = .003f;
        [Header("Shared materials: background queue 3000, fade queue 4100")]
        public Material backgroundMaterial;
        public Material fadeMaterial;

        public float BackgroundAlpha => Mathf.Max(sessionBackground, roundBackground);
        public float FadeAlpha => Mathf.Max(sessionFade, roundFade);
        public MeshRenderer BackgroundRenderer => backgroundRenderer;
        public MeshRenderer FadeRenderer => fadeRenderer;
        public Mesh ShellMesh => shellMesh;

        const int Segments = 64;
        const int Rings = 16;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        float sessionBackground, roundBackground, sessionFade, roundFade;
        float builtRadius = -1, builtHeight = -1, builtFloor = -1;
        GameObject shellRoot;
        Mesh shellMesh;
        MeshRenderer backgroundRenderer, fadeRenderer;
        MaterialPropertyBlock properties;

        void Awake() => EnsureBuilt();
        void OnEnable() => EnsureBuilt();

        public void SetBackground(float alpha)
        {
            sessionBackground = Mathf.Clamp01(alpha);
            EnsureBuilt();
        }

        public void SetRoundBackground(float alpha)
        {
            roundBackground = Mathf.Clamp01(alpha);
            EnsureBuilt();
        }

        public void SetSessionFade(float alpha)
        {
            sessionFade = Mathf.Clamp01(alpha);
            EnsureBuilt();
        }

        public void SetRoundFade(float alpha)
        {
            roundFade = Mathf.Clamp01(alpha);
            EnsureBuilt();
        }

        public void EnsureBuilt()
        {
            if (!shellRoot)
            {
                shellRoot = new GameObject("KINO black enclosure (runtime)") { hideFlags = HideFlags.DontSave };
                // Do not inherit a moved or scaled rig/gameplay parent.
                if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                    SceneManager.MoveGameObjectToScene(shellRoot, gameObject.scene);
            }
            shellRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            shellRoot.transform.localScale = Vector3.one;

            float r = Mathf.Max(.1f, radius);
            float floor = Mathf.Max(0, floorY);
            float h = Mathf.Max(floor + .1f, height);
            if (!shellMesh || builtRadius != r || builtHeight != h || builtFloor != floor)
            {
                if (!shellMesh) shellMesh = new Mesh { name = "KINO closed upper dome", hideFlags = HideFlags.DontSave };
                BuildMesh(shellMesh, r, h, floor);
                builtRadius = r; builtHeight = h; builtFloor = floor;
            }
            if (!backgroundRenderer) backgroundRenderer = NewRenderer("Black background", 50);
            if (!fadeRenderer) fadeRenderer = NewRenderer("Black fade", 32760);
            if (properties == null) properties = new MaterialPropertyBlock();
            Apply(backgroundRenderer, backgroundMaterial, BackgroundAlpha);
            Apply(fadeRenderer, fadeMaterial, FadeAlpha);
        }

        MeshRenderer NewRenderer(string label, int order)
        {
            var child = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(shellRoot.transform, false);
            child.GetComponent<MeshFilter>().sharedMesh = shellMesh;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            return renderer;
        }

        void Apply(MeshRenderer renderer, Material material, float alpha)
        {
            renderer.sharedMaterial = material;
            properties.SetColor(ColorId, new Color(0, 0, 0, alpha));
            renderer.SetPropertyBlock(properties);
            renderer.enabled = isActiveAndEnabled && material && alpha > 0;
        }

        static void BuildMesh(Mesh mesh, float r, float h, float floor)
        {
            float rise = h - floor;
            int apex = Rings * Segments;
            int floorRim = apex + 1;
            int floorCenter = floorRim + Segments;
            var vertices = new Vector3[floorCenter + 1];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[Rings * Segments * 6];
            for (int ring = 0; ring < Rings; ring++)
            {
                float latitude = ring * (Mathf.PI * .5f / Rings);
                float c = Mathf.Cos(latitude), s = Mathf.Sin(latitude);
                for (int side = 0; side < Segments; side++)
                {
                    float longitude = side * (Mathf.PI * 2 / Segments);
                    float x = Mathf.Cos(longitude), z = Mathf.Sin(longitude);
                    int index = ring * Segments + side;
                    vertices[index] = new Vector3(r * c * x, floor + rise * s, r * c * z);
                    normals[index] = new Vector3(c * x / r, s / rise, c * z / r).normalized;
                }
            }
            vertices[apex] = new Vector3(0, h, 0);
            normals[apex] = Vector3.up;
            vertices[floorCenter] = new Vector3(0, floor, 0);
            normals[floorCenter] = Vector3.down;

            int cursor = 0;
            for (int ring = 0; ring < Rings - 1; ring++)
                for (int side = 0; side < Segments; side++)
                {
                    int next = (side + 1) % Segments;
                    int a = ring * Segments + side, b = ring * Segments + next;
                    int c = a + Segments, d = b + Segments;
                    // Outward winding: the shader culls these front faces to show the interior.
                    triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = b;
                    triangles[cursor++] = b; triangles[cursor++] = c; triangles[cursor++] = d;
                }
            for (int side = 0; side < Segments; side++)
            {
                int next = (side + 1) % Segments;
                triangles[cursor++] = (Rings - 1) * Segments + side;
                triangles[cursor++] = apex;
                triangles[cursor++] = (Rings - 1) * Segments + next;
                // Separate rim vertices keep the flat floor normals facing down.
                vertices[floorRim + side] = vertices[side];
                normals[floorRim + side] = Vector3.down;
                triangles[cursor++] = floorCenter;
                triangles[cursor++] = floorRim + side;
                triangles[cursor++] = floorRim + next;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        void OnDisable()
        {
            if (backgroundRenderer) backgroundRenderer.enabled = false;
            if (fadeRenderer) fadeRenderer.enabled = false;
        }

        void OnDestroy()
        {
            Release(shellRoot);
            Release(shellMesh);
        }

        static void Release(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
