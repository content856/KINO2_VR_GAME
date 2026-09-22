using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KinoRotunda.Editor
{
    public class KinoPanoramaWindow : EditorWindow
    {
        [SerializeField] string sourcePath;
        [SerializeField] KinoPanoramaSkybox.Options options = new KinoPanoramaSkybox.Options();
        [SerializeField] string message;
        Texture2D preview;
        bool applying;
        Vector2 scroll;
        const string SyntagmaUrl = "https://www.360cities.net/image/sunset-over-syntagma-square-drone-aerial-view-athens-greece";

        [MenuItem("Tools/KINO Rotunda/360 Skybox")]
        public static void Open()
        {
            var window = GetWindow<KinoPanoramaWindow>("360 Skybox");
            window.minSize = new Vector2(520, 570);
            if (window.position.width < 530)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.x + 80, main.y + 60, 540, Mathf.Clamp(main.height - 100, 570, 780));
            }
            window.Show();
        }

        void OnEnable()
        {
            if (options == null) options = new KinoPanoramaSkybox.Options();
            EditorApplication.update += Repaint;
        }

        void OnDisable() { EditorApplication.update -= Repaint; }

        void OnGUI()
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 225;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("360° SKYBOX", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("KINO Rotunda · keep the current sun direction", EditorStyles.miniLabel);
            EditorGUILayout.Space();
            bool locked = applying || KinoPanoramaSkybox.IsBusy || EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(locked))
            {
                Rect drop = GUILayoutUtility.GetRect(0, 76, GUILayout.ExpandWidth(true));
                GUI.Box(drop, "Drop a 360° file here to apply it\nJPG / PNG / HDR / EXR · equirectangular 2:1");
                HandleDrop(drop);
                if (GUILayout.Button("Choose file and apply..."))
                {
                    string chosen = EditorUtility.OpenFilePanelWithFilters("Choose a 360° panorama", "",
                        new[] { "360 panoramas", "jpg,jpeg,png,hdr,exr" });
                    if (!string.IsNullOrEmpty(chosen))
                    {
                        options.manualSun = false;
                        QueueApply(chosen);
                    }
                }
                var selected = (Texture2D)EditorGUILayout.ObjectField("Or select texture", preview, typeof(Texture2D), false);
                if (selected != preview)
                {
                    preview = selected;
                    sourcePath = selected ? AssetDatabase.GetAssetPath(selected) : "";
                    options.manualSun = false;
                }
                if (!string.IsNullOrEmpty(sourcePath)) EditorGUILayout.LabelField(Path.GetFileName(sourcePath), EditorStyles.wordWrappedMiniLabel);
                if (preview)
                {
                    Rect rect = GUILayoutUtility.GetRect(0, (position.width - 36) / 2, GUILayout.ExpandWidth(true));
                    GUI.DrawTexture(rect, preview, ScaleMode.StretchToFill);
                    if (options.manualSun)
                    {
                        float x = rect.x + options.sunUV.x * rect.width;
                        float y = rect.y + (1 - options.sunUV.y) * rect.height;
                        EditorGUI.DrawRect(new Rect(x - 8, y - 1, 16, 2), Color.yellow);
                        EditorGUI.DrawRect(new Rect(x - 1, y - 8, 2, 16), Color.yellow);
                    }
                    if (GUI.enabled && Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        options.sunUV = new Vector2((Event.current.mousePosition.x - rect.x) / rect.width,
                            1 - (Event.current.mousePosition.y - rect.y) / rect.height);
                        options.manualSun = true;
                        Event.current.Use();
                    }
                    EditorGUILayout.LabelField("Click the sun in the image to correct automatic alignment.", EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.Space();
                options.alignSun = EditorGUILayout.Toggle("Align panorama to scene sun", options.alignSun);
                using (new EditorGUI.DisabledScope(!options.alignSun))
                {
                    options.manualSun = EditorGUILayout.Toggle("Use picked sun position", options.manualSun);
                    if (options.manualSun) options.sunUV = EditorGUILayout.Vector2Field("Sun UV (bottom-left origin)", options.sunUV);
                }
                options.rotationOffset = EditorGUILayout.Slider("Extra yaw", options.rotationOffset, -180, 180);
                options.cropped360 = EditorGUILayout.Toggle("Cropped 360° strip", options.cropped360);
                if (options.cropped360)
                {
                    options.cropBottomElevation = EditorGUILayout.FloatField("Strip bottom elevation", options.cropBottomElevation);
                    EditorGUILayout.HelpBox("Pick the sun in the preview. The original aspect ratio sets the angular scale; uncaptured poles use a colour gradient.", MessageType.Info);
                }
                options.exposure = EditorGUILayout.Slider("Sky exposure", options.exposure, .05f, 4);
                options.desktopSize = EditorGUILayout.IntPopup("Desktop resolution", options.desktopSize, new[] { "4K", "8K" }, new[] { 4096, 8192 });
                options.questSize = EditorGUILayout.IntPopup("Quest resolution", options.questSize, new[] { "2K", "4K" }, new[] { 2048, 4096 });
                options.bakeLighting = EditorGUILayout.Toggle("Bake scene lighting", options.bakeLighting);
                options.updateReflections = EditorGUILayout.Toggle("Refresh active reflections", options.updateReflections);
                options.sourceUrl = EditorGUILayout.TextField("Source / licence URL", options.sourceUrl);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(sourcePath)))
                    if (GUILayout.Button("Apply / rebake selected panorama")) QueueApply(sourcePath);
            }
            EditorGUILayout.HelpBox("The Directional Light keeps its rotation, colour and intensity. Only panorama yaw changes; the horizon stays level. A different photographed sun elevation cannot be matched by yaw alone.", MessageType.Info);
            EditorGUILayout.HelpBox("JPG/PNG is converted to linear EXR, preserving its existing dynamic range. HDR/EXR keeps the original HDR values. The import also saves a scene backup.", MessageType.None);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorGUILayout.LabelField(KinoPanoramaSkybox.Status, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            if (GUILayout.Button("Open selected Syntagma panorama source")) Application.OpenURL(SyntagmaUrl);
            EditorGUILayout.LabelField("Syntagma requires a downloaded image licensed for the VR game. Hosted Embed does not include the file.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        void HandleDrop(Rect rect)
        {
            if (!GUI.enabled) return;
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            string path = DragAndDrop.paths.Length == 1 ? DragAndDrop.paths[0] : "";
            if (!KinoPanoramaSkybox.Supported(path)) { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; return; }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                options.manualSun = false;
                QueueApply(path);
            }
            e.Use();
        }

        void QueueApply(string path)
        {
            sourcePath = path;
            applying = true;
            // Import/reimport outside OnGUI, which can be invoked repeatedly in the same frame.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var result = KinoPanoramaSkybox.ApplyFile(path, options);
                    sourcePath = result.sourcePath;
                    preview = AssetDatabase.LoadAssetAtPath<Texture2D>(result.sourcePath);
                    message = result.note + (result.aligned ? $" Panorama yaw: {result.rotation:F2}°. Elevation difference: {result.sunElevationDifference:F2}°." : "");
                }
                catch (Exception ex) { message = ex.Message; KinoPanoramaSkybox.WriteStatus("ERROR: " + ex.Message); Debug.LogException(ex); }
                finally { applying = false; Repaint(); }
            };
        }
    }
}
