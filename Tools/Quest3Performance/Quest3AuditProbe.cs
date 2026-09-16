// Temporary editor probe: copy into an Editor folder, run Quest3AuditProbe.Run
// in Unity batch mode, then remove the temporary copy. Does not save any assets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class Quest3AuditProbe
{
    [Serializable] public class Report
    {
        public string unityVersion, activeBuildTarget, scene, utc;
        public List<RendererInfo> renderers = new List<RendererInfo>();
        public List<CameraInfo> cameras = new List<CameraInfo>();
        public List<MaterialInfo> materials = new List<MaterialInfo>();
        public List<TextureInfo> textures = new List<TextureInfo>();
        public List<LightInfo> lights = new List<LightInfo>();
        public List<ProbeInfo> probes = new List<ProbeInfo>();
        public List<CanvasInfo> canvases = new List<CanvasInfo>();
        public List<string> volumes = new List<string>(), lodGroups = new List<string>();
        public int colliders, rigidbodies, enabledColliders, activeRigidbodies, lightmaps;
        public List<string> lightmapTextures = new List<string>();
    }
    [Serializable] public class RendererInfo
    {
        public string path, type, mesh, meshAsset, staticFlags, shadows, lightProbes, reflectionProbes;
        public bool active, enabled, readable;
        public int vertices, submeshes, lightmap;
        public long triangles;
        public string[] materials;
        public Vector3 bounds;
    }
    [Serializable] public class CameraInfo
    {
        public string path, targetEye;
        public bool active, enabled, hdr, msaa, postProcessing, dynamicResolution;
        public float farClip;
    }
    [Serializable] public class MaterialInfo
    {
        public string path, name, shader;
        public bool instancing;
        public int renderQueue, passes;
        public string[] keywords;
    }
    [Serializable] public class TextureInfo
    {
        public string path, type, format, androidFormat, defaultCompression;
        public int width, height, mips, defaultMaxSize, androidMaxSize;
        public bool readable, androidOverride, streaming;
        public long editorRuntimeBytes;
    }
    [Serializable] public class LightInfo { public string path, type, bakeType, shadows; public bool active, enabled; }
    [Serializable] public class ProbeInfo { public string path, mode, refresh, texture; public bool active, enabled; public int resolution; }
    [Serializable] public class CanvasInfo { public string path, mode; public bool active, enabled; public int graphics; }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    public static void Run()
    {
        const string scenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();
        var report = new Report { unityVersion = Application.unityVersion, activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(), scene = scenePath, utc = DateTime.UtcNow.ToString("o") };
        var materials = new HashSet<Material>();
        foreach (var root in roots)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var filter = r.GetComponent<MeshFilter>();
                var mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
                long triangles = 0;
                if (mesh) for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) triangles += mesh.GetIndexCount(i) / 3;
                report.renderers.Add(new RendererInfo { path = PathOf(r.transform), type = r.GetType().Name, active = r.gameObject.activeInHierarchy, enabled = r.enabled,
                    mesh = mesh ? mesh.name : "", meshAsset = mesh ? AssetDatabase.GetAssetPath(mesh) : "", vertices = mesh ? mesh.vertexCount : 0, triangles = triangles, submeshes = mesh ? mesh.subMeshCount : 0,
                    readable = mesh && mesh.isReadable, staticFlags = GameObjectUtility.GetStaticEditorFlags(r.gameObject).ToString(), shadows = r.shadowCastingMode.ToString(), lightProbes = r.lightProbeUsage.ToString(), reflectionProbes = r.reflectionProbeUsage.ToString(),
                    lightmap = r.lightmapIndex, materials = r.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "").ToArray(), bounds = r.bounds.size });
                foreach (var m in r.sharedMaterials) if (m) materials.Add(m);
            }
            foreach (var c in root.GetComponentsInChildren<Camera>(true))
            {
                var data = c.GetComponent<UniversalAdditionalCameraData>();
                report.cameras.Add(new CameraInfo { path = PathOf(c.transform), active = c.gameObject.activeInHierarchy, enabled = c.enabled, hdr = c.allowHDR, msaa = c.allowMSAA, dynamicResolution = c.allowDynamicResolution, postProcessing = data && data.renderPostProcessing, farClip = c.farClipPlane, targetEye = c.stereoTargetEye.ToString() });
            }
            foreach (var l in root.GetComponentsInChildren<Light>(true)) report.lights.Add(new LightInfo { path = PathOf(l.transform), type = l.type.ToString(), bakeType = l.lightmapBakeType.ToString(), shadows = l.shadows.ToString(), active = l.gameObject.activeInHierarchy, enabled = l.enabled });
            foreach (var p in root.GetComponentsInChildren<ReflectionProbe>(true)) report.probes.Add(new ProbeInfo { path = PathOf(p.transform), mode = p.mode.ToString(), refresh = p.refreshMode.ToString(), texture = p.texture ? AssetDatabase.GetAssetPath(p.texture) : "", resolution = p.resolution, active = p.gameObject.activeInHierarchy, enabled = p.enabled });
            foreach (var v in root.GetComponentsInChildren<Volume>(true)) report.volumes.Add(PathOf(v.transform) + " | enabled=" + v.enabled + " | " + AssetDatabase.GetAssetPath(v.sharedProfile));
            foreach (var l in root.GetComponentsInChildren<LODGroup>(true)) report.lodGroups.Add(PathOf(l.transform));
            foreach (var c in root.GetComponentsInChildren<Canvas>(true)) report.canvases.Add(new CanvasInfo { path = PathOf(c.transform), mode = c.renderMode.ToString(), active = c.gameObject.activeInHierarchy, enabled = c.enabled, graphics = c.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length });
            var colliders = root.GetComponentsInChildren<Collider>(true);
            report.colliders += colliders.Length;
            report.enabledColliders += colliders.Count(c => c.enabled && c.gameObject.activeInHierarchy);
            var bodies = root.GetComponentsInChildren<Rigidbody>(true);
            report.rigidbodies += bodies.Length;
            report.activeRigidbodies += bodies.Count(b => b.gameObject.activeInHierarchy);
        }
        foreach (var m in materials) report.materials.Add(new MaterialInfo { path = AssetDatabase.GetAssetPath(m), name = m.name, shader = m.shader ? m.shader.name : "", instancing = m.enableInstancing, renderQueue = m.renderQueue, passes = m.passCount, keywords = m.shaderKeywords });
        foreach (var path in AssetDatabase.GetDependencies(scenePath, true))
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (!texture) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var android = importer?.GetPlatformTextureSettings("Android");
            report.textures.Add(new TextureInfo { path = path, type = texture.GetType().Name, width = texture.width, height = texture.height,
                format = texture.graphicsFormat.ToString(), mips = texture.mipmapCount, readable = texture.isReadable, editorRuntimeBytes = Profiler.GetRuntimeMemorySizeLong(texture),
                androidOverride = android != null && android.overridden, androidFormat = android != null ? android.format.ToString() : "", androidMaxSize = android != null ? android.maxTextureSize : 0,
                defaultMaxSize = importer ? importer.maxTextureSize : 0, defaultCompression = importer ? importer.textureCompression.ToString() : "", streaming = importer && importer.streamingMipmaps });
        }
        report.lightmaps = LightmapSettings.lightmaps.Length;
        foreach (var lm in LightmapSettings.lightmaps) foreach (var texture in new[] { lm.lightmapColor, lm.lightmapDir, lm.shadowMask }) if (texture) report.lightmapTextures.Add(AssetDatabase.GetAssetPath(texture));
        Directory.CreateDirectory("Artifacts/Quest3Performance");
        File.WriteAllText("Artifacts/Quest3Performance/scene-inventory.json", JsonUtility.ToJson(report, true));
        Debug.Log("QUEST3_AUDIT_COMPLETE");
    }
}
