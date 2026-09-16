using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static class KinoNumberBatchSetup
    {
        public const string GeometryPath = "Assets/KINOVR/Meshes/KinoNumberGeometry.asset";
        const string MaterialPath = "Assets/KINOVR/Materials/DecorativeNumberSDF.mat";
        const string BallPath = "Assets/KINOVR/Prefabs/NumberedBall.prefab";
        const string DecorativePath = "Assets/KINOVR/Prefabs/DecorativeNumberedBall.prefab";
        const string AirPath = "Assets/KINOVR/Prefabs/KinoAirBalls.prefab";

        [MenuItem("Tools/KINO VR/Air balls/5 - Batch decorative numbers")]
        public static void Upgrade()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            GenerateGeometry();
            var visual = PrefabUtility.LoadPrefabContents(DecorativePath);
            try { ConfigureDecoration(visual); PrefabUtility.SaveAsPrefabAsset(visual, DecorativePath); }
            finally { PrefabUtility.UnloadPrefabContents(visual); }
            var air = PrefabUtility.LoadPrefabContents(AirPath);
            try { ConfigureChambers(air); PrefabUtility.SaveAsPrefabAsset(air, AirPath); }
            finally { PrefabUtility.UnloadPrefabContents(air); }
            AssetDatabase.SaveAssets();
        }

        public static void GenerateGeometry()
        {
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BallPath));
            try
            {
                var visual = source.GetComponent<KinoBallNumber>();
                var label = visual.numberLabel;
                var geometry = AssetDatabase.LoadAssetAtPath<KinoNumberGeometry>(GeometryPath);
                if (!geometry)
                {
                    geometry = ScriptableObject.CreateInstance<KinoNumberGeometry>();
                    AssetDatabase.CreateAsset(geometry, GeometryPath);
                }
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (!material)
                {
                    material = new Material(label.fontSharedMaterial) { name = "DecorativeNumberSDF" };
                    AssetDatabase.CreateAsset(material, MaterialPath);
                }
                else material.CopyPropertiesFromMaterial(label.fontSharedMaterial);
                // TMP's mesh shader uses this same depth state for world-space text.
                material.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
                geometry.material = material;
                geometry.numbers = new KinoNumberGeometry.NumberShape[80];
                var transform = visual.face.worldToLocalMatrix * label.transform.localToWorldMatrix;
                var uv = new List<Vector4>();
                for (int number = 1; number <= 80; number++)
                {
                    label.text = number.ToString();
                    label.ForceMeshUpdate(true, true);
                    if (label.textInfo.materialCount != 1) throw new InvalidOperationException("Number uses multiple font atlases.");
                    int count = label.textInfo.meshInfo[0].vertexCount;
                    if (count != (number < 10 ? 4 : 8)) throw new InvalidOperationException("Unexpected number glyph geometry: " + number);
                    var mesh = label.mesh;
                    mesh.GetUVs(0, uv);
                    var shape = new KinoNumberGeometry.NumberShape {
                        vertices = mesh.vertices.Take(count).Select(transform.MultiplyPoint3x4).ToArray(),
                        uv = uv.Take(count).ToArray(), colors = mesh.colors32.Take(count).ToArray(),
                        triangles = mesh.triangles.Take(count / 4 * 6).ToArray()
                    };
                    for (int v = 0; v < count; v++) shape.uv[v].w /= Mathf.Abs(source.transform.lossyScale.y);
                    geometry.numbers[number - 1] = shape;
                }
                EditorUtility.SetDirty(material);
                EditorUtility.SetDirty(geometry);
            }
            finally { Object.DestroyImmediate(source); }
        }

        public static void ConfigureDecoration(GameObject visual)
        {
            var number = visual.GetComponent<KinoBallNumber>();
            number.numberLabel = null;
            number.enabled = false; // Its chamber batch calls RefreshFacing once per frame.
            foreach (var canvas in visual.GetComponentsInChildren<Canvas>(true)) Object.DestroyImmediate(canvas.gameObject);
        }

        public static void ConfigureChambers(GameObject root)
        {
            var geometry = AssetDatabase.LoadAssetAtPath<KinoNumberGeometry>(GeometryPath);
            if (!geometry || geometry.numbers.Length != 80) throw new InvalidOperationException("Generate the number geometry first.");
            foreach (var chamber in root.GetComponentsInChildren<KinoAirChamber>(true))
            {
                for (int i = 0; i < chamber.balls.Length; i++)
                {
                    var ball = chamber.balls[i];
                    ball.SetNumber(chamber.numbers[i], null);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(ball);
                }
                var batch = chamber.GetComponentInChildren<KinoNumberBatch>(true);
                if (!batch)
                {
                    var go = new GameObject("Batched numbers");
                    go.transform.SetParent(chamber.transform, false);
                    batch = go.AddComponent<KinoNumberBatch>();
                }
                batch.chamber = chamber;
                batch.geometry = geometry;
                if (!batch.farNumbers)
                {
                    var far = new GameObject("Numbers behind glass", typeof(MeshFilter), typeof(MeshRenderer));
                    far.transform.SetParent(batch.transform, false);
                    batch.farNumbers = far.GetComponent<MeshFilter>();
                }
                string glassName = chamber.kind == KinoAirChamber.ChamberKind.Lottery ? "Kino_Armillary__LotteryGlass" : $"Tube_{chamber.seed:00}__TubeGlass";
                var glass = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == glassName);
                if (!glass) throw new InvalidOperationException("Missing glass sorting reference: " + glassName);
                batch.glassCenter = chamber.transform.InverseTransformPoint(glass.bounds.center);
                batch.Refresh();
                EditorUtility.SetDirty(batch);
            }
        }
    }
}
