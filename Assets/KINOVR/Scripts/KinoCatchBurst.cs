using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KinoVR
{
    // Catch effect for every ball: streaking sparks, a shockwave ring and a core flash in the
    // ball's colour. Sixteen pooled bursts share one mesh and one draw call, so a catch costs
    // two array writes: no Instantiate, no Destroy and no garbage during gameplay.
    [DisallowMultipleComponent]
    public sealed class KinoCatchBurst : MonoBehaviour
    {
        public static KinoCatchBurst Instance { get; private set; }

        [Header("Assigned by Tools > KINO VR > Experience setup")]
        public Material material;
        [Range(8, 128)] public int sparksPerBurst = 64;

        [Header("Colours per ball (alpha = burst size)")]
        public Color normalBall = new Color(1f, .78f, .22f, 1f);
        public Color glowBall = new Color(1f, .92f, .45f, 1.35f);
        public Color mysteryBall = new Color(.72f, .45f, 1f, 1.35f);
        public Color secondChanceBall = new Color(.3f, 1f, .5f, 1.5f);
        public Color bonusBall = new Color(1f, .3f, .22f, 1.6f);
        public Color boostBall = new Color(1f, .62f, .15f, 1.3f);

        public int EmittedCount { get; private set; }
        public bool IsRendering => burstRenderer && burstRenderer.enabled;

        const int Slots = 16;
        const float LongestLife = 1.1f;
        static readonly int PositionsId = Shader.PropertyToID("_BurstPos");
        static readonly int ColoursId = Shader.PropertyToID("_BurstColor");
        static readonly int NowId = Shader.PropertyToID("_BurstNow");
        readonly Vector4[] positions = new Vector4[Slots];
        readonly Vector4[] colours = new Vector4[Slots];
        GameObject root;
        Mesh mesh;
        MeshRenderer burstRenderer;
        MaterialPropertyBlock block;
        int next, builtSparks = -1;
        float activeUntil = -1;

        void OnEnable()
        {
            Instance = this;
            for (int i = 0; i < Slots; i++) positions[i] = new Vector4(0, -100, 0, -1000);
            EnsureBuilt();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (burstRenderer) burstRenderer.enabled = false;
        }

        public void Emit(Vector3 position, Catchable.BallType type)
        {
            EnsureBuilt();
            int slot = next++ % Slots;
            positions[slot] = new Vector4(position.x, position.y, position.z, Time.time);
            var c = ColourFor(type);
            var shaded = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(c.r, c.g, c.b).linear : c;
            colours[slot] = new Vector4(shaded.r, shaded.g, shaded.b, c.a);
            activeUntil = Time.time + LongestLife * 1.6f;
            EmittedCount++;
            Apply();
        }

        Color ColourFor(Catchable.BallType type)
        {
            switch (type)
            {
                case Catchable.BallType.MoreWins: return glowBall;
                case Catchable.BallType.Mystery: return mysteryBall;
                case Catchable.BallType.SecondChance: return secondChanceBall;
                case Catchable.BallType.KinoBonus: return bonusBall;
                case Catchable.BallType.KinoBoost: return boostBall;
                default: return normalBall;
            }
        }

        void LateUpdate()
        {
            if (builtSparks != sparksPerBurst) EnsureBuilt();
            Apply();
        }

        void Apply()
        {
            if (!burstRenderer) return;
            bool active = Time.time < activeUntil && material;
            burstRenderer.enabled = active && isActiveAndEnabled;
            if (!active) return;
            block.SetVectorArray(PositionsId, positions);
            block.SetVectorArray(ColoursId, colours);
            block.SetFloat(NowId, Time.time);
            burstRenderer.SetPropertyBlock(block);
        }

        void EnsureBuilt()
        {
            if (!root)
            {
                root = new GameObject("KINO catch bursts (runtime)", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
                if (gameObject.scene.IsValid() && gameObject.scene.isLoaded) SceneManager.MoveGameObjectToScene(root, gameObject.scene);
                burstRenderer = root.GetComponent<MeshRenderer>();
                burstRenderer.shadowCastingMode = ShadowCastingMode.Off;
                burstRenderer.receiveShadows = false;
                burstRenderer.lightProbeUsage = LightProbeUsage.Off;
                burstRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                burstRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                burstRenderer.allowOcclusionWhenDynamic = false;
                burstRenderer.enabled = false;
            }
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            burstRenderer.sharedMaterial = material;
            if (block == null) block = new MaterialPropertyBlock();
            if (mesh && builtSparks == sparksPerBurst) return;
            if (!mesh) mesh = new Mesh { name = "KINO catch bursts", hideFlags = HideFlags.DontSave };
            Build(mesh);
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            builtSparks = sparksPerBurst;
        }

        void Build(Mesh target)
        {
            int perBurst = sparksPerBurst + 2;
            int quads = Slots * perBurst;
            var vertices = new Vector3[quads * 4];
            var shape = new Vector4[vertices.Length];
            var data = new Vector4[vertices.Length];
            var indices = new int[quads * 6];
            var random = new System.Random(23);
            float R() => (float)random.NextDouble();
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) };
            int q = 0;
            for (int slot = 0; slot < Slots; slot++)
                for (int e = 0; e < perBurst; e++, q++)
                {
                    Vector4 s, d;
                    if (e < sparksPerBurst)
                    {
                        // Directions spread over a sphere, biased upward so sparks arc and fall.
                        var dir = new Vector3(R() * 2 - 1, R() * 1.6f - .4f, R() * 2 - 1).normalized;
                        s = new Vector4(0, dir.x, dir.y, dir.z);
                        // Two thirds fast streaks, one third slow glitter that lingers around the catch.
                        d = e % 3 == 2
                            ? new Vector4(.35f + R() * .7f, .7f + R() * .5f, .004f + R() * .003f, 1.8f + R() * 1.2f)
                            : new Vector4(1.1f + R() * 2.4f, .45f + R() * .5f, .006f + R() * .006f, 1.4f + R() * 1.2f);
                    }
                    else if (e == sparksPerBurst) { s = new Vector4(1, 0, 0, 0); d = new Vector4(0, .38f, .32f, .9f); }
                    else { s = new Vector4(2, 0, 0, 0); d = new Vector4(0, .22f, .09f, 1.6f); }
                    for (int k = 0; k < 4; k++)
                    {
                        int index = q * 4 + k;
                        vertices[index] = new Vector3(corners[k].x, corners[k].y, slot);
                        shape[index] = s; data[index] = d;
                    }
                    int v = q * 4, t = q * 6;
                    indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                    indices[t + 3] = v + 1; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
                }
            target.Clear();
            target.vertices = vertices;
            target.SetUVs(0, shape);
            target.SetUVs(1, data);
            target.SetTriangles(indices, 0, false);
            // Positions come from the shader; never cull.
            target.bounds = new Bounds(Vector3.zero, new Vector3(200, 200, 200));
            target.UploadMeshData(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Application.isPlaying) { if (root) Destroy(root); if (mesh) Destroy(mesh); }
            else { if (root) DestroyImmediate(root); if (mesh) DestroyImmediate(mesh); }
        }
    }
}
