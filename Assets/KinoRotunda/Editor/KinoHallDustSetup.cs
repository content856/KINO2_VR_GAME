using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoRotunda.Editor
{
    [InitializeOnLoad]
    public static class KinoHallDustSetup
    {
        const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        const string MaterialPath = "Assets/KinoRotunda/Materials/HallDust.mat";
        const string MeshPath = "Assets/KinoRotunda/Models/HallDustEmission.asset";
        const string ObjectName = "Hall Dust - soft motes near the arches";
        const string Output = "Artifacts/KinoRotunda/HallDust";
        const string BatchKey = "KinoHallDust.PlayTest";
        static double playStarted;
        static Dictionary<uint, Vector3> initialPositions;
        static readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[128];

        static KinoHallDustSetup()
        {
            EditorApplication.playModeStateChanged += PlayState;
            EditorApplication.update += PlayTick;
        }

        public static void BatchRun()
        {
            try
            {
                Directory.CreateDirectory(Output);
                if (File.Exists(Output + "/error.txt")) File.Delete(Output + "/error.txt");
                if (File.Exists(Output + "/play-test.txt")) File.Delete(Output + "/play-test.txt");
                EditorSceneManager.OpenScene(ScenePath);
                Setup();
                Validate();
                CapturePreview();
                // Exercise the saved configuration, including native prewarming.
                EditorSceneManager.OpenScene(ScenePath);
                SessionState.SetBool(BatchKey, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { Fail(e); }
        }

        [MenuItem("Tools/KINO Rotunda/Dust/1 - Set up subtle hall dust")]
        public static void Setup()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Output + "/Before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var shader = Shader.Find("KinoRotunda/Dust Mote");
            if (!shader) throw new InvalidOperationException("Dust shader did not import.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "HallDust" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetColor("_BaseColor", new Color(1, .88f, .65f, .7f));
            material.SetVector("_NearFade", new Vector4(.9f, 2.2f, 0, 0));
            EditorUtility.SetDirty(material);

            var system = FindDust();
            if (!system)
            {
                var go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Add hall dust");
                system = go.AddComponent<ParticleSystem>();
            }
            Undo.RecordObject(system, "Configure hall dust");
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            system.transform.localScale = Vector3.one;
            system.useAutoRandomSeed = false;
            system.randomSeed = 5831;
            var main = system.main;
            main.duration = 38;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(30, 38);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.008f, .02f);
            main.startSize = new ParticleSystem.MinMaxCurve(.022f, .055f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, 1, 1, .16f), new Color(1, 1, 1, .34f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = 128;
            main.cullingMode = ParticleSystemCullingMode.Automatic;
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 3.2f;
            emission.rateOverDistance = 0;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            // One renderer covers the ring. This invisible point mesh emits throughout
            // a 3D volume at the arches, leaving the player and display approach clear.
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.mesh = BuildEmissionMesh();
            shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
            shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Random;
            shape.useMeshColors = false;
            shape.useMeshMaterialIndex = false;
            shape.normalOffset = 0;
            shape.randomDirectionAmount = 1;
            var color = system.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .18f), new GradientAlphaKey(.85f, .75f), new GradientAlphaKey(0, 1) });
            color.color = fade;
            var noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.octaveCount = 1;
            noise.strength = .025f;
            noise.frequency = .35f;
            noise.scrollSpeed = .035f;
            noise.damping = false;
            noise.positionAmount = .3f;
            noise.rotationAmount = 0;
            noise.sizeAmount = 0;
            var collision = system.collision; collision.enabled = false;
            var lights = system.lights; lights.enabled = false;
            var trails = system.trails; trails.enabled = false;
            var subEmitters = system.subEmitters; subEmitters.enabled = false;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            Undo.RecordObject(renderer, "Configure dust renderer");
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.minParticleSize = 0;
            renderer.maxParticleSize = .015f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            EditorUtility.SetDirty(system);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = system.gameObject;
        }

        static Mesh BuildEmissionMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (!mesh)
            {
                mesh = new Mesh { name = "Hall dust emission points (no renderer)" };
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }
            var random = new System.Random(731);
            var vertices = new Vector3[768];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                float angle = Mathf.Lerp(24, 336, (float)random.NextDouble()) * Mathf.Deg2Rad;
                float radius = Mathf.Sqrt(Mathf.Lerp(9.5f * 9.5f, 11.25f * 11.25f, (float)random.NextDouble()));
                float height = Mathf.Lerp(1.5f, 5.25f, (float)random.NextDouble());
                vertices[i] = new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
                normals[i] = Vector3.up;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            // Triangle indices keep this a regular mesh asset; the system explicitly
            // samples vertices, so none of the connecting surfaces are used or drawn.
            mesh.triangles = Enumerable.Range(0, vertices.Length).ToArray();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        [MenuItem("Tools/KINO Rotunda/Dust/2 - Validate hall dust")]
        public static void Validate()
        {
            RequireScene();
            var system = FindDust();
            if (!system) throw new InvalidOperationException("Hall dust is missing.");
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            var errors = ShaderUtil.GetShaderMessages(renderer.sharedMaterial.shader)
                .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(e => e.message)));
            if (system.GetComponentsInChildren<Renderer>().Length != 1 || renderer.sharedMaterial.shader.passCount != 1)
                throw new InvalidOperationException("Dust must use one renderer and one shader pass.");
            if (system.main.maxParticles > 128 || system.collision.enabled || system.lights.enabled || system.trails.enabled || system.subEmitters.enabled)
                throw new InvalidOperationException("Dust exceeds its effect budget.");
            foreach (Vector3 p in system.shape.mesh.vertices)
                if (new Vector2(p.x, p.z).magnitude < 9.49f || new Vector2(p.x, p.z).magnitude > 11.26f || p.y < 1.49f || p.y > 5.26f)
                    throw new InvalidOperationException("Dust source escaped the arcade volume.");
            int minCount = 128, maxCount = 0;
            float minRadius = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                system.Simulate(38 + i * 12, true, true, true);
                int count = system.GetParticles(particles);
                minCount = Mathf.Min(minCount, count); maxCount = Mathf.Max(maxCount, count);
                for (int j = 0; j < count; j++)
                {
                    Vector3 p = particles[j].position;
                    minRadius = Mathf.Min(minRadius, new Vector2(p.x, p.z).magnitude);
                    if (!float.IsFinite(p.x) || p.y < 0 || p.y > 6.6f || new Vector2(p.x, p.z).magnitude > 12.6f)
                        throw new InvalidOperationException($"Dust drift escaped the hall at sample {i}: position {p}, velocity {particles[j].velocity}, remaining life {particles[j].remainingLifetime:F2}.");
                }
            }
            if (minCount < 75 || maxCount > 128 || minRadius < 8)
                throw new InvalidOperationException("Dust density or player clearance is incorrect.");
            system.Simulate(38, true, true, true);
            var before = Positions(system);
            system.Simulate(2, true, false, true);
            int moving = CountMoving(system, before);
            if (moving < 60) throw new InvalidOperationException("Dust particles are not drifting.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.txt", $"PASS\nNative particle systems: 1\nRenderer/material/shader passes: 1/1/1\nLive particles over 122 seconds: {minCount}-{maxCount}; cap 128\nMaximum billboard triangles: 256\nParticles moving after two seconds: {moving}\nEmission radius: 9.5-11.25 m; height 1.5-5.25 m\nMinimum simulated radius: {minRadius:F2} m\nDisplay approach excluded from emission; camera proximity fade 0.9-2.2 m\nLifetime alpha fade: transparent birth and death\nNo texture samples, depth copies, shadows, lights, collisions or trails\nQuest GPU time: not measured on device\n");
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        [MenuItem("Tools/KINO Rotunda/Dust/3 - Capture hall dust preview")]
        public static void CapturePreview()
        {
            RequireScene();
            var system = FindDust();
            if (!system) throw new InvalidOperationException("Hall dust is missing.");
            Directory.CreateDirectory(Output + "/Frames");
            var go = new GameObject("Dust preview camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            bool wasEnabled = renderer.enabled;
            camera.enabled = false;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 100;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 65;
            camera.transform.position = new Vector3(0, 1.6f, 0);
            camera.transform.LookAt(new Vector3(0, 2.3f, 11.8f));
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            try
            {
                system.Simulate(38, true, true, true);
                renderer.enabled = false;
                Capture(camera, Output + "/Player-before.png", 1600, 1000);
                renderer.enabled = true;
                Capture(camera, Output + "/Player-after.png", 1600, 1000);
                camera.transform.LookAt(new Vector3(-10, 3.3f, 6));
                camera.fieldOfView = 42;
                Capture(camera, Output + "/Arches-player-view.png", 1600, 1000);
                camera.transform.position = new Vector3(-6, 1.65f, 1);
                camera.transform.LookAt(new Vector3(-10.5f, 3.1f, 5));
                camera.fieldOfView = 52;
                renderer.enabled = false;
                Capture(camera, Output + "/Arches-before.png", 1600, 1000);
                renderer.enabled = true;
                Capture(camera, Output + "/Arches-after.png", 1600, 1000);
                for (int i = 0; i < 144; i++)
                {
                    if (i > 0) system.Simulate(1 / 24f, true, false, false);
                    Capture(camera, Output + "/Frames/" + i.ToString("000") + ".png", 1280, 800);
                }
            }
            finally
            {
                renderer.enabled = wasEnabled;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Object.DestroyImmediate(go);
            }
        }

        static void Capture(Camera camera, string path, int width, int height)
        {
            var active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var output = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(rt, output);
                RenderTexture.active = output;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = active;
                GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(rt);
                RenderTexture.ReleaseTemporary(output);
                Object.DestroyImmediate(texture);
            }
        }

        static Dictionary<uint, Vector3> Positions(ParticleSystem system)
        {
            int count = system.GetParticles(particles);
            var positions = new Dictionary<uint, Vector3>();
            for (int i = 0; i < count; i++) positions[particles[i].randomSeed] = particles[i].position;
            return positions;
        }

        static int CountMoving(ParticleSystem system, Dictionary<uint, Vector3> before)
        {
            int moving = 0, count = system.GetParticles(particles);
            for (int i = 0; i < count; i++)
                if (before.TryGetValue(particles[i].randomSeed, out Vector3 p) && (particles[i].position - p).sqrMagnitude > .000004f) moving++;
            return moving;
        }

        static void PlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(BatchKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            { playStarted = EditorApplication.timeSinceStartup; initialPositions = null; }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                bool success = File.Exists(Output + "/play-test.txt") && File.ReadAllText(Output + "/play-test.txt").StartsWith("PASS");
                SessionState.SetBool(BatchKey, false);
                EditorApplication.Exit(success ? 0 : 1);
            }
        }

        static void PlayTick()
        {
            if (!SessionState.GetBool(BatchKey, false) || !EditorApplication.isPlaying) return;
            try
            {
                var system = FindDust();
                if (!system) throw new InvalidOperationException("Dust missing in Play Mode.");
                if (initialPositions == null)
                {
                    if (system.particleCount < 60)
                    {
                        if (EditorApplication.timeSinceStartup - playStarted > 5)
                            throw new InvalidOperationException("Dust did not prewarm on startup.");
                        return;
                    }
                    initialPositions = Positions(system);
                    playStarted = EditorApplication.timeSinceStartup;
                }
                if (EditorApplication.timeSinceStartup - playStarted < 2) return;
                if (!system.isPlaying || CountMoving(system, initialPositions) < 50)
                    throw new InvalidOperationException("Runtime dust is not advancing.");
                File.WriteAllText(Output + "/play-test.txt", "PASS - saved scene reloaded; native prewarm populated the hall immediately; particles drift in Play Mode; cap 128.\n");
                EditorApplication.isPlaying = false;
            }
            catch (Exception e) { Fail(e); }
        }

        static ParticleSystem FindDust()
        {
            var go = GameObject.Find(ObjectName);
            return go ? go.GetComponent<ParticleSystem>() : null;
        }
        static void RequireScene()
        {
            if (SceneManager.GetActiveScene().path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Open KinoRotunda in Edit Mode after the current bake finishes.");
        }
        static void Fail(Exception e)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/error.txt", e.ToString());
            Debug.LogException(e);
            SessionState.SetBool(BatchKey, false);
            EditorApplication.Exit(1);
        }
    }
}
