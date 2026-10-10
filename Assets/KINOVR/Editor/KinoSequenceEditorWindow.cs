using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace KinoVR.Editor
{
    // Simple sequence editor: drag to reorder, tick to enable, set each screen's duration.
    // Edits the KinoTimedGameplay prefab, or the live controller while in Play mode.
    public sealed class KinoSequenceEditorWindow : EditorWindow
    {
        SerializedObject target, roundTarget;
        ReorderableList preList, postList;
        Vector2 scroll;
        KinoExperienceController controller;

        [MenuItem("Tools/KINO VR/Experience Sequence", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<KinoSequenceEditorWindow>("KINO Sequence");
            window.minSize = new Vector2(420, 520);
            window.Show();
        }

        void OnEnable() { Bind(); EditorApplication.playModeStateChanged += PlayModeChanged; }
        void OnDisable() => EditorApplication.playModeStateChanged -= PlayModeChanged;
        void PlayModeChanged(PlayModeStateChange change) { Bind(); Repaint(); }
        void OnFocus() { if (target == null || !controller) Bind(); }

        void Bind()
        {
            controller = null;
            if (EditorApplication.isPlaying) controller = FindFirstObjectByType<KinoExperienceController>();
            if (!controller)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KinoExperienceSetup.PrefabPath);
                controller = prefab ? prefab.GetComponent<KinoExperienceController>() : null;
            }
            target = controller ? new SerializedObject(controller) : null;
            var round = controller ? (controller.round ? controller.round : controller.GetComponent<KinoRoundController>()) : null;
            roundTarget = round ? new SerializedObject(round) : null;
            if (target == null) return;
            if (controller.preGame == null) controller.preGame = KinoSequence.DefaultPreGame();
            if (controller.postGame == null) controller.postGame = KinoSequence.DefaultPostGame();
            bool fixedPre = KinoSequence.Sanitize(controller.preGame, KinoSequence.PreGameSteps);
            bool fixedPost = KinoSequence.Sanitize(controller.postGame, KinoSequence.PostGameSteps);
            if (fixedPre || fixedPost) { EditorUtility.SetDirty(controller); target.Update(); }
            preList = MakeList(target.FindProperty("preGame"), "Before the game", true);
            postList = MakeList(target.FindProperty("postGame"), "After the game", false);
        }

        ReorderableList MakeList(SerializedProperty property, string header, bool preGame)
        {
            var list = new ReorderableList(target, property, true, true, false, false)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 8,
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, header, EditorStyles.boldLabel),
            };
            list.drawElementCallback = (rect, index, active, focused) =>
            {
                var element = property.GetArrayElementAtIndex(index);
                var enabled = element.FindPropertyRelative("enabled");
                var step = (KinoSequenceStep)element.FindPropertyRelative("step").enumValueIndex;
                rect.y += 4; rect.height = EditorGUIUtility.singleLineHeight;
                var toggle = new Rect(rect.x, rect.y, 20, rect.height);
                enabled.boolValue = EditorGUI.Toggle(toggle, enabled.boolValue);
                using (new EditorGUI.DisabledScope(!enabled.boolValue))
                {
                    var label = new Rect(rect.x + 24, rect.y, rect.width - 24 - 150, rect.height);
                    string note = preGame ? Note(step, index) : "";
                    EditorGUI.LabelField(label, new GUIContent(KinoSequence.Label(step) + note));
                    var duration = DurationProperty(step);
                    var field = new Rect(rect.xMax - 140, rect.y, 140, rect.height);
                    if (duration != null)
                    {
                        float old = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 70;
                        duration.floatValue = Mathf.Max(step == KinoSequenceStep.RemoveHeadset ? 2 : 1, EditorGUI.FloatField(field, "Seconds", duration.floatValue));
                        EditorGUIUtility.labelWidth = old;
                    }
                    else EditorGUI.LabelField(field, step == KinoSequenceStep.Ready ? "waits for touch" : "", EditorStyles.miniLabel);
                }
            };
            return list;
        }

        string Note(KinoSequenceStep step, int index)
        {
            int ready = ReadyPosition();
            if (ready < 0 || step == KinoSequenceStep.Ready) return "";
            return index < ready ? "   (app start only)" : "";
        }

        int ReadyPosition()
        {
            var pre = target.FindProperty("preGame");
            for (int i = 0; i < pre.arraySize; i++)
            {
                var e = pre.GetArrayElementAtIndex(i);
                if ((KinoSequenceStep)e.FindPropertyRelative("step").enumValueIndex == KinoSequenceStep.Ready &&
                    e.FindPropertyRelative("enabled").boolValue) return i;
            }
            return -1;
        }

        float Seconds(string property, string label, string tooltip, float minimum)
        {
            var p = roundTarget.FindProperty(property);
            if (p == null) return 0;
            p.floatValue = Mathf.Max(minimum, EditorGUILayout.FloatField(new GUIContent(label + " (s)", tooltip), p.floatValue));
            return p.floatValue;
        }

        SerializedProperty DurationProperty(KinoSequenceStep step)
        {
            switch (step)
            {
                case KinoSequenceStep.AllwynLogo:
                case KinoSequenceStep.KinoLogo: return target.FindProperty("brandingSeconds");
                case KinoSequenceStep.Safety: return target.FindProperty("safetySeconds");
                case KinoSequenceStep.Welcome: return target.FindProperty("introductionSeconds");
                case KinoSequenceStep.KinoSplash: return target.FindProperty("kinoSplashSeconds");
                case KinoSequenceStep.Score: return target.FindProperty("finaleSeconds");
                case KinoSequenceStep.RemoveHeadset: return target.FindProperty("closingSeconds");
                default: return null;
            }
        }

        void OnGUI()
        {
            if (target == null || !controller)
            {
                EditorGUILayout.HelpBox("KinoTimedGameplay prefab with a KinoExperienceController was not found. Run Tools > KINO VR > Experience > 1 - Apply visitor flow first.", MessageType.Warning);
                if (GUILayout.Button("Retry")) Bind();
                return;
            }
            target.Update();
            roundTarget?.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(EditorApplication.isPlaying ? "Editing the running scene (changes are lost when Play stops)" :
                "Editing " + KinoExperienceSetup.PrefabPath, EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Drag to reorder, tick to enable. Steps above the Ready screen play once when the app starts; steps below it play for every visitor. The KINO logo duration is shared with the Allwyn logo.", MessageType.None);
            preList.DoLayoutList();

            EditorGUILayout.LabelField("The game", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Main round, then Second Chance (always)");
                EditorGUILayout.PropertyField(target.FindProperty("includeBoost"), new GUIContent("KINO Boost wave (inactive at launch)", "Activation runs the Boost wave after Second Chance for every visitor, without buttons."));
                EditorGUILayout.PropertyField(target.FindProperty("offerBoostSelection"), new GUIContent("Operator NORMAL / ΜΕ BOOST menu", "Shows mode buttons before the Ready screen. Leave off to run Boost automatically."));
                EditorGUILayout.PropertyField(target.FindProperty("roundStartDelay"), new GUIContent("Seconds before the first ball", "Time in the revealed room after the KINO splash before the first ball launches."));
            }
            if (roundTarget != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Second Chance fake-out", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    float hold = Seconds("secondChanceBoardHold", "Board hold", "The final board stays visible after the last ball.", 0);
                    float fade = Seconds("secondChanceFadeOut", "Fade to black", "Slow fade to black, as if the game were over. The music fades with it.", .1f);
                    float black = Seconds("secondChanceBlackHold", "Black hold", "Silence in black before Second Chance appears.", 0);
                    float reveal = Seconds("secondChanceRevealFade", "Fade in", "Fade back in on the Second Chance announcement.", .1f);
                    float reading = Seconds("secondChanceReading", "Reading time", "Announcement fully visible; the first green ball launches when it ends.", .5f);
                    EditorGUILayout.LabelField(" ", string.Format("Black at {0:0.#} s, announcement at {1:0.#} s, first green ball at {2:0.#} s after the last ball.",
                        hold + fade, hold + fade + black, hold + fade + black + reveal + reading), EditorStyles.miniLabel);
                    if (GUILayout.Button("Reset fake-out timing"))
                    {
                        roundTarget.FindProperty("secondChanceBoardHold").floatValue = KinoRoundState.BoardHoldSeconds;
                        roundTarget.FindProperty("secondChanceFadeOut").floatValue = KinoRoundState.FadeOutSeconds;
                        roundTarget.FindProperty("secondChanceBlackHold").floatValue = KinoRoundState.BlackHoldSeconds;
                        roundTarget.FindProperty("secondChanceRevealFade").floatValue = KinoRoundState.RevealFadeSeconds;
                        roundTarget.FindProperty("secondChanceReading").floatValue = KinoRoundState.ReadingSeconds;
                    }
                }
            }
            EditorGUILayout.Space(6);
            postList.DoLayoutList();
            EditorGUILayout.PropertyField(target.FindProperty("outroOnEnvironment"), new GUIContent("Outro on blue environment",
                "After the score the level fades out and the remove-headset message appears on the wave environment. Off: it appears over the dimmed room."));

            Warnings();
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset to default order"))
                {
                    Undo.RecordObject(controller, "Reset KINO sequence");
                    controller.preGame = KinoSequence.DefaultPreGame();
                    controller.postGame = KinoSequence.DefaultPostGame();
                    EditorUtility.SetDirty(controller);
                    Bind();
                    GUIUtility.ExitGUI();
                }
                if (!EditorApplication.isPlaying && GUILayout.Button("Select prefab"))
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(KinoExperienceSetup.PrefabPath);
            }
            EditorGUILayout.EndScrollView();
            bool changed = target.ApplyModifiedProperties();
            if (roundTarget != null) changed |= roundTarget.ApplyModifiedProperties();
            if (changed && !EditorApplication.isPlaying) AssetDatabase.SaveAssetIfDirty(controller.gameObject);
        }

        void Warnings()
        {
            var pre = target.FindProperty("preGame");
            int ready = ReadyPosition(), safety = -1, enabledPre = 0;
            for (int i = 0; i < pre.arraySize; i++)
            {
                var e = pre.GetArrayElementAtIndex(i);
                if (!e.FindPropertyRelative("enabled").boolValue) continue;
                enabledPre++;
                if ((KinoSequenceStep)e.FindPropertyRelative("step").enumValueIndex == KinoSequenceStep.Safety) safety = i;
            }
            if (ready < 0)
                EditorGUILayout.HelpBox("Ready screen is off: every visitor gets the whole list and the game starts automatically, then loops for the next visitor.", MessageType.Warning);
            if (safety < 0)
                EditorGUILayout.HelpBox("Safety message is off.", MessageType.Warning);
            else if (ready >= 0 && safety < ready)
                EditorGUILayout.HelpBox("Safety message is above the Ready screen, so it plays once at app start and not for each visitor, and no session record is written for it.", MessageType.Warning);
            if (enabledPre == 0)
                EditorGUILayout.HelpBox("No screens before the game.", MessageType.Info);
            if (target.FindProperty("offerBoostSelection").boolValue)
                EditorGUILayout.HelpBox("The operator menu is on: the KINO Boost wave follows the menu choice instead of the toggle above.", MessageType.Info);
        }
    }
}
