using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KinoRotunda.Editor
{
    public static class KinoCloudLayerSetup
    {
        const string TexturePath = "Assets/KinoRotunda/Textures/CloudLayer.png";
        const string MaterialPath = "Assets/KinoRotunda/Materials/CloudLayer.mat";

        [MenuItem("Tools/KINO Rotunda/Clouds/Configure CloudLayer")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure the cloud layer outside Play mode.");
            var scene = SceneManager.GetActiveScene();
            var cloud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .SingleOrDefault(t => t.name == "CloudLayer");
            if (!cloud || !cloud.TryGetComponent<MeshRenderer>(out var renderer))
                throw new InvalidOperationException("The active scene must contain the CloudLayer quad.");

            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if (!importer) throw new FileNotFoundException("Cloud texture is missing.", TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 1024,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50
            });
            importer.SaveAndReimport();

            var shader = Shader.Find("KinoRotunda/Scrolling Cloud Layer");
            if (!shader) throw new InvalidOperationException("Cloud shader did not import.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "CloudLayer" };
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Opacity", .35f);
                material.SetVector("_ScrollSpeed", new Vector4(.003f, .0007f, 0, 0));
                material.SetFloat("_EdgeFade", .06f);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
                material.SetTexture("_BaseMap", texture);
                // Preserve the source cloud proportions on the user's existing quad.
                var scale = cloud.lossyScale;
                material.SetTextureScale("_BaseMap", new Vector2(1,
                    texture.width / (float)texture.height * Mathf.Abs(scale.y / scale.x)));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            Undo.RecordObject(renderer, "Configure transparent scrolling cloud layer");
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (cloud.TryGetComponent<Collider>(out var collider))
            {
                Undo.RecordObject(collider, "Disable decorative cloud collider");
                collider.enabled = false;
            }
            var errors = ShaderUtil.GetShaderMessages(shader)
                .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(m => m.message)));
            EditorUtility.SetDirty(renderer);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = cloud.gameObject;
            Debug.Log("[CloudLayer] Saved: alpha transparency, slow UV scroll, no shadows or collider.");
        }

        [MenuItem("Tools/KINO Rotunda/Clouds/Capture overhead preview")]
        public static void CapturePreview()
        {
            var go = new GameObject("Cloud preview camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 1.6f, 0);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            camera.fieldOfView = 75;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 120;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data =
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            var previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            var rt = RenderTexture.GetTemporary(1024, 1024, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var output = RenderTexture.GetTemporary(1024, 1024, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(1024, 1024, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(rt, output);
                RenderTexture.active = output;
                texture.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                texture.Apply();
                Directory.CreateDirectory("Artifacts/KinoRotunda/CloudLayer");
                File.WriteAllBytes("Artifacts/KinoRotunda/CloudLayer/Overhead.png", texture.EncodeToPNG());
                var shader = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath).shader;
                var errors = ShaderUtil.GetShaderMessages(shader)
                    .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
                if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(m => m.message)));
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.ReleaseTemporary(rt);
                RenderTexture.ReleaseTemporary(output);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
