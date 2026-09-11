using UnityEditor;
using UnityEngine;

namespace KinoRotunda.Editor
{
    // Add convenient controls to this material's header while retaining Unity's
    // standard URP inspector and shader for every material, including this one.
    [InitializeOnLoad]
    public static class KinoMarbleControls
    {
        const string MaterialPath = "Assets/KinoRotunda/Materials/NeroMarble.mat";

        static KinoMarbleControls()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawControls;
        }

        [MenuItem("Tools/KINO Rotunda/10 - Edit marble colours")]
        public static void SelectMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
        }

        static void DrawControls(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1 || !(editor.target is Material material) ||
                AssetDatabase.GetAssetPath(material) != MaterialPath ||
                !material.HasProperty("_BaseColor") || !material.HasProperty("_SpecColor")) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Marble colours", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                // HDR keeps the current tint's brightness intact when its hue is edited.
                var marbleColour = EditorGUILayout.ColorField(new GUIContent("Marble Color",
                    "Tint of the marble and its veins. Changes appear immediately in the scene."),
                    material.GetColor("_BaseColor"), true, false, true);
                bool marbleChanged = EditorGUI.EndChangeCheck();
                EditorGUI.BeginChangeCheck();
                var sheenColour = EditorGUILayout.ColorField(new GUIContent("Sheen Color",
                    "Colour of the polished reflections. Use a subtle tint for a natural finish."),
                    material.GetColor("_SpecColor"), true, false, false);
                bool sheenChanged = EditorGUI.EndChangeCheck();
                if (!marbleChanged && !sheenChanged) return;

                Undo.RecordObject(material, "Change marble colours");
                if (marbleChanged)
                {
                    material.SetColor("_BaseColor", marbleColour);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", marbleColour);
                }
                if (sheenChanged)
                {
                    material.SetFloat("_WorkflowMode", 0);
                    material.EnableKeyword("_SPECULAR_SETUP");
                    material.SetColor("_SpecColor", sheenColour);
                }
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                editor.Repaint();
            }
        }
    }
}
