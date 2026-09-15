using System;
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
    public static class KinoExteriorBirdsSetup
    {
        const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        const string TexturePath = "Assets/KinoRotunda/Textures/Birds/Birds.png";
        const string MaterialPath = "Assets/KinoRotunda/Materials/ExteriorBirds.mat";
        const string Output = "Artifacts/KinoRotunda/ExteriorBirds";
        const string BatchKey = "KinoExteriorBirds.BatchTest";
        static double testStarted;
        static Vector3[] initialVertices;
        static bool passedPlayTest;
        static Vector2[] sourceBodyPivots;
        static float rawBodyDrift, stabilizedBodyDrift;

        static KinoExteriorBirdsSetup()
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
                SessionState.SetBool(BatchKey, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { Fail(e); }
        }

        [MenuItem("Tools/KINO Rotunda/Birds/1 - Add exterior birds")]
        public static void Setup()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Output + "/Before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if (!importer) throw new FileNotFoundException("Import the supplied Birds.png first.");
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 1024,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50
            });
            importer.SaveAndReimport();
            var shader = Shader.Find("KinoRotunda/Bird Silhouette");
            if (!shader) throw new InvalidOperationException("Bird shader did not import.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "ExteriorBirds" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            EditorUtility.SetDirty(material);
            var birds = FindBirds();
            if (!birds)
            {
                var go = new GameObject("Exterior Birds");
                Undo.RegisterCreatedObjectUndo(go, "Add exterior birds");
                birds = go.AddComponent<KinoExteriorBirds>();
            }
            Undo.RecordObject(birds, "Configure exterior bird sheet");
            birds.birdMaterial = material;
            var player = Object.FindFirstObjectByType<KinoVR.KinoPlayerView>();
            if (player) birds.viewer = player.head;
            birds.PreviewPass(.8f, true);
            // Evaluate may reuse a mesh from OnEnable; establish the saved renderer reference.
            birds.GetComponent<MeshRenderer>().sharedMaterial = material;
            EditorUtility.SetDirty(birds);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = birds.gameObject;
        }

        [MenuItem("Tools/KINO Rotunda/Birds/2 - Validate exterior birds")]
        public static void Validate()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            var birds = FindBirds();
            if (!birds) throw new InvalidOperationException("No exterior birds in scene.");
            var shader = birds.birdMaterial.shader;
            var shaderErrors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            if (shaderErrors.Length > 0) throw new InvalidOperationException(string.Join("\n", shaderErrors.Select(m => m.message)));
            if (birds.GetComponentsInChildren<Renderer>().Length != 1 || shader.passCount != 1)
                throw new InvalidOperationException("Birds must use one renderer and one shader pass.");
            if (birds.GetComponentsInChildren<Collider>().Length != 0 || birds.GetComponentsInChildren<Light>().Length != 0)
                throw new InvalidOperationException("Unexpected bird physics or lighting.");
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                source.LoadImage(File.ReadAllBytes(TexturePath));
                if (source.GetPixel(0, 0).a != 0) throw new InvalidOperationException("Bird source lost transparency.");
                MeasureSourcePivots(source);
                ValidateRegistration(birds);
            }
            finally { Object.DestroyImmediate(source); }

            // Exercise scheduled starts/stops, both sides, and all 16 frames over ten minutes.
            float minRadius = float.MaxValue;
            var framesSeen = new System.Collections.Generic.HashSet<int>();
            var sidesSeen = new System.Collections.Generic.HashSet<bool>();
            int previousPass = 0, inactiveSamples = 0;
            float previousEnd = -1;
            birds.Evaluate(0);
            if (birds.IsFlockActive || birds.GetComponent<MeshRenderer>().enabled)
                throw new InvalidOperationException("First flock must wait for its scheduled start.");
            for (int step = 0; step <= 4800; step++)
            {
                birds.Evaluate(step / 8f + .019f);
                if (!birds.IsFlockActive)
                {
                    inactiveSamples++;
                    if (birds.GetComponent<MeshRenderer>().enabled)
                        throw new InvalidOperationException("Bird renderer must switch off during the quiet interval.");
                    continue;
                }
                sidesSeen.Add(birds.FlyingFromLeft);
                if (birds.PassNumber != previousPass)
                {
                    if (previousEnd >= 0)
                    {
                        float gap = birds.PassStartTime - previousEnd;
                        if (gap < birds.quietInterval.x - .001f || gap > birds.quietInterval.y + .001f)
                            throw new InvalidOperationException("Flocks overlap or the quiet interval is outside its limits.");
                    }
                    previousEnd = birds.PassEndTime;
                    previousPass = birds.PassNumber;
                }
                var mesh = birds.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.vertexCount != birds.birdCount * 4 || mesh.triangles.Length != birds.birdCount * 6)
                    throw new InvalidOperationException("Bird mesh has unexpected geometry.");
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                if (mesh.bounds.size.magnitude > birds.flockSpread + 8)
                    throw new InvalidOperationException("Birds no longer form one compact flock.");
                for (int i = 0; i < vertices.Length; i++)
                {
                    float radius = new Vector2(vertices[i].x, vertices[i].z).magnitude;
                    minRadius = Mathf.Min(minRadius, radius);
                    if (radius < 20 || float.IsNaN(vertices[i].x) || !mesh.bounds.Contains(vertices[i]))
                        throw new InvalidOperationException("Bird entered the rotunda or escaped its culling bounds.");
                    if (uvs[i].x < 0 || uvs[i].x > 1 || uvs[i].y < 0 || uvs[i].y > 1)
                        throw new InvalidOperationException("Invalid bird sheet UVs.");
                }
                for (int i = 0; i < birds.birdCount; i++)
                {
                    Vector2 uv = (uvs[i * 4] + uvs[i * 4 + 3]) * .5f;
                    int frame = Mathf.FloorToInt(uv.x * 8) + Mathf.FloorToInt((1 - uv.y) * 2) * 8;
                    framesSeen.Add(frame);
                }
            }
            if (framesSeen.Count != 16) throw new InvalidOperationException("Not all 16 frames are sampled.");
            if (sidesSeen.Count != 2 || inactiveSamples == 0)
                throw new InvalidOperationException("Random scheduling did not exercise both sides and quiet periods.");
            foreach (bool fromLeft in new[] { true, false })
            {
                birds.PreviewPass(0, fromLeft);
                if (birds.GetComponent<MeshFilter>().sharedMesh.vertices.Any(v => v.z >= 0))
                    throw new InvalidOperationException("Flock must start behind the player.");
                birds.PreviewPass(.55f, fromLeft);
                ValidateRearTip(birds, fromLeft);
                if (birds.GetComponent<MeshFilter>().sharedMesh.vertices.Any(v => fromLeft ? v.x >= 0 : v.x <= 0))
                    throw new InvalidOperationException("Flock crossed onto the opposite side of the rotunda.");
                birds.PreviewPass(.999f, fromLeft);
                if (birds.GetComponent<MeshFilter>().sharedMesh.vertices.Any(v => Mathf.Abs(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg) > 12))
                    throw new InvalidOperationException("Flock does not finish behind the display-wall sector.");
                birds.PreviewPass(1, fromLeft);
                if (birds.GetComponent<MeshRenderer>().enabled)
                    throw new InvalidOperationException("Flock must disappear at the end of its pass.");
            }
            birds.PreviewPass(.8f, true);
            File.WriteAllText(Output + "/validation.txt", $"PASS\nBirds per flock: {birds.birdCount}\nActive flocks: at most one\nRenderers: 1 (disabled during quiet intervals)\nShader passes: 1\nVertices: {birds.birdCount * 4}\nTriangles: {birds.birdCount * 2}\nSheet: 8 columns x 2 rows, all 16 frames exercised, nominal {birds.animationFPS} FPS\nSource alpha: verified\nMinimum vertex radius across 600 seconds: {minRadius:F2} m\nScheduled passes tested: {previousPass}; both left and right paths\nQuiet gaps: {birds.quietInterval.x}-{birds.quietInterval.y} seconds verified\nBoth paths start behind the player and end within the display-wall sector\nBody landmark drift across all frames (both directions): {rawBodyDrift:F4} m raw, {stabilizedBodyDrift:F6} m registered\nFull source cells and fixed scale retained; no extra vertical bob\nTwo staggered followers form the narrow REAR tip; broad irregular main flock ahead; both paths verified\nAndroid: ASTC 6x6, max 1024, mipmaps; no CPU texture copy\nHeadset GPU timing: not measured\n");
        }

        // Independently recover a stable body landmark from source alpha. Ignore the
        // wings above the tail, and measure the lower tail contour in a fixed strip.
        static void MeasureSourcePivots(Texture2D source)
        {
            sourceBodyPivots = new Vector2[KinoExteriorBirds.FrameCount];
            var pixels = source.GetPixels32();
            float cellWidth = source.width / (float)KinoExteriorBirds.Columns;
            int cellHeight = source.height / KinoExteriorBirds.Rows;
            for (int frame = 0; frame < sourceBodyPivots.Length; frame++)
            {
                var contour = new int[41];
                int rowTop = frame / KinoExteriorBirds.Columns * cellHeight;
                float columnLeft = frame % KinoExteriorBirds.Columns * cellWidth;
                for (int sample = 0; sample < contour.Length; sample++)
                {
                    int x = Mathf.RoundToInt(columnLeft + 55 + sample);
                    for (int y = cellHeight - 1; y >= 0; y--)
                        if (pixels[(source.height - 1 - rowTop - y) * source.width + x].a > 128)
                        { contour[sample] = y; break; }
                }
                Array.Sort(contour);
                sourceBodyPivots[frame] = new Vector2((columnLeft + 147.5f) / source.width,
                    1 - (rowTop + contour[contour.Length / 2] - 16f) / source.height);
            }
        }

        static Vector3 BodyPosition(Vector3[] vertices, Vector2[] uv, int bird)
        {
            int v = bird * 4;
            Vector2 middle = (uv[v] + uv[v + 3]) * .5f;
            int frame = Mathf.FloorToInt(middle.x * 8) + Mathf.FloorToInt((1 - middle.y) * 2) * 8;
            Vector2 pivot = sourceBodyPivots[frame];
            return vertices[v] + (vertices[v + 1] - vertices[v]) * ((pivot.x - uv[v].x) / (uv[v + 1].x - uv[v].x))
                + (vertices[v + 2] - vertices[v]) * ((pivot.y - uv[v].y) / (uv[v + 2].y - uv[v].y));
        }

        static void ValidateRegistration(KinoExteriorBirds birds)
        {
            bool original = birds.stabilizeAnimation;
            rawBodyDrift = stabilizedBodyDrift = 0;
            try
            {
                foreach (bool fromLeft in new[] { true, false })
                foreach (bool stabilize in new[] { false, true })
                {
                    birds.stabilizeAnimation = stabilize;
                    var positions = new Vector3[KinoExteriorBirds.FrameCount];
                    float firstWidth = 0;
                    for (int frame = 0; frame < positions.Length; frame++)
                    {
                        birds.PreviewPass(.55f, fromLeft, frame);
                        var mesh = birds.GetComponent<MeshFilter>().sharedMesh;
                        var vertices = mesh.vertices;
                        positions[frame] = BodyPosition(vertices, mesh.uv, 0);
                        float width = Vector3.Distance(vertices[0], vertices[1]);
                        if (frame == 0) firstWidth = width;
                        if (Mathf.Abs(width - firstWidth) > .0001f)
                            throw new InvalidOperationException("Body stabilization changed bird scale between frames.");
                    }
                    float drift = 0;
                    foreach (Vector3 a in positions)
                    foreach (Vector3 b in positions) drift = Mathf.Max(drift, Vector3.Distance(a, b));
                    if (stabilize) stabilizedBodyDrift = Mathf.Max(stabilizedBodyDrift, drift);
                    else rawBodyDrift = Mathf.Max(rawBodyDrift, drift);
                }
                if (rawBodyDrift < birds.birdWidth * .3f || stabilizedBodyDrift > birds.birdWidth * .01f)
                    throw new InvalidOperationException("Source-measured body landmark still jumps across animation frames.");
            }
            finally { birds.stabilizeAnimation = original; }
        }

        static void ValidateRearTip(KinoExteriorBirds birds, bool fromLeft)
        {
            if (birds.birdCount < 4) return;
            var mesh = birds.GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var uv = mesh.uv;
            var centres = Enumerable.Range(0, birds.birdCount).Select(i => BodyPosition(vertices, uv, i)).ToArray();
            Vector3 radial = centres.Aggregate(Vector3.zero, (sum, p) => sum + p);
            radial.y = 0;
            radial.Normalize();
            Vector3 forward = new Vector3(radial.z, 0, -radial.x) * (fromLeft ? 1 : -1);
            float last = Vector3.Dot(centres[centres.Length - 1], forward);
            float penultimate = Vector3.Dot(centres[centres.Length - 2], forward);
            float mainRear = centres.Take(centres.Length - 2).Min(p => Vector3.Dot(p, forward));
            if (last >= penultimate || penultimate >= mainRear)
                throw new InvalidOperationException("The two stragglers must follow BEHIND the main flock on both paths.");
            float tailHeight = Mathf.Abs(centres[centres.Length - 1].y - centres[centres.Length - 2].y);
            float mainHeight = centres.Take(centres.Length - 2).Max(p => p.y) - centres.Take(centres.Length - 2).Min(p => p.y);
            if (birds.birdCount >= 8 && mainHeight <= tailHeight + .3f)
                throw new InvalidOperationException("The rear tip must be narrower than the main flock.");
        }

        [MenuItem("Tools/KINO Rotunda/Birds/3 - Capture bird motion preview")]
        public static void CapturePreview()
        {
            RequireScene();
            var birds = FindBirds();
            if (!birds) throw new InvalidOperationException("No exterior birds in scene.");
            Directory.CreateDirectory(Output + "/FlockFrames");
            var go = new GameObject("Bird preview camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 1.35f, 0);
            camera.transform.LookAt(new Vector3(0, 2.2f, 11.8f));
            camera.fieldOfView = 65;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 100;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            try
            {
                birds.PreviewPass(.8f, true);
                Capture(camera, Output + "/Player-view.png", 1600, 1000);
                birds.PreviewPass(.8f, false);
                Capture(camera, Output + "/Right-flock.png", 1600, 1000);
                birds.PreviewPass(.8f, true);
                float duration = birds.PassDuration;
                camera.transform.LookAt(new Vector3(-24, 7, 36));
                camera.fieldOfView = 45;
                for (int i = 0; i < 192; i++)
                {
                    // Real-time sampling at 24 FPS: the last seven seconds plus a second hidden.
                    birds.PreviewPass((duration - 7 + i / 24f) / duration, true);
                    Capture(camera, Output + "/FlockFrames/" + i.ToString("000") + ".png", 960, 600);
                }
                birds.PreviewPass(.8f, true);
                camera.transform.LookAt(new Vector3(-24, 7, 36));
                camera.fieldOfView = 35;
                Capture(camera, Output + "/Arch-closeup.png", 1280, 800);
                CaptureSheetPreview(birds.birdMaterial);
            }
            finally { birds.PreviewPass(.8f, true); Object.DestroyImmediate(go); }
        }

        static void CaptureSheetPreview(Material material)
        {
            if (sourceBodyPivots == null)
            {
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try { source.LoadImage(File.ReadAllBytes(TexturePath)); MeasureSourcePivots(source); }
                finally { Object.DestroyImmediate(source); }
            }
            var cameraGo = new GameObject("Sheet preview camera") { hideFlags = HideFlags.HideAndDontSave };
            var birdGo = new GameObject("Sheet preview bird") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.72f, .8f, .87f);
                camera.cullingMask = 1 << 31;
                camera.orthographic = true;
                camera.orthographicSize = 1.45f;
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 10;
                var bird = birdGo.AddComponent<KinoExteriorBirds>();
                bird.birdCount = 1;
                bird.birdWidth = 1.8f;
                bird.birdMaterial = material;
                bird.viewer = camera.transform;
                bird.PreviewPass(.55f, true, 0);
                var mesh = bird.GetComponent<MeshFilter>().sharedMesh;
                Vector3 target = BodyPosition(mesh.vertices, mesh.uv, 0);
                camera.transform.position = target - Vector3.forward * 5;
                camera.transform.LookAt(target);
                foreach (bool stabilize in new[] { false, true })
                {
                    bird.stabilizeAnimation = stabilize;
                    string directory = Output + (stabilize ? "/RegisteredFrames" : "/SourceFrames");
                    Directory.CreateDirectory(directory);
                    for (int frame = 0; frame < KinoExteriorBirds.FrameCount; frame++)
                    {
                        bird.PreviewPass(.55f, true, frame);
                        Capture(camera, directory + "/" + frame.ToString("000") + ".png", 640, 400);
                    }
                }
                // Unobstructed views make the rear tip reviewable even when the
                // room's pillars hide most of the flock in the player preview.
                bird.birdCount = 16;
                bird.birdWidth = .8f;
                bird.stabilizeAnimation = true;
                camera.transform.position = new Vector3(0, 1.6f, 0);
                camera.farClipPlane = 100;
                camera.orthographicSize = 3;
                foreach (bool fromLeft in new[] { true, false })
                {
                    bird.PreviewPass(.55f, fromLeft);
                    mesh = bird.GetComponent<MeshFilter>().sharedMesh;
                    var vertices = mesh.vertices;
                    var uv = mesh.uv;
                    target = Vector3.zero;
                    for (int i = 0; i < bird.birdCount; i++) target += BodyPosition(vertices, uv, i);
                    camera.transform.LookAt(target / bird.birdCount);
                    Capture(camera, Output + (fromLeft ? "/Formation-left.png" : "/Formation-right.png"), 1280, 720);
                }
            }
            finally { Object.DestroyImmediate(birdGo); Object.DestroyImmediate(cameraGo); }
        }

        public static void BatchCapture()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                CapturePreview();
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Fail(e); }
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

        static void PlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(BatchKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                testStarted = EditorApplication.timeSinceStartup;
                initialVertices = null;
                passedPlayTest = false;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                bool success = File.Exists(Output + "/play-test.txt") && File.ReadAllText(Output + "/play-test.txt").StartsWith("PASS");
                SessionState.SetBool(BatchKey, false);
                EditorApplication.Exit(success ? 0 : 1);
            }
        }

        static void PlayTick()
        {
            if (!SessionState.GetBool(BatchKey, false) || !EditorApplication.isPlaying || passedPlayTest) return;
            try
            {
                var birds = FindBirds();
                if (!birds) throw new InvalidOperationException("Birds missing in Play mode.");
                if (!birds.IsFlockActive)
                {
                    if (EditorApplication.timeSinceStartup - testStarted > birds.firstDelay.y + 5)
                        throw new InvalidOperationException("No runtime flock spawned after its initial wait.");
                    return;
                }
                var mesh = birds.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh) return;
                if (initialVertices == null)
                {
                    initialVertices = mesh.vertices;
                    testStarted = EditorApplication.timeSinceStartup;
                }
                if (EditorApplication.timeSinceStartup - testStarted < 2) return;
                var current = mesh.vertices;
                if ((current[0] - initialVertices[0]).sqrMagnitude < .01f)
                    throw new InvalidOperationException("Runtime bird positions are not advancing.");
                if (Object.FindObjectsByType<KinoExteriorBirds>(FindObjectsSortMode.None).Length != 1)
                    throw new InvalidOperationException("Duplicate bird controllers.");
                File.WriteAllText(Output + "/play-test.txt", "PASS — saved scene reloaded; initial quiet period; runtime flock spawned and advanced in LateUpdate; one bird controller.\n");
                passedPlayTest = true;
                EditorApplication.isPlaying = false;
            }
            catch (Exception e) { Fail(e); }
        }

        static KinoExteriorBirds FindBirds() => Object.FindFirstObjectByType<KinoExteriorBirds>();
        static void RequireScene()
        {
            if (SceneManager.GetActiveScene().path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open KinoRotunda in Edit mode first.");
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the light bake.");
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
