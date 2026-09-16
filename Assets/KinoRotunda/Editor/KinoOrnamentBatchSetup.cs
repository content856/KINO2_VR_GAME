using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoRotunda.Editor
{
    public static class KinoOrnamentBatchSetup
    {
        const string Folder = "Assets/KinoRotunda/Meshes/OrnamentBatches";
        const string RootName = "Grouped fixed ornaments";

        [MenuItem("Tools/KINO Rotunda/13 - Group fixed ornaments")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Apply in Edit mode after lighting has finished.");
            var root = GameObject.Find("KINO Rotunda • Environment");
            if (!root) throw new InvalidOperationException("Open the Rotunda scene first.");
            Configure(root);
            Validate(root);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        public static void Configure(GameObject root)
        {
            if (!AssetDatabase.IsValidFolder("Assets/KinoRotunda/Meshes")) AssetDatabase.CreateFolder("Assets/KinoRotunda", "Meshes");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/KinoRotunda/Meshes", "OrnamentBatches");
            var previous = root.transform.Find(RootName);
            if (previous)
            {
                // Restore the retained sources before collecting them for regeneration.
                foreach (var batch in previous.GetComponentsInChildren<KinoOrnamentBatch>(true))
                    foreach (var source in batch.sources) if (source) source.enabled = true;
                Object.DestroyImmediate(previous.gameObject);
            }
            var sources = root.GetComponentsInChildren<MeshRenderer>()
                .Where(r => r.enabled && r.name.StartsWith("Ball_", StringComparison.Ordinal) &&
                    !r.name.StartsWith("Ball_Tube_", StringComparison.Ordinal) &&
                    !r.name.StartsWith("Ball_Armillary_", StringComparison.Ordinal) &&
                    r.transform.parent && r.transform.parent.parent && r.transform.parent.parent.name == "Animation_Balls")
                .OrderBy(r => r.name, StringComparer.Ordinal).ToArray();
            var groupRoot = new GameObject(RootName).transform;
            groupRoot.SetParent(root.transform, false);
            // Keep each base/capital separate; split the wider stage into local cells.
            // Never combine the whole room or any tube/lottery animation.
            foreach (var group in sources.GroupBy(GroupKey).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var members = group.ToArray();
                var first = members[0];
                if (members.Any(r => r.sharedMaterials.Length != 1 || r.sharedMaterial != first.sharedMaterial ||
                    r.lightmapIndex != -1 || r.receiveGI != ReceiveGI.LightProbes))
                    throw new InvalidOperationException("Unsupported ornament material or lightmap: " + group.Key);
                var bounds = first.bounds;
                foreach (var r in members) bounds.Encapsulate(r.bounds);
                var go = new GameObject("Ball_Batch_" + group.Key);
                go.SetActive(false);
                go.transform.SetParent(groupRoot, false);
                go.transform.position = bounds.center;
                var combine = members.Select(r => new CombineInstance {
                    mesh = r.GetComponent<MeshFilter>().sharedMesh, subMeshIndex = 0,
                    transform = go.transform.worldToLocalMatrix * r.transform.localToWorldMatrix
                }).ToArray();
                var generated = new Mesh { name = group.Key };
                generated.CombineMeshes(combine, true, true, false);
                generated.RecalculateBounds();
                string path = Folder + "/" + group.Key + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh)
                {
                    EditorUtility.CopySerialized(generated, mesh);
                    Object.DestroyImmediate(generated);
                    EditorUtility.SetDirty(mesh);
                }
                else { mesh = generated; AssetDatabase.CreateAsset(mesh, path); }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = first.sharedMaterial;
                renderer.shadowCastingMode = first.shadowCastingMode;
                renderer.receiveShadows = first.receiveShadows;
                renderer.receiveGI = ReceiveGI.LightProbes;
                renderer.lightProbeUsage = first.lightProbeUsage;
                renderer.reflectionProbeUsage = first.reflectionProbeUsage;
                renderer.renderingLayerMask = first.renderingLayerMask;
                renderer.motionVectorGenerationMode = first.motionVectorGenerationMode;
                // Anchor to an existing ornament in this small group, at the same
                // height, rather than sampling light across an entire arch/room.
                renderer.probeAnchor = members.OrderBy(r => (r.bounds.center - bounds.center).sqrMagnitude).First().transform;
                var batch = go.AddComponent<KinoOrnamentBatch>();
                batch.sources = members;
                go.SetActive(true);
                foreach (var source in members)
                {
                    source.enabled = false;
                    EditorUtility.SetDirty(source);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                }
            }
        }

        static string GroupKey(MeshRenderer renderer)
        {
            string section = renderer.transform.parent.name;
            if (section.StartsWith("Balls_Arcade_", StringComparison.Ordinal))
                return section + (renderer.name.Contains("_Base_") ? "_Base" :
                    renderer.name.Contains("_Capital_Left_") ? "_Capital_Left" : "_Capital_Right");
            var p = renderer.bounds.center;
            return section + "_" + Mathf.FloorToInt(p.x / 2) + "_" + Mathf.FloorToInt(p.y / 2) + "_" + Mathf.FloorToInt(p.z / 2);
        }

        public static void Validate(GameObject root)
        {
            var batches = root.GetComponentsInChildren<KinoOrnamentBatch>();
            var sources = batches.SelectMany(b => b.sources).ToArray();
            if (sources.Length != 278 || sources.Distinct().Count() != 278 || sources.Any(r => !r || r.enabled))
                throw new InvalidOperationException("Expected 278 retained, disabled ornament sources.");
            long triangles = 0;
            foreach (var batch in batches)
            {
                var renderer = batch.GetComponent<MeshRenderer>();
                var mesh = batch.GetComponent<MeshFilter>().sharedMesh;
                long expected = batch.sources.Sum(r => (long)r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0));
                if (!renderer.enabled || mesh.GetIndexCount(0) != expected || mesh.subMeshCount != 1 ||
                    !mesh.HasVertexAttribute(VertexAttribute.TexCoord0) || !mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    throw new InvalidOperationException("Invalid combined ornament geometry: " + batch.name);
                var expectedBounds = batch.sources[0].bounds;
                foreach (var source in batch.sources) expectedBounds.Encapsulate(source.bounds);
                if (Vector3.Distance(renderer.bounds.center, expectedBounds.center) > .001f ||
                    Vector3.Distance(renderer.bounds.size, expectedBounds.size) > .001f)
                    throw new InvalidOperationException("Combined ornament bounds changed: " + batch.name);
                triangles += (long)mesh.GetIndexCount(0) / 3;
            }
            if (triangles != 20824) throw new InvalidOperationException("Ornament triangle total changed.");
            Debug.Log("KINO_ORNAMENT_BATCHES_VALID: " + sources.Length + " sources -> " + batches.Length + " local renderers, " + triangles + " triangles.");
        }
    }
}
