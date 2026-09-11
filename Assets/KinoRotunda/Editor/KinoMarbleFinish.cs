using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KinoRotunda.Editor
{
    /// <summary>A material-only blue polish for the existing dark marble.</summary>
    public static class KinoMarbleFinish
    {
        const string Root = "Assets/KinoRotunda";
        const string Output = "Artifacts/KinoRotunda/BlueMarble";

        [MenuItem("Tools/KINO Rotunda/9 - Blue marble polish")]
        public static void Apply()
        {
            if (SceneManager.GetActiveScene().path != Root + "/Scenes/KinoRotunda.unity")
                throw new InvalidOperationException("Open the KINO scene first.");
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the light bake.");
            Directory.CreateDirectory(Output);
            var marble = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/NeroMarble.mat");
            var screen = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Screen.mat");
            string screenBefore = EditorJsonUtility.ToJson(screen);
            string lightingBefore = LightingState();
            if (!File.Exists(Output + "/NeroMarble-before.json"))
            {
                File.WriteAllText(Output + "/NeroMarble-before.json", EditorJsonUtility.ToJson(marble, true));
                KinoRotundaBuilder.Capture();
                File.Copy("Artifacts/KinoRotunda/UnityPreview.png", Output + "/Before.png", true);
            }
            Undo.RecordObject(marble, "Blue marble polish");
            // Lift the blue already present in the stone texture. The texture and
            // normal map stay intact; no emission is added to the architecture.
            // Balance green against the warm veins to keep the blue from reading violet.
            marble.SetColor("_BaseColor", new Color(.85f, 1.38f, 2.15f, 1));
            marble.SetColor("_Color", marble.GetColor("_BaseColor"));
            // A restrained blue specular tint gives an angle-dependent polished
            // sheen through URP's Fresnel response, using the standard Lit shader.
            marble.SetFloat("_WorkflowMode", 0);
            marble.EnableKeyword("_SPECULAR_SETUP");
            // Color properties are authored in sRGB; these correspond to a small
            // dielectric reflectance with a slightly stronger blue component.
            marble.SetColor("_SpecColor", new Color(.18f, .25f, .30f, 1));
            EditorUtility.SetDirty(marble);
            AssetDatabase.SaveAssets();
            KinoRotundaBuilder.Capture();
            File.Copy("Artifacts/KinoRotunda/UnityPreview.png", Output + "/After.png", true);
            KinoRotundaBuilder.Validate();
            if (screenBefore != EditorJsonUtility.ToJson(screen) || lightingBefore != LightingState())
                throw new InvalidOperationException("Unexpected change to the screen or lighting setup.");
            File.WriteAllText(Output + "/verification.txt", "Standard URP Lit shader; blue albedo and specular tint.\n" +
                "Existing textures, normal map, smoothness, lightmaps, lights, HDRI, screen and reflection settings preserved.\n" +
                "No added emission, shader passes, geometry or runtime lights.\n");
            File.WriteAllText("Artifacts/KinoRotunda/unity-status.txt", DateTime.Now.ToString("s") + " BLUE_MARBLE_READY");
        }

        static string LightingState()
        {
            var root = GameObject.Find("KINO Rotunda • Environment");
            return EditorJsonUtility.ToJson(RenderSettings.skybox) + "\n" +
                string.Join("\n", root.GetComponentsInChildren<Light>(true).Select(l => EditorJsonUtility.ToJson(l))) + "\n" +
                string.Join("\n", root.GetComponentsInChildren<ReflectionProbe>(true).Select(p => EditorJsonUtility.ToJson(p)));
        }
    }
}
