using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KinoVR
{
    // The blue pre-game environment: ribbons of glowing lines and drifting particles
    // that surround the seated player, over a navy gradient on the enclosure shell.
    // Meshes are built once; all animation runs in the vertex shaders, so the CPU
    // cost per frame is three property-block updates. The animation is a seamless
    // 120-second loop.
    [DisallowMultipleComponent]
    public sealed class KinoWaveEnvironment : MonoBehaviour
    {
        public const float LoopSeconds = 120;

        [Header("Assigned by Tools > KINO VR > Experience setup")]
        public Material lineMaterial;
        public Material particleMaterial;
        public Material skyMaterial;
        [Tooltip("Supplies the dome mesh for the navy gradient.")]
        public KinoBlackEnclosure enclosure;

        [Header("Look")]
        [Range(0, 3)] public float intensity = 1;
        [Tooltip("Line count multiplier. Lower it if the headset profiler shows fill-rate pressure.")]
        [Range(.25f, 1)] public float density = 1;
        [Range(0, 3)] public float speed = 1;
        [Tooltip("Wave amplitude straight ahead, where the reading panels sit, relative to the sides.")]
        [Range(0, 1)] public float calmFront = .3f;
        [Tooltip("Ribbon centre relative to eye height (metres).")]
        public float heightOffset = -.3f;
        [Tooltip("Sky colour at the horizon in front of the player (intro and outro background).")]
        public Color skyHorizon = new Color(.04f, .15f, .44f);
        [Tooltip("Sky colour overhead and underfoot.")]
        public Color skyZenith = new Color(.01f, .035f, .12f);
        public Color deepBlue = new Color(.05f, .3f, 1f);
        public Color midBlue = new Color(.08f, .45f, 1f);
        public Color cyan = new Color(.18f, .7f, 1f);
        [Tooltip("Ambient dust drifting up through the space.")]
        [Range(0, 1500)] public int particleCount = 700;
        [Tooltip("Sparks that stream along the wave ribbons.")]
        [Range(0, 800)] public int riderCount = 320;


        [Header("Transitions (seconds, unscaled)")]
        [Min(.05f)] public float fadeSeconds = .6f;
        [Min(.05f)] public float dimSeconds = .8f;
        [Tooltip("Lines draw outward from the front to the back when the environment appears.")]
        [Min(.1f)] public float revealSeconds = 1.8f;

        public float Visibility => visibility;
        public float Dim => dim;
        public float Reveal => reveal;
        public float Collapse => collapse;
        public float Flash => flash;
        public float SkyOpacity => skyOpacity;
        public bool IsShowing => visibility > .001f;
        public bool LinesRendering => lineRenderer && lineRenderer.enabled;
        public double LoopTime => time;
        /// <summary>Hides the renderers immediately without changing state (used by pixel probes).</summary>
        public bool ProbeHidden { get => probeHidden; set { probeHidden = value; ApplyRendering(); } }

        struct Bundle
        {
            public float centre, amplitude, cycles, speedCycles, phase, twist, spread, radius;
            public int lines;
            public int colour;
        }
        // Speeds and azimuth frequencies are whole numbers so the ring and the loop close seamlessly.
        static readonly Bundle[] Bundles =
        {
            new Bundle { centre = 0f,    amplitude = 1.55f, cycles = 3, speedCycles = 7,  phase = 0f,   twist = 1.6f, spread = .85f, radius = 5.0f, lines = 56, colour = 0 },
            new Bundle { centre = .1f,   amplitude = 1.15f, cycles = 4, speedCycles = -6, phase = 1.7f, twist = 2.2f, spread = .62f, radius = 4.4f, lines = 42, colour = 1 },
            new Bundle { centre = -.12f, amplitude = .72f,  cycles = 5, speedCycles = 9,  phase = 3.1f, twist = 1.4f, spread = .42f, radius = 5.6f, lines = 30, colour = 2 },
        };
        const int Segments = 200;

        static readonly int TimeId = Shader.PropertyToID("_WaveTime");
        static readonly int VisibilityId = Shader.PropertyToID("_Visibility");
        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int DimId = Shader.PropertyToID("_Dim");
        static readonly int CollapseId = Shader.PropertyToID("_Collapse");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int OriginId = Shader.PropertyToID("_Origin");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int CalmId = Shader.PropertyToID("_CalmFront");
        static readonly int SkyOpacityId = Shader.PropertyToID("_SkyOpacity");
        static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
        static readonly int ZenithId = Shader.PropertyToID("_ZenithColor");

        float visibility, targetVisibility, dim, targetDim, reveal = 1, collapse, flash, skyOpacity = 1;
        double time;
        bool probeHidden;
        Vector4 origin = new Vector4(0, 1.2f, 0, 0);
        GameObject root;
        Mesh lineMesh, particleMesh;
        MeshRenderer lineRenderer, particleRenderer, skyRenderer;
        MaterialPropertyBlock block;
        float builtDensity = -1;
        int builtParticles = -1, builtRiders = -1;
        Color builtDeep, builtMid, builtCyan;

        void OnEnable() => EnsureBuilt();

        /// <summary>Places the ring around the eye; the calm zone faces the given heading.</summary>
        public void Anchor(Vector3 eye, Vector3 forward)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z);
            origin = new Vector4(eye.x, eye.y + heightOffset, eye.z, yaw);
            EnsureBuilt();
            ApplyProperties();
        }

        /// <summary>Sets the targets for this frame. Visibility and dim ease unless immediate.
        /// skyOpacity fades the navy sky on its own, so the room can show through while the lines remain.</summary>
        public void Drive(float targetVisibility, float targetDim, float collapse = 0, float flash = 0, bool immediate = false, float skyOpacity = 1)
        {
            this.skyOpacity = Mathf.Clamp01(skyOpacity);
            this.targetVisibility = Mathf.Clamp01(targetVisibility);
            this.targetDim = Mathf.Clamp01(targetDim);
            this.collapse = Mathf.Clamp01(collapse);
            this.flash = Mathf.Clamp01(flash);
            if (immediate)
            {
                if (visibility <= 0 && this.targetVisibility > 0) reveal = 0;
                visibility = this.targetVisibility;
                dim = this.targetDim;
            }
            // Apply now so an immediate hide never leaves a frame of waves behind.
            ApplyProperties();
        }

        public void HideImmediately() => Drive(0, 0, 0, 0, true);

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            time = (time + dt * speed) % LoopSeconds;
            // A new appearance draws the lines out from the front.
            if (visibility <= 0 && targetVisibility > 0) reveal = 0;
            visibility = Mathf.MoveTowards(visibility, targetVisibility, dt / fadeSeconds);
            dim = Mathf.MoveTowards(dim, targetDim, dt / dimSeconds);
            if (visibility > 0) reveal = Mathf.MoveTowards(reveal, 1, dt / revealSeconds);
            if (builtDensity != density || builtParticles != particleCount || builtRiders != riderCount ||
                builtDeep != deepBlue || builtMid != midBlue || builtCyan != cyan) EnsureBuilt();
            ApplyProperties();
        }

        public void EnsureBuilt()
        {
            if (!root)
            {
                root = new GameObject("KINO wave environment (runtime)") { hideFlags = HideFlags.DontSave };
                if (gameObject.scene.IsValid() && gameObject.scene.isLoaded) SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            }
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            if (!lineMesh || builtDensity != density || builtDeep != deepBlue || builtMid != midBlue || builtCyan != cyan)
            {
                if (!lineMesh) lineMesh = new Mesh { name = "KINO wave lines", hideFlags = HideFlags.DontSave };
                BuildLines(lineMesh);
                builtDensity = density; builtDeep = deepBlue; builtMid = midBlue; builtCyan = cyan;
            }
            if (!particleMesh || builtParticles != particleCount || builtRiders != riderCount)
            {
                if (!particleMesh) particleMesh = new Mesh { name = "KINO wave particles", hideFlags = HideFlags.DontSave };
                BuildParticles(particleMesh);
                builtParticles = particleCount; builtRiders = riderCount;
            }
            if (enclosure) enclosure.EnsureBuilt();
            // Sorting: enclosure background 50, sky 55, lines 60, particles 61, hands 75, reading canvas 100.
            if (!skyRenderer) skyRenderer = NewRenderer("Navy sky", 55, enclosure ? enclosure.ShellMesh : null);
            else if (enclosure && skyRenderer.GetComponent<MeshFilter>().sharedMesh != enclosure.ShellMesh)
                skyRenderer.GetComponent<MeshFilter>().sharedMesh = enclosure.ShellMesh;
            if (!lineRenderer) lineRenderer = NewRenderer("Wave lines", 60, lineMesh);
            if (!particleRenderer) particleRenderer = NewRenderer("Wave particles", 61, particleMesh);
            skyRenderer.sharedMaterial = skyMaterial;
            lineRenderer.sharedMaterial = lineMaterial;
            particleRenderer.sharedMaterial = particleMaterial;
            if (block == null) block = new MaterialPropertyBlock();
            ApplyProperties();
        }

        MeshRenderer NewRenderer(string label, int order, Mesh mesh)
        {
            var child = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(root.transform, false);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = false;
            return renderer;
        }

        void ApplyProperties()
        {
            if (block == null || !lineRenderer) return;
            block.Clear();
            block.SetFloat(TimeId, (float)time);
            block.SetFloat(VisibilityId, visibility);
            block.SetFloat(RevealId, reveal);
            block.SetFloat(DimId, dim);
            block.SetFloat(CollapseId, collapse);
            block.SetFloat(FlashId, flash);
            block.SetVector(OriginId, origin);
            block.SetFloat(IntensityId, intensity);
            block.SetFloat(CalmId, calmFront);
            block.SetFloat(SkyOpacityId, skyOpacity);
            // SetVector never converts colour space, so convert explicitly for linear projects.
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            block.SetVector(HorizonId, linear ? skyHorizon.linear : skyHorizon);
            block.SetVector(ZenithId, linear ? skyZenith.linear : skyZenith);
            lineRenderer.SetPropertyBlock(block);
            particleRenderer.SetPropertyBlock(block);
            skyRenderer.SetPropertyBlock(block);
            ApplyRendering();
        }

        void ApplyRendering()
        {
            bool on = isActiveAndEnabled && !probeHidden && visibility > 0;
            if (lineRenderer) lineRenderer.enabled = on && lineMaterial;
            if (particleRenderer) particleRenderer.enabled = on && particleMaterial && particleCount + riderCount > 0;
            bool sky = isActiveAndEnabled && !probeHidden && visibility > 0 && skyOpacity > 0;
            if (skyRenderer) skyRenderer.enabled = sky && skyMaterial && skyRenderer.GetComponent<MeshFilter>().sharedMesh;
        }

        Color Shade(int index)
        {
            var c = index == 0 ? deepBlue : index == 1 ? midBlue : cyan;
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        void BuildLines(Mesh mesh)
        {
            int total = 0;
            foreach (var b in Bundles) total += Mathf.Max(4, Mathf.RoundToInt(b.lines * density));
            int perLine = (Segments + 1) * 2;
            var vertices = new Vector3[total * perLine];
            var shape = new Vector4[vertices.Length];
            var twist = new Vector4[vertices.Length];
            var colours = new Color[vertices.Length];
            var indices = new int[total * Segments * 6];
            var random = new System.Random(7);
            int v = 0, t = 0;
            foreach (var b in Bundles)
            {
                int count = Mathf.Max(4, Mathf.RoundToInt(b.lines * density));
                var colour = Shade(b.colour);
                for (int i = 0; i < count; i++)
                {
                    float s = i / (float)(count - 1) * 2 - 1;
                    float edge = 1 - s * s;
                    float brightness = (.35f + .65f * edge) * .16f / Mathf.Sqrt(density);
                    if (Mathf.Abs(s) < .05f) brightness *= 1.7f;
                    if (random.NextDouble() < .1) brightness *= 2.2f;   // occasional hero line
                    var c = new Color(colour.r, colour.g, colour.b, brightness);
                    var a = new Vector4(b.centre, b.amplitude, b.cycles, b.speedCycles);
                    var w = new Vector4(b.phase, b.twist, b.spread, b.radius);
                    int start = v;
                    for (int k = 0; k <= Segments; k++)
                    {
                        float u = k / (float)Segments;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            vertices[v] = new Vector3(u, s, side);
                            shape[v] = a; twist[v] = w; colours[v] = c;
                            v++;
                        }
                    }
                    for (int k = 0; k < Segments; k++)
                    {
                        int i0 = start + k * 2;
                        indices[t++] = i0; indices[t++] = i0 + 1; indices[t++] = i0 + 2;
                        indices[t++] = i0 + 2; indices[t++] = i0 + 1; indices[t++] = i0 + 3;
                    }
                }
            }
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.SetUVs(0, shape);
            mesh.SetUVs(1, twist);
            mesh.colors = colours;
            mesh.SetTriangles(indices, 0, false);
            // Positions are generated in the shader; keep the renderer from being culled.
            mesh.bounds = new Bounds(new Vector3(0, 2, 0), new Vector3(40, 40, 40));
            mesh.UploadMeshData(false);
        }

        void BuildParticles(Mesh mesh)
        {
            int ambient = Mathf.Max(0, particleCount), riders = Mathf.Max(0, riderCount), count = ambient + riders;
            var vertices = new Vector3[count * 4];
            var place = new Vector4[vertices.Length];
            var motion = new Vector4[vertices.Length];
            var shape = new Vector4[vertices.Length];
            var twist = new Vector4[vertices.Length];
            var ride = new Vector4[vertices.Length];
            var colours = new Color[vertices.Length];
            var indices = new int[count * 6];
            var random = new System.Random(11);
            float R() => (float)random.NextDouble();
            var corners = new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(-1, 1), new Vector3(1, 1) };
            var blue = Shade(0);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var light = linear ? new Color(.45f, .85f, 1f).linear : new Color(.45f, .85f, 1f);
            var spark = linear ? new Color(.7f, .93f, 1f).linear : new Color(.7f, .93f, 1f);
            for (int i = 0; i < count; i++)
            {
                Vector4 p, m, a = Vector4.zero, w = Vector4.zero, r = Vector4.zero;
                Color c;
                if (i < ambient)
                {
                    // Mostly fine dust, some mid motes and a few large soft bokeh dots.
                    float pick = R();
                    float size = pick < .6f ? .05f + R() * .05f : pick < .92f ? .11f + R() * .1f : .3f + R() * .35f;
                    p = new Vector4(R() * Mathf.PI * 2, R(), 2.6f + R() * 3.4f, size);
                    m = new Vector4(Mathf.Round(R() * 2 - 1), 1 + Mathf.Floor(R() * 3), R() * 6.283f, 4.5f + R() * 2.5f);
                    c = R() < .45f ? light : blue;
                    c.a = (.25f + .75f * R()) * (size > .3f ? .3f : 1f);
                }
                else
                {
                    // Riders stay close to a ribbon line; whole laps per loop keep them seamless.
                    var bundle = Bundles[random.Next(Bundles.Length)];
                    float s = (R() * 2 - 1) * .9f;
                    a = new Vector4(bundle.centre, bundle.amplitude, bundle.cycles, bundle.speedCycles);
                    w = new Vector4(bundle.phase, bundle.twist, bundle.spread, bundle.radius);
                    float laps = (R() < .5f ? 1 : 2) * (R() < .5f ? 1 : -1);
                    r = new Vector4(1, s, R(), laps);
                    p = new Vector4(0, 0, 0, .06f + R() * .07f);
                    m = new Vector4(0, 0, R() * 6.283f, 0);
                    c = spark;
                    c.a = .45f + .55f * R();
                }
                for (int k = 0; k < 4; k++)
                {
                    int index = i * 4 + k;
                    vertices[index] = corners[k];
                    place[index] = p; motion[index] = m; shape[index] = a; twist[index] = w; ride[index] = r; colours[index] = c;
                }
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                indices[t + 3] = v + 1; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.SetUVs(0, place);
            mesh.SetUVs(1, motion);
            mesh.SetUVs(2, shape);
            mesh.SetUVs(3, twist);
            mesh.SetUVs(4, ride);
            mesh.colors = colours;
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = new Bounds(new Vector3(0, 2, 0), new Vector3(40, 40, 40));
            mesh.UploadMeshData(false);
        }

        void OnDisable() { if (root) ApplyRendering(); }

        void OnDestroy()
        {
            Release(root); Release(lineMesh); Release(particleMesh);
        }

        static void Release(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
