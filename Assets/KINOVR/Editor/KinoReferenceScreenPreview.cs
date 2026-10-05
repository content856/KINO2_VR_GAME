using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    // Exercises the runtime presentations in a disposable, unsaved copy of the scene.
    // Batch entry: -executeMethod KinoVR.Editor.KinoReferenceScreenPreview.CaptureBatch
    [InitializeOnLoad]
    public static class KinoReferenceScreenPreview
    {
        const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        const string Output = "Artifacts/ReferenceScreens";
        const string OpenEditorRequest = Output + "/capture-and-test.request";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static KinoReferenceScreenPreview()
        {
            EditorApplication.update += ProcessOpenEditorRequest;
        }

        static void ProcessOpenEditorRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(OpenEditorRequest)) return;
            File.Delete(OpenEditorRequest);
            try
            {
                File.WriteAllText(Output + "/open-editor-request.txt", "RUNNING\n");
                CaptureAndTestInOpenEditor();
                File.WriteAllText(Output + "/open-editor-request.txt", "STARTED: captures validated; full Normal/Boost sessions running in the existing editor.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/open-editor-request.txt", "FAIL: " + error);
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/KINO VR/Experience/Apply, capture and test in open editor")]
        public static void CaptureAndTestInOpenEditor()
        {
            RequireCleanEditMode();
            KinoExperienceSetup.Apply();
            Capture();
            KinoExperienceTests.RunDesktopInOpenEditor();
        }

        static void RequireCleanEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before capturing reference screens.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before capturing reference screens.");
        }

        public static void CaptureBatch()
        {
            RunBatch(false);
        }

        public static void ApplyAndCaptureBatch()
        {
            RunBatch(true);
        }

        public static void ApplyCaptureAndTestBatch()
        {
            RunBatch(true, true);
        }

        static void RunBatch(bool apply, bool play = false)
        {
            try
            {
                if (apply) KinoExperienceSetup.Apply();
                Capture();
                if (play) KinoExperienceTests.RunBatch();
                else EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/validation.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/KINO VR/Experience/Capture reference-style screens")]
        public static void Capture()
        {
            RequireCleanEditMode();

            Directory.CreateDirectory(Output);
            var previousScenes = EditorSceneManager.GetSceneManagerSetup();
            GameObject cameraObject = null;
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                KinoExperienceSetup.Validate();
                KinoSecondChanceTests.ValidateRulesAndAssets();
                KinoMainSpecialTests.ValidateRules();

                var round = Object.FindFirstObjectByType<KinoRoundController>();
                if (!round || !round.experience || !round.playerView || !round.secondChancePresentation)
                    throw new InvalidOperationException("The main scene is missing its runtime screen references.");
                // Runtime reparenting is allowed in Play; unpack only this disposable
                // edit-mode fixture so the same docking code can run for screenshots.
                var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(round.gameObject);
                if (instance) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                var flow = round.experience;
                flow.writeLocalRecords = false;
                Invoke(round.playerView, "Awake");
                Invoke(flow, "Awake");
                flow.content.alpha = 0;
                if (flow.modeCanvas) flow.modeCanvas.gameObject.SetActive(false);
                flow.enclosure.SetSessionFade(0);
                flow.enclosure.SetBackground(0);

                cameraObject = new GameObject("Reference screen capture camera");
                var camera = cameraObject.AddComponent<Camera>();
                var view = round.playerView.View;
                var source = view.GetComponent<Camera>();
                if (source) camera.CopyFrom(source);
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.aspect = 16f / 9;
                camera.transform.SetPositionAndRotation(view.position, view.rotation);
                float playerFieldOfView = camera.fieldOfView;

                var draw = ResolvedDraw();
                round.secondChancePresentation.Present(draw, 65.4);
                KinoExperienceTests.ValidateBoardOverlay(round, round.secondChancePresentation.announcementCanvas);
                CapturePair(camera, round.secondChancePresentation.announcementCanvas, round.board,
                    "SecondChance", playerFieldOfView);
                round.secondChancePresentation.ResetPresentation();
                if (!round.board.NumbersVisible || round.board.numberGrid.alpha < .999f)
                    throw new InvalidOperationException("Resetting Second Chance did not reveal the numbers again.");

                // Follow legal state transitions without starting the launcher or writing a session record.
                var state = flow.State;
                state.Begin(0);
                double now = 0;
                foreach (float duration in new[] { flow.startupSeconds, flow.safetySeconds,
                    flow.BrandingDuration, flow.introductionSeconds })
                {
                    now += duration + .01;
                    state.Advance(now, flow.startupSeconds, flow.safetySeconds, flow.BrandingDuration,
                        flow.introductionSeconds, flow.finaleSeconds, flow.closingSeconds, false);
                }
                if (!state.BeginSecondChance(now) || !state.Finish(now + 1))
                    throw new InvalidOperationException("Could not reach Finale through the session state machine.");
                typeof(KinoExperienceController).GetProperty("Record").SetValue(flow,
                    new KinoSessionRecord { sessionId = "reference-preview", score = 44, completedUtc = "preview" });
                typeof(KinoExperienceController).GetField("clock", PrivateInstance).SetValue(flow, now + 3);
                Invoke(flow, "PresentStage");
                Invoke(flow, "AnimateStage");
                KinoExperienceTests.ValidateBoardOverlay(round, flow.contentCanvas);
                if (flow.kinoLogo.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Finale repeats the KINO logo already on the board.");
                CapturePair(camera, flow.contentCanvas, round.board, "EndPanelScore", playerFieldOfView);

                // Ensure the decorative score layout still accommodates plausible multi-digit results.
                flow.Record.score = 144;
                Invoke(flow, "PresentStage");
                Invoke(flow, "AnimateStage");
                RefreshText(flow.contentCanvas);
                foreach (var label in flow.contentCanvas.GetComponentsInChildren<TMP_Text>())
                    if (label.isTextOverflowing)
                        throw new InvalidOperationException("Finale text overflows with a three-digit score: " + label.name);

                File.WriteAllText(Output + "/validation.txt",
                    "PASS: existing experience state, second-chance rules/assets, and main-special rules.\n" +
                    "Runtime second-chance reveal and finale presentations rendered in the main scene.\n" +
                    "Both overlays fit the board number field, hide live numbers and retain the board header; finale has no duplicate logo.\n" +
                    "SecondChance-player-view.png and EndPanelScore-player-view.png use the player camera pose/FOV.\n" +
                    "Board closeups turn from the same player position to fit the entire board, including its existing KINO header.\n" +
                    "Finale also checked for text overflow with score 144. Preview score 44 is fixture data.\n");
                Debug.Log("KINO_REFERENCE_SCREENS_READY");
            }
            finally
            {
                if (cameraObject) Object.DestroyImmediate(cameraObject);
                // Never save the runtime-only objects or fixture record into the scene or prefab.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (previousScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            }
        }

        static KinoRoundState ResolvedDraw()
        {
            var state = new KinoRoundState();
            state.Begin(60, 0);
            for (int i = 0; i < 20; i++)
            {
                if (!state.TryRegisterNormalLaunch(i * 3) || !state.TryMiss(false))
                    throw new InvalidOperationException("Could not prepare a resolved draw for the reveal.");
            }
            state.Tick(60);
            if (!state.TryBeginSecondChanceTransition(60))
                throw new InvalidOperationException("Could not enter the second-chance transition.");
            state.Tick(63);
            state.Tick(64);
            if (state.Phase != KinoRoundPhase.SecondChanceReveal)
                throw new InvalidOperationException("The reveal fixture is in the wrong phase.");
            return state;
        }

        static void CapturePair(Camera camera, Canvas canvas, KinoNumberBoard board, string name, float playerFieldOfView)
        {
            RefreshText(canvas);
            foreach (var label in canvas.GetComponentsInChildren<TMP_Text>())
                if (label.isTextOverflowing)
                    throw new InvalidOperationException(name + " text overflows: " + label.name);
            camera.fieldOfView = playerFieldOfView;
            Render(camera, Output + "/" + name + "-player-view.png");
            var corners = new Vector3[4];
            var boardRect = (RectTransform)board.transform;
            boardRect.GetWorldCorners(corners);
            var playerRotation = camera.transform.rotation;
            try
            {
                var boardCenter = boardRect.TransformPoint(boardRect.rect.center);
                camera.transform.rotation = Quaternion.LookRotation(boardCenter - camera.transform.position, boardRect.up);
                float halfAngle = 0;
                foreach (var corner in corners)
                {
                    Vector3 point = camera.transform.InverseTransformPoint(corner);
                    if (point.z <= 0) throw new InvalidOperationException("Preview board is behind the player.");
                    halfAngle = Mathf.Max(halfAngle,
                        Mathf.Atan(Mathf.Max(Mathf.Abs(point.y), Mathf.Abs(point.x) / camera.aspect) / point.z));
                }
                camera.fieldOfView = Mathf.Clamp(halfAngle * 2 * Mathf.Rad2Deg * 1.08f, 10, 120);
                Render(camera, Output + "/" + name + "-panel.png");
            }
            finally { camera.transform.rotation = playerRotation; camera.fieldOfView = playerFieldOfView; }
        }

        static void RefreshText(Canvas canvas)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var label in canvas.GetComponentsInChildren<TMP_Text>()) label.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
        }

        static void Invoke(object target, string name)
        {
            var method = target.GetType().GetMethod(name, PrivateInstance);
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
            method.Invoke(target, null);
        }

        static void Render(Camera camera, string path)
        {
            const int width = 1600, height = 900;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(pixels);
            }
        }
    }
}
