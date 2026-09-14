using System;
using System.IO;
using System.Linq;
using KinoVR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static partial class KinoGameplaySetup
    {
        const string VisualOutput = Output + "/VisualRefresh";
        static readonly Vector3 OvalSize = new Vector3(1.26f, .75f, .75f);

        static void ConfigureOvalBall(GameObject ball)
        {
            const string meshPath = "Assets/KINOVR/Meshes/KinoOvalBall.asset";
            if (!AssetDatabase.IsValidFolder("Assets/KINOVR/Meshes"))
                AssetDatabase.CreateFolder("Assets/KINOVR", "Meshes");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh || mesh.vertexCount < 2000)
            {
                var generated = MakeOvalMesh();
                if (mesh)
                {
                    EditorUtility.CopySerialized(generated, mesh);
                    Object.DestroyImmediate(generated);
                    EditorUtility.SetDirty(mesh);
                }
                else
                {
                    mesh = generated;
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }
            }
            ball.GetComponent<MeshFilter>().sharedMesh = mesh;
            // Bake the shape into the vertices so the number never inherits a squash.
            ball.transform.localScale = Vector3.one * .3f;
            var sphere = ball.GetComponent<SphereCollider>();
            if (sphere) Object.DestroyImmediate(sphere);
            var collider = ball.GetComponent<CapsuleCollider>();
            if (!collider) collider = ball.AddComponent<CapsuleCollider>();
            collider.direction = 0;
            collider.radius = OvalSize.y * .5f;
            collider.height = OvalSize.x;
            collider.center = Vector3.zero;
            collider.isTrigger = false;

            const string materialPath = "Assets/KINOVR/Materials/NumberedBallGold.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var shader = Shader.Find("KINO/Ball Lacquer");
            if (!shader) throw new InvalidOperationException("Missing KINO ball lacquer shader.");
            if (!material)
            {
                material = new Material(shader) { name = "NumberedBallGold" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_EnvironmentAmount", .16f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            ball.GetComponent<MeshRenderer>().sharedMaterial = material;

            var visual = ball.GetComponent<KinoBallNumber>();
            visual.surfaceRadii = OvalSize * .5f;
            visual.face.localPosition = Vector3.back * visual.surfaceRadii.z;
            visual.face.localScale = Vector3.one;
            var canvas = visual.face.GetComponentInChildren<Canvas>();
            canvas.name = "Number on gold surface";
            canvas.transform.localPosition = Vector3.back * .005f;
            canvas.transform.localScale = Vector3.one * (.72f / 128);
            var badge = canvas.transform.Find("White badge");
            if (badge) Object.DestroyImmediate(badge.gameObject);
            visual.numberLabel.color = Color.black;
            visual.numberLabel.fontSize = 96;
            visual.numberLabel.transform.localScale = Vector3.one;
        }

        static Mesh MakeOvalMesh()
        {
            const int columns = 64, rows = 32;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[columns * rows * 6];
            for (int row = 0; row <= rows; row++)
            for (int column = 0; column <= columns; column++)
            {
                float latitude = Mathf.PI * row / rows;
                float longitude = Mathf.PI * 2 * column / columns;
                var unit = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                int index = row * (columns + 1) + column;
                vertices[index] = Vector3.Scale(unit, OvalSize) * .5f;
                normals[index] = new Vector3(unit.x / OvalSize.x, unit.y / OvalSize.y, unit.z / OvalSize.z).normalized;
                uv[index] = new Vector2((float)column / columns, 1 - (float)row / rows);
                if (row == rows || column == columns) continue;
                int next = index + columns + 1;
                int t = (row * columns + column) * 6;
                triangles[t] = index; triangles[t + 1] = index + 1; triangles[t + 2] = next;
                triangles[t + 3] = index + 1; triangles[t + 4] = next + 1; triangles[t + 5] = next;
            }
            var mesh = new Mesh { name = "KINO oval 1.68 to 1", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void ConfigureBoardMotion(KinoNumberBoard board)
        {
            SetBoardPanel(board.transform, "Live number field", "BoardBlue", 59, 121, 955, 443);
            SetBoardPanel(board.transform, "Catch header", "BoardCatchHeader", 48, 16, 310, 76);
            SetBoardPanel(board.transform, "Timer header", "BoardTimerHeader", 774, 16, 235, 76);
            var artwork = board.transform.Find("Existing KINO artwork").GetComponent<RawImage>();
            var material = GraphicMaterial("BoardAnimatedArtwork", 3);
            material.SetFloat("_Mode", 3);
            material.SetVector("_BoardRect", new Vector4(0, 0, 1, 1));
            SetMotionDefaults(material);
            artwork.material = material;
        }
        static void SetBoardPanel(Transform board, string objectName, string materialName, float x, float y, float width, float height)
        {
            var material = GraphicMaterial(materialName, 0);
            material.SetVector("_BoardRect", new Vector4(x / 1065, 1 - (y + height) / 602, width / 1065, height / 602));
            SetMotionDefaults(material);
            board.Find(objectName).GetComponent<Image>().material = material;
        }
        static void SetMotionDefaults(Material material)
        {
            material.SetFloat("_MotionSpeed", .7f);
            material.SetFloat("_GlowStrength", 1);
            material.SetFloat("_PreviewTime", -1);
            EditorUtility.SetDirty(material);
        }

        [MenuItem("Tools/KINO VR/4 - Oval balls and animated display")]
        public static void UpgradeVisuals()
        {
            RequireScene();
            Directory.CreateDirectory(VisualOutput);
            var scene = SceneManager.GetActiveScene();
            if (!File.Exists(VisualOutput + "/Before.unity"))
                EditorSceneManager.SaveScene(scene, VisualOutput + "/Before.unity", true);
            const string prefabPath = "Assets/KINOVR/Prefabs/NumberedBall.prefab";
            if (!File.Exists(VisualOutput + "/NumberedBall.before.prefab"))
                File.Copy(prefabPath, VisualOutput + "/NumberedBall.before.prefab");
            var ball = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                ConfigureOvalBall(ball);
                PrefabUtility.SaveAsPrefabAsset(ball, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(ball); }

            LoadMaterials();
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            ConfigureBoardMotion(round.board);
            // Apply only the four material references; retain other scene overrides.
            var panelNames = new[] { "Existing KINO artwork", "Live number field", "Catch header", "Timer header" };
            var panelMaterials = panelNames.Select(name => round.board.transform.Find(name).GetComponent<Graphic>().material).ToArray();
            for (int i = 0; i < panelNames.Length; i++)
            {
                var graphic = round.board.transform.Find(panelNames[i]).GetComponent<Graphic>();
                // Applying a property reloads the instance, including later unapplied
                // material changes. Restore each intended reference before applying it.
                graphic.material = panelMaterials[i];
                if (PrefabUtility.IsPartOfPrefabInstance(graphic))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
                    var property = new SerializedObject(graphic).FindProperty("m_Material");
                    PrefabUtility.ApplyPropertyOverride(property, "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab", InteractionMode.AutomatedAction);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            ValidateVisuals();
            Status("OVAL_BALLS_AND_NEON_READY");
        }

        static void ValidateVisuals()
        {
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            var ball = round.launcher.ballPrefab;
            var shape = ball.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            Assert(Mathf.Abs(shape.x / shape.y - 1.68f) < .01f, "Ball must match the board's oval ratio.");
            Assert(ball.transform.localScale == Vector3.one * .3f, "Ball root must stay uniformly scaled.");
            var collider = ball.GetComponent<CapsuleCollider>();
            Assert(collider && collider.direction == 0 && !ball.GetComponent<SphereCollider>(), "Missing horizontal catch collider.");
            Assert(Mathf.Abs(collider.height - shape.x) < .001f && Mathf.Abs(collider.radius * 2 - shape.y) < .001f, "Catch collider extents differ from ball.");
            var visual = ball.GetComponent<KinoBallNumber>();
            Assert(visual.numberLabel.color == Color.black, "Ball number must be black.");
            Assert(!ball.GetComponentsInChildren<Image>(true).Any(), "Ball still contains a badge.");
            Assert(ball.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "KINO/Ball Lacquer", "Missing lacquer material.");
            var prefabBoard = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab").GetComponentInChildren<KinoNumberBoard>(true);
            foreach (string panelName in new[] { "Existing KINO artwork", "Live number field", "Catch header", "Timer header" })
                Assert(prefabBoard.transform.Find(panelName).GetComponent<Graphic>().material == round.board.transform.Find(panelName).GetComponent<Graphic>().material, "Board motion material was not saved: " + panelName);
            foreach (var shaderName in new[] { "KINO/Ball Lacquer", "KINO/Board Graphic" })
                Assert(!ShaderUtil.ShaderHasError(Shader.Find(shaderName)), "Shader errors: " + shaderName);
            File.WriteAllText(VisualOutput + "/validation.txt", "PASS: 1.68:1 oval geometry; uniform root and label; capsule matches extents; black numbers; no white badge; shared yellow lacquer palette; shaders compile.\n");
        }

        [MenuItem("Tools/KINO VR/5 - Capture oval balls and neon motion")]
        public static void PreviewVisuals() => CaptureVisuals(false);

        static void CaptureVisuals(bool captureMotion)
        {
            RequireScene();
            Directory.CreateDirectory(VisualOutput);
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            // Preview a disposable copy so TMP colour caches and marker states never
            // become scene/prefab overrides when capturing caught-number examples.
            var originalBoard = round.board;
            bool boardWasActive = originalBoard.gameObject.activeSelf;
            var board = Object.Instantiate(originalBoard.gameObject, originalBoard.transform.parent).GetComponent<KinoNumberBoard>();
            board.name = "KINO board preview copy";
            board.gameObject.SetActive(true);
            originalBoard.gameObject.SetActive(false);
            var materials = board.GetComponentsInChildren<Graphic>(true).Select(g => g.material)
                .Where(m => m && m.HasProperty("_PreviewTime")).Distinct().ToArray();
            var screen = Screen().bounds;
            var go = new GameObject("Visual refresh QA camera");
            var camera = go.AddComponent<Camera>();
            camera.nearClipPlane = .02f;
            camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.useOcclusionCulling = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            GameObject ball = null;
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = screen.size.y * .505f;
                camera.transform.position = screen.center + Vector3.back * 2;
                camera.transform.rotation = Quaternion.identity;
                board.ResetBoard();
                board.SetProgress(0, 75, 75, false);
                foreach (var material in materials) material.SetFloat("_PreviewTime", 0);
                Capture(camera, VisualOutput + "/Board-empty.png", 1600, 904);
                foreach (int number in new[] { 1, 7, 16, 26, 37, 49, 59, 80 }) board.MarkCaught(number);
                board.SetProgress(8, 43, 75, false);
                for (int frame = 0; frame < 4; frame++)
                {
                    foreach (var material in materials) material.SetFloat("_PreviewTime", frame * 4);
                    Capture(camera, VisualOutput + "/Board-motion-" + frame + ".png", 1600, 904);
                }
                if (captureMotion)
                {
                    Directory.CreateDirectory(VisualOutput + "/MotionFrames");
                    for (int frame = 0; frame < 150; frame++)
                    {
                        foreach (var material in materials) material.SetFloat("_PreviewTime", frame / 15f);
                        Capture(camera, VisualOutput + "/MotionFrames/" + frame.ToString("000") + ".png", 960, 544);
                    }
                }
                camera.orthographic = false;
                camera.fieldOfView = 65;
                camera.transform.SetPositionAndRotation(round.playerView.desktopCamera.transform.position, round.playerView.desktopCamera.transform.rotation);
                ball = Object.Instantiate(round.launcher.ballPrefab);
                ball.GetComponent<Rigidbody>().isKinematic = true;
                ball.transform.position = new Vector3(.35f, 1.42f, 1.1f);
                ball.GetComponent<KinoBallNumber>().SetNumber(80, camera.transform);
                Capture(camera, VisualOutput + "/Player-view.png", 1600, 1000);

                camera.orthographic = true;
                camera.orthographicSize = .17f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.012f, .026f, .06f);
                ball.transform.position = new Vector3(0, 30, 0);
                foreach (int number in new[] { 1, 8, 80 })
                {
                    camera.transform.SetPositionAndRotation(ball.transform.position + Vector3.back, Quaternion.identity);
                    ball.GetComponent<KinoBallNumber>().SetNumber(number, camera.transform);
                    Capture(camera, VisualOutput + "/Ball-" + number + ".png", 1000, 650);
                }
                camera.transform.position = ball.transform.position + new Vector3(.55f, .18f, -1);
                camera.transform.LookAt(ball.transform);
                ball.GetComponent<KinoBallNumber>().SetNumber(80, camera.transform);
                Capture(camera, VisualOutput + "/Ball-oblique.png", 1000, 650);
                ValidateVisuals();
            }
            finally
            {
                foreach (var material in materials) material.SetFloat("_PreviewTime", -1);
                Object.DestroyImmediate(board.gameObject);
                originalBoard.gameObject.SetActive(boardWasActive);
                if (ball) Object.DestroyImmediate(ball);
                Object.DestroyImmediate(go);
            }
            Status("VISUAL_PREVIEWS_READY");
        }
    }
}
