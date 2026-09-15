using UnityEngine;
using UnityEngine.Rendering;

namespace KinoRotunda
{
    /// <summary>One occasional flock, travelling from the rear to the display wall.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class KinoExteriorBirds : MonoBehaviour
    {
        public const int Columns = 8;
        public const int Rows = 2;
        public const int FrameCount = Columns * Rows;

        [Range(1, 32)] public int birdCount = 16;
        [Min(22)] public float flightRadius = 50;
        [Min(2)] public float flightHeight = 11;
        [Min(.1f)] public float birdWidth = .8f;
        [Min(.1f)] public float flightSpeed = 6;
        [Range(1, 60)] public float animationFPS = 32;
        [Tooltip("Register the supplied sheet to the body/tail attachment so changing frames does not move the whole bird.")]
        public bool stabilizeAnimation = true;
        [Tooltip("Random wait before the first flock starts behind the player.")]
        public Vector2 firstDelay = new Vector2(2, 5);
        [Tooltip("Random bird-free interval after one flock finishes, before the next starts.")]
        public Vector2 quietInterval = new Vector2(16, 30);
        [Tooltip("Length of the loose flock, including the two stragglers forming its narrow rear tip.")]
        [Min(1)] public float flockSpread = 7;
        [Tooltip("Use the centre-eye anchor for a consistent billboard in both eyes.")]
        public Transform viewer;
        public Material birdMaterial;

        Mesh mesh;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        Vector3[] vertices;
        Vector2[] uvs;
        Bird[] birds;
        System.Random random;
        bool rebuild = true;
        float startedAt;
        float lastEvaluation = -1;
        float passStart, passEnd, passRadius, passHeight;
        int sequenceSeed = 173;
        float cellAspect = .75f, insetU, insetV;
        const float StartAngle = 155;
        const float EndAngle = 3;

        // Body registration for Birds.png (2172 x 724, cells 271.5 x 362).
        // Measured from the tail/body attachment, NOT the silhouette bounds: wing
        // extension changes those bounds. Keep the original full cells and scale.
        // Coordinates are pixels from each cell's top left, including row two's reset.
        static readonly float[] BodyPivotY =
        {
            244, 252, 262, 265, 269, 267, 266, 266,
            156, 153, 147, 136, 138, 135, 135, 138
        };

        struct Bird
        {
            public float trail, height, depth, width, phase, flapRate;
        }

        public bool IsFlockActive { get; private set; }
        public bool FlyingFromLeft { get; private set; }
        public int PassNumber { get; private set; }
        public float PassDuration => passEnd - passStart;
        public float PassStartTime => passStart;
        public float PassEndTime => passEnd;
        public float NextFlockTime { get; private set; }

        void OnEnable()
        {
            startedAt = Time.time;
            sequenceSeed = Application.isPlaying ? System.Environment.TickCount : 173;
            rebuild = true;
            if (Application.isPlaying) Evaluate(0);
            else PreviewPass(.8f, true);
        }

        void OnValidate()
        {
            birdCount = Mathf.Clamp(birdCount, 1, 32);
            flightRadius = Mathf.Max(22, flightRadius);
            flightHeight = Mathf.Max(2, flightHeight);
            birdWidth = Mathf.Max(.1f, birdWidth);
            flightSpeed = Mathf.Max(.1f, flightSpeed);
            animationFPS = Mathf.Clamp(animationFPS, 1, 60);
            firstDelay.x = Mathf.Max(0, firstDelay.x);
            firstDelay.y = Mathf.Max(firstDelay.x, firstDelay.y);
            quietInterval.x = Mathf.Max(1, quietInterval.x);
            quietInterval.y = Mathf.Max(quietInterval.x, quietInterval.y);
            flockSpread = Mathf.Max(1, flockSpread);
            rebuild = true;
        }

        void LateUpdate()
        {
            if (Application.isPlaying) Evaluate(Time.time - startedAt);
            else if (rebuild) PreviewPass(.8f, true);
        }

        void BuildMesh()
        {
            if (!meshFilter) meshFilter = GetComponent<MeshFilter>();
            if (!meshRenderer) meshRenderer = GetComponent<MeshRenderer>();
            ReleaseMesh();
            mesh = new Mesh { name = "Exterior birds (runtime)", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            vertices = new Vector3[birdCount * 4];
            uvs = new Vector2[vertices.Length];
            birds = new Bird[birdCount];
            var triangles = new int[birdCount * 6];
            for (int i = 0; i < birdCount; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 2; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = birdMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            // This combined mesh moves around the room; static occlusion is inappropriate.
            meshRenderer.allowOcclusionWhenDynamic = false;
            var texture = birdMaterial ? birdMaterial.mainTexture : null;
            cellAspect = texture ? (float)texture.width * Rows / (texture.height * Columns) : .75f;
            insetU = texture ? .5f / texture.width : 0;
            insetV = texture ? .5f / texture.height : 0;
            rebuild = false;
            ResetSequence();
        }

        void ResetSequence()
        {
            // Local random state does not affect gameplay or the lottery's random sequence.
            random = new System.Random(sequenceSeed);
            NextFlockTime = Range(firstDelay.x, firstDelay.y);
            passStart = passEnd = -1;
            lastEvaluation = -1;
            PassNumber = 0;
            IsFlockActive = false;
        }

        float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        void BeginFlock(float time, bool fromLeft)
        {
            FlyingFromLeft = fromLeft;
            passRadius = flightRadius + Range(0, 4);
            passHeight = flightHeight + Range(-.6f, .6f);
            passStart = time;
            passEnd = time + (StartAngle - EndAngle) * Mathf.Deg2Rad * passRadius / (flightSpeed * Range(.93f, 1.07f));
            PassNumber++;
            int stragglers = Mathf.Min(2, birdCount - 1);
            int mainCount = birdCount - stragglers;
            for (int i = 0; i < birdCount; i++)
            {
                float trail = 0, height = 0, depth = 0;
                if (i >= mainCount)
                {
                    // The point is BEHIND the main flock: two unevenly spaced followers.
                    bool last = i == birdCount - 1;
                    trail = -flockSpread * (last ? .5f : Range(.29f, .34f));
                    height = last ? Range(-.18f, .08f) : Range(.15f, .38f);
                    depth = last ? Range(-.2f, .2f) : Range(.25f, .6f);
                }
                else
                {
                    // A broad, asymmetric front tapering towards the rear. Sample once
                    // per pass; no rows, paired V arms, or frame-driven positional bob.
                    float bestClearance = -1;
                    for (int attempt = 0; attempt < 96; attempt++)
                    {
                        float along = Range(0, 1);
                        float candidateTrail = flockSpread * Mathf.Lerp(-.13f, .5f, along);
                        float candidateHeight = Range(-1, 1) * Mathf.Lerp(.65f, 1.65f, along);
                        float clearance = float.MaxValue;
                        for (int j = 0; j < i; j++)
                        {
                            float dx = candidateTrail - birds[j].trail, dy = candidateHeight - birds[j].height;
                            clearance = Mathf.Min(clearance, dx * dx + dy * dy);
                        }
                        if (clearance > bestClearance)
                        {
                            bestClearance = clearance;
                            trail = candidateTrail;
                            height = candidateHeight;
                            depth = Range(-1, 1) * Mathf.Lerp(.55f, 1.6f, along);
                        }
                        if (clearance >= .5f) break;
                    }
                }
                birds[i] = new Bird
                {
                    trail = trail, height = height, depth = depth,
                    width = birdWidth * Range(.85f, 1.12f), phase = Range(0, FrameCount), flapRate = Range(.9f, 1.1f)
                };
            }
        }

        /// <summary>Playback includes random quiet periods and exactly one active pass.</summary>
        public void Evaluate(float seconds)
        {
            if (rebuild || !mesh || vertices == null || vertices.Length != birdCount * 4) BuildMesh();
            seconds = Mathf.Max(0, seconds);
            if (seconds < lastEvaluation) ResetSequence();
            // Also supports skipping ahead in editor validation without overlapping flocks.
            while (seconds >= NextFlockTime)
            {
                BeginFlock(NextFlockTime, random.Next(2) == 0);
                NextFlockTime = passEnd + Range(quietInterval.x, quietInterval.y);
            }
            lastEvaluation = seconds;
            IsFlockActive = seconds >= passStart && seconds < passEnd;
            meshRenderer.enabled = IsFlockActive;
            if (IsFlockActive) RenderFlock(seconds - passStart);
        }

        /// <summary>Inspect either path at a fixed phase without waiting for a random spawn.</summary>
        public void PreviewPass(float progress, bool fromLeft, int animationFrame = -1)
        {
            if (Application.isPlaying) return;
            if (rebuild || !mesh || vertices == null || vertices.Length != birdCount * 4) BuildMesh();
            ResetSequence();
            BeginFlock(0, fromLeft);
            IsFlockActive = progress >= 0 && progress < 1;
            meshRenderer.enabled = IsFlockActive;
            RenderFlock(Mathf.Clamp01(progress) * PassDuration, animationFrame);
            lastEvaluation = float.PositiveInfinity; // The next normal Evaluate restarts its schedule.
        }

        void RenderFlock(float age, int animationFrame = -1)
        {
            Vector3 eye = viewer ? transform.InverseTransformPoint(viewer.position) : new Vector3(0, 1.6f, 0);
            float direction = FlyingFromLeft ? 1 : -1;
            float angle = -direction * Mathf.Lerp(StartAngle, EndAngle, age / PassDuration) * Mathf.Deg2Rad;
            for (int i = 0; i < birdCount; i++)
            {
                Bird bird = birds[i];
                // Each follower reaches the same bend later along the shared arc.
                float birdAngle = angle + direction * bird.trail / passRadius;
                Vector3 radial = new Vector3(Mathf.Sin(birdAngle), 0, Mathf.Cos(birdAngle));
                Vector3 forward = new Vector3(Mathf.Cos(birdAngle), 0, -Mathf.Sin(birdAngle)) * direction;
                Vector3 centre = radial * (passRadius + bird.depth) + Vector3.up * (passHeight + bird.height);
                Vector3 towardBird = centre - eye;
                towardBird.y = 0;
                if (towardBird.sqrMagnitude < .001f) towardBird = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, towardBird).normalized;
                bool mirror = Vector3.Dot(forward, right) < 0;
                int frame = animationFrame >= 0 ? animationFrame % FrameCount :
                    Mathf.FloorToInt(age * animationFPS * bird.flapRate + bird.phase) % FrameCount;
                float width = bird.width;
                if (stabilizeAnimation)
                {
                    // Account for the half-texel inset when mapping the source landmark
                    // onto the quad, and flip its horizontal offset with the artwork.
                    float pivotX = (147.5f / 271.5f - insetU * Columns) / (1 - 2 * insetU * Columns);
                    float pivotY = (1 - BodyPivotY[frame] / 362f - insetV * Rows) / (1 - 2 * insetV * Rows);
                    centre += right * ((.5f - pivotX) * width * (mirror ? -1 : 1));
                    centre += Vector3.up * ((.5f - pivotY) * width / cellAspect);
                }
                Vector3 halfRight = right * (width * .5f);
                Vector3 halfUp = Vector3.up * (width / cellAspect * .5f);
                int v = i * 4;
                vertices[v] = centre - halfRight - halfUp;
                vertices[v + 1] = centre + halfRight - halfUp;
                vertices[v + 2] = centre - halfRight + halfUp;
                vertices[v + 3] = centre + halfRight + halfUp;

                int column = frame % Columns, row = frame / Columns;
                float left = (float)column / Columns + insetU;
                float rightU = (float)(column + 1) / Columns - insetU;
                // PNG reading order: top row left-to-right, then bottom row left-to-right.
                float bottom = 1 - (float)(row + 1) / Rows + insetV;
                float top = 1 - (float)row / Rows - insetV;
                if (mirror) { float swap = left; left = rightU; rightU = swap; }
                uvs[v] = new Vector2(left, bottom);
                uvs[v + 1] = new Vector2(rightU, bottom);
                uvs[v + 2] = new Vector2(left, top);
                uvs[v + 3] = new Vector2(rightU, top);
            }
            // Reuse the same buffers; no per-bird GameObjects, materials, or frame allocations.
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(.02f); // Keep edge vertices inside despite float rounding at 40+ metres.
            mesh.bounds = bounds;
        }

        void OnDisable()
        {
            if (meshRenderer) meshRenderer.enabled = false;
            IsFlockActive = false;
            ReleaseMesh();
        }

        void ReleaseMesh()
        {
            if (!mesh) return;
            if (meshFilter && meshFilter.sharedMesh == mesh) meshFilter.sharedMesh = null;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }
    }
}
