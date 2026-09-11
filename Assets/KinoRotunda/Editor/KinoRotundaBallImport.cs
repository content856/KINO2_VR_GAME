using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KinoRotunda.Editor
{
    // Moving ornaments must retain individual transforms and use probe lighting.
    public sealed class KinoRotundaBallImport : AssetPostprocessor
    {
        public static void ValidateImportedBalls()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KinoRotunda/Models/KinoRotunda.fbx");
            var balls = new System.Collections.Generic.List<string>();
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith("Ball_", System.StringComparison.Ordinal)) continue;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) != 0 ||
                    renderer.receiveGI != ReceiveGI.LightProbes ||
                    renderer.lightProbeUsage != LightProbeUsage.BlendProbes ||
                    mesh.bounds.center.magnitude > .001f ||
                    !mesh.HasVertexAttribute(VertexAttribute.TexCoord0) ||
                    !mesh.HasVertexAttribute(VertexAttribute.TexCoord1) ||
                    renderer.sharedMaterial == null || renderer.sharedMaterial.name != "BrushedGold" ||
                    renderer.transform.parent.parent.name != "Animation_Balls")
                    throw new System.InvalidOperationException("Invalid animated ball: " + renderer.name);
                balls.Add(renderer.name);
            }
            int tubeBalls = balls.FindAll(n => n.StartsWith("Ball_Tube_", System.StringComparison.Ordinal)).Count;
            if (balls.Count - tubeBalls != 292 || (tubeBalls != 0 && tubeBalls != 63))
                throw new System.InvalidOperationException("Expected 292 original balls and zero or 63 tube balls, found " + balls.Count);
            const string output = "Artifacts/KinoRotunda/SeparateBalls";
            System.IO.Directory.CreateDirectory(output);
            System.IO.File.WriteAllText(output + "/unity-validation.json", JsonUtility.ToJson(new BallValidation {
                balls = balls.Count, independentTransforms = true, centredPivots = true,
                nonStatic = true, probeLighting = true, materialsAndUVsValid = true
            }, true));
            Debug.Log("[KinoRotunda] Validated " + balls.Count + " independent, non-static balls with centred pivots.");
        }

        [System.Serializable]
        sealed class BallValidation
        {
            public int balls;
            public bool independentTransforms, centredPivots, nonStatic, probeLighting, materialsAndUVsValid;
        }

        void OnPostprocessModel(GameObject model)
        {
            if (assetPath != "Assets/KinoRotunda/Models/KinoRotunda.fbx") return;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                KinoLottery.ConfigureRenderer(renderer);
                KinoPerimeterTubes.ConfigureRenderer(renderer);
                if (!renderer.name.StartsWith("Ball_", System.StringComparison.Ordinal)) continue;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
                renderer.receiveGI = ReceiveGI.LightProbes;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            }
        }
    }
}
