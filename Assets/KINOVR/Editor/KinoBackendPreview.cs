using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    /// <summary>Offline captures of the actual runtime menus in a disposable scene copy.</summary>
    public static class KinoBackendPreview
    {
        const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        const string Output = "Artifacts/KinoBackend/preview";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Action NoAction = () => { };

        public static void CaptureBatch()
        {
            try { Capture(); EditorApplication.Exit(0); }
            catch (Exception error)
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/validation.txt", "FAIL\n" + error);
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static void CaptureSelectionAndHeaderBatch()
            => CaptureSelectionAndHeaderBatch(false);

        public static void CaptureSelectionHeaderAndTestBatch()
            => CaptureSelectionAndHeaderBatch(true);

        static void CaptureSelectionAndHeaderBatch(bool play)
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            var scenes = EditorSceneManager.GetSceneManagerSetup();
            int exitCode = 0;
            try
            {
                BuildProfile.SetActiveBuildProfile(null);
                KinoFingerTouchTests.Validate();
                KinoGameplaySetup.ApplySecondChanceHeader();
                Capture();
                EditorSceneManager.OpenScene(ScenePath);
                KinoGameplaySetup.CaptureSecondChanceHeader();
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/validation.txt", "FAIL\n" + error);
                Debug.LogException(error);
                exitCode = 1;
            }
            finally
            {
                if (scenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(scenes);
                BuildProfile.SetActiveBuildProfile(profile);
            }
            if (exitCode == 0 && play) KinoBackendTests.RunOfflineExperienceBatch();
            else EditorApplication.Exit(exitCode);
        }

        // Compare the two requested UI changes in one offline run. Restore the active
        // build profile before exiting, without saving any scene or profile asset.
        public static void CapturePolishBatch()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            int exitCode = 0;
            try
            {
                BuildProfile.SetActiveBuildProfile(null);
                Capture();
                KinoReferenceScreenPreview.Capture();
            }
            catch (Exception error) { Debug.LogException(error); exitCode = 1; }
            finally { BuildProfile.SetActiveBuildProfile(profile); }
            EditorApplication.Exit(exitCode);
        }

        [MenuItem("Tools/KINO VR/Backend/Capture offline UI previews")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before capturing backend UI.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before capturing backend UI.");
            Directory.CreateDirectory(Output);
            var previousScenes = EditorSceneManager.GetSceneManagerSetup();
            GameObject cameraObject = null;
            KinoWaveEnvironment previewWaves = null;
            KinoBlackEnclosure previewEnclosure = null;
            var report = new StringBuilder();
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                Require(round && round.experience && round.playerView, "The gameplay scene is missing its experience/player view.");
                var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(round.gameObject);
                if (instance) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                var experience = round.experience;
                previewWaves = experience.waves;
                previewEnclosure = experience.enclosure;
                // Set before Awake: previews never create a backend coordinator, touch device
                // credentials, start requests, or write visitor/session records.
                experience.useLeaderboard = false;
                experience.writeLocalRecords = false;
                Invoke(round.playerView, "Awake");
                Invoke(experience, "Awake");
                experience.State.Go(KinoExperienceStage.PlayerSelection, 0);
                Invoke(experience, "PresentStage");
                if (experience.waves)
                {
                    typeof(KinoWaveEnvironment).GetField("reveal", PrivateInstance).SetValue(experience.waves, 1f);
                    experience.waves.Drive(1, .15f, immediate: true);
                    typeof(KinoWaveEnvironment).GetField("reveal", PrivateInstance).SetValue(experience.waves, 1f);
                    experience.waves.Drive(1, .15f, immediate: true);
                }

                var view = round.playerView.View;
                Require(view, "The preview player view was not initialized.");
                cameraObject = new GameObject("Offline backend UI capture camera");
                var camera = cameraObject.AddComponent<Camera>();
                var source = view.GetComponent<Camera>();
                if (source) camera.CopyFrom(source);
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.aspect = 16f / 9;
                camera.transform.SetPositionAndRotation(view.position, view.rotation);
                float playerFov = camera.fieldOfView;

                var panel = experience.gameObject.AddComponent<KinoBackendPanel>();
                panel.Initialize(experience);
                panel.ShowPairing("DEMO7A2B", "Πληκτρολόγησε τον κωδικό από το admin.", _ => { }, NoAction, NoAction, NoAction);
                CapturePair(camera, panel, "01-pairing", playerFov, report);

                panel.Show("ΕΠΙΛΟΓΗ ΚΑΜΠΑΝΙΑΣ", "Επίλεξε την καμπάνια της εκδήλωσης.", new[]
                {
                    new KinoBackendChoice("KINO VR Experience", NoAction),
                    new KinoBackendChoice("Allwyn", NoAction)
                });
                CapturePair(camera, panel, "06-campaigns", playerFov, report);
                panel.Show("ΕΠΙΛΟΓΗ ΠΕΡΙΟΧΗΣ", "Σε ποια περιοχή βρίσκεσαι;", new[]
                {
                    new KinoBackendChoice("Αττική", NoAction),
                    new KinoBackendChoice("Κεντρική Μακεδονία", NoAction),
                    new KinoBackendChoice("Πίσω στις καμπάνιες", NoAction)
                });
                CapturePair(camera, panel, "07-regions", playerFov, report);
                panel.Show("ΕΠΙΛΟΓΗ ΚΑΤΑΣΤΗΜΑΤΟΣ", "Επίλεξε το κατάστημα της μάσκας.", new[]
                {
                    new KinoBackendChoice("Αθήνα · ATH01", NoAction),
                    new KinoBackendChoice("Πειραιάς · PIR02", NoAction),
                    new KinoBackendChoice("Περιστέρι · PER03 — χωρίς event", NoAction),
                    new KinoBackendChoice("Μαρούσι · MAR04", NoAction),
                    new KinoBackendChoice("Πίσω στις περιοχές", NoAction)
                }, "Allwyn");
                CapturePair(camera, panel, "08-stores-page-1", playerFov, report);
                Invoke(panel, "ChangePage", 1);
                CapturePair(camera, panel, "09-stores-page-2", playerFov, report);
                panel.Show("ΑΛΛΑΓΗ ΚΑΤΑΣΤΗΜΑΤΟΣ", "Για το προσωπικό της εκδήλωσης.", new[]
                {
                    new KinoBackendChoice("Αλλαγή καταστήματος", NoAction),
                    new KinoBackendChoice("Επιστροφή στους παίκτες", NoAction)
                }, "Αθήνα");
                CapturePair(camera, panel, "10-change-store", playerFov, report);
                panel.Show("ΤΟ ΣΚΟΡ ΔΕΝ ΚΑΤΑΧΩΡΙΣΤΗΚΕ", "Ενημέρωσε το προσωπικό. Το αποτέλεσμα κρατήθηκε στη μάσκα.", new[]
                {
                    new KinoBackendChoice("Εντάξει — επόμενος παίκτης", NoAction)
                });
                CapturePair(camera, panel, "11-status-message", playerFov, report);

                var players = new List<KinoBackendChoice>
                {
                    new KinoBackendChoice("Κωνσταντίνος-Αλέξανδρος", NoAction),
                    new KinoBackendChoice("Χριστόφορος_Παπαδόπουλος", NoAction),
                    new KinoBackendChoice("Μαρία_Θεσσαλονίκη", NoAction),
                    new KinoBackendChoice("Νίκος_Πειραιάς", NoAction),
                    new KinoBackendChoice("Αναστασία_Παπαδοπούλου", NoAction),
                    new KinoBackendChoice("Δημήτρης_Περιστέρι", NoAction),
                    new KinoBackendChoice("Γιώργος_Αθήνα", NoAction),
                    new KinoBackendChoice("Ανανέωση λίστας", NoAction, isUtility: true),
                    new KinoBackendChoice("Ρυθμίσεις καταστήματος", NoAction, isUtility: true)
                };
                panel.ShowPlayerSelection("ΕΠΙΛΕΞΕ ΤΟ ΟΝΟΜΑ ΣΟΥ", "7 διαθέσιμοι παίκτες · Allwyn", players);
                CapturePair(camera, panel, "02-players-page-1", playerFov, report);
                ValidateDenseTouchTargets(panel, report);
                Invoke(panel, "ChangePage", 1);
                CapturePair(camera, panel, "03-players-page-2", playerFov, report);
                panel.ShowPlayerSelection("ΕΠΙΛΕΞΕ ΤΟ ΟΝΟΜΑ ΣΟΥ", "Η λίστα ανανεώθηκε · Allwyn", new List<KinoBackendChoice>(players));
                Require((int)typeof(KinoBackendPanel).GetField("page", PrivateInstance).GetValue(panel) == 1,
                    "An unchanged polled list reset the page.");
                Invoke(panel, "ChangePage", 1);
                CapturePair(camera, panel, "12-player-and-store-actions", playerFov, report);

                panel.ShowPlayerSelection("ΕΠΙΛΕΞΕ ΤΟ ΟΝΟΜΑ ΣΟΥ", "Αναμονή εγγραφής από το tablet.", new[]
                {
                    new KinoBackendChoice("Ανανέωση λίστας", NoAction, isUtility: true),
                    new KinoBackendChoice("Ρυθμίσεις καταστήματος", NoAction, isUtility: true)
                },
                    "Το όνομά σου θα εμφανιστεί μόλις ολοκληρωθεί η εγγραφή.");
                CapturePair(camera, panel, "04-waiting-for-tablet", playerFov, report);

                panel.Hide();
                experience.State.Standby(0);
                Invoke(experience, "PresentStage");
                experience.footer.richText = false;
                experience.footer.text = "Παίζεις ως: Κωνσταντίνος-Αλέξανδρος";
                panel.ShowReadyActions(NoAction);
                ValidateReadyAction(panel, experience, report);
                CapturePair(camera, panel, "05-ready-change-player", playerFov, report, true);

                panel.Hide();
                Require(!panel.IsVisible, "Hide left the backend panel visible.");
                foreach (var button in PanelCanvas(panel).GetComponentsInChildren<KinoBackendTouchButton>(true))
                    Require(!button.gameObject.activeInHierarchy, "Hide left an active backend touch target.");
                report.Insert(0, "PASS: offline runtime UI captured using the existing scene fonts and player-camera pose.\n" +
                    "Synthetic nicknames/code only; no API requests, credential access, session writes, or saved scene/prefab edits.\n" +
                    "The player-view captures retain the desktop camera FOV; panel captures turn from the same eye position to fit the controls.\n");
                File.WriteAllText(Output + "/validation.txt", report.ToString());
                Debug.Log("KINO_BACKEND_PREVIEW_PASS");
            }
            finally
            {
                // Awake is invoked manually in edit mode; turn off the generated,
                // non-saved renderers before another capture opens its fixture.
                if (previewWaves) previewWaves.HideImmediately();
                if (previewEnclosure)
                {
                    previewEnclosure.SetBackground(0);
                    previewEnclosure.SetRoundBackground(0);
                    previewEnclosure.SetSessionFade(0);
                    previewEnclosure.SetRoundFade(0);
                }
                if (cameraObject) Object.DestroyImmediate(cameraObject);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (previousScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            }
        }

        static void ValidateDenseTouchTargets(KinoBackendPanel panel, StringBuilder report)
        {
            var canvas = PanelCanvas(panel);
            var first = canvas.transform.Find("Choice 1").GetComponent<BoxCollider>();
            var second = canvas.transform.Find("Choice 2").GetComponent<BoxCollider>();
            var fixture = KinoFingerTouchTests.CreateFinger("Synthetic index fingertip");
            var wrist = new GameObject("Synthetic 10 cm wrist collider");
            try
            {
                var sphere = fixture.GetComponent<SphereCollider>();
                var wristSphere = wrist.AddComponent<SphereCollider>();
                wristSphere.radius = .1f;
                wristSphere.isTrigger = true;
                var touch = typeof(KinoBackendTouchButton).GetMethod("IsTouching", BindingFlags.Static | BindingFlags.NonPublic);
                fixture.transform.position = first.transform.position;
                wrist.transform.position = first.transform.position;
                Require(!(bool)touch.Invoke(null, new object[] { first, wristSphere, 0f }), "The wrist can still select a row.");
                Require((bool)touch.Invoke(null, new object[] { first, sphere, 0f }), "The index fingertip did not hit its own row.");
                Require(!(bool)touch.Invoke(null, new object[] { second, sphere, 0f }), "The fingertip hit a neighboring row.");
                first.gameObject.SetActive(false);
                Require((bool)touch.Invoke(null, new object[] { first, sphere, .012f }), "Hiding a row discarded the held-contact latch volume.");
                first.gameObject.SetActive(true);
            }
            finally { Object.DestroyImmediate(fixture); Object.DestroyImmediate(wrist); }
            report.AppendLine("PASS: dense rows accept only the index fingertip, reject wrist and neighboring-row overlap, and retain hidden-row latch geometry.");
        }

        static void ValidateReadyAction(KinoBackendPanel panel, KinoExperienceController experience, StringBuilder report)
        {
            var canvas = PanelCanvas(panel);
            var labels = canvas.GetComponentsInChildren<TMP_Text>();
            Require(labels.Length == 1 && labels[0].text == "Αλλαγή παίκτη", "Ready left full-panel text visible.");
            Require(canvas.GetComponentsInChildren<KinoBackendTouchButton>().Length == 1, "Ready left additional backend controls active.");
            var correction = canvas.GetComponentInChildren<KinoBackendTouchButton>();
            foreach (var graphic in canvas.GetComponentsInChildren<Graphic>())
                Require(graphic.transform.IsChildOf(correction.transform), "Ready left graphics outside its correction button visible.");
            var delta = canvas.transform.position - experience.modeCanvas.transform.position;
            var desktop = experience.round.playerView.desktopCamera;
            if (desktop && desktop.isActiveAndEnabled)
            {
                desktop.enabled = false;
                panel.Hide(); panel.ShowReadyActions(NoAction);
                delta = canvas.transform.position - experience.modeCanvas.transform.position;
                Require(Vector3.Distance(delta, Vector3.down * .13f) < .0001f, "Headset-layout correction is not 13 cm below Start.");
                desktop.enabled = true;
                panel.Hide(); panel.ShowReadyActions(NoAction);
            }
            else Require(Vector3.Distance(delta, Vector3.down * .13f) < .0001f, "Ready correction is not 13 cm below Start.");
            var original = canvas.transform.position;
            panel.ShowReadyActions(NoAction);
            Require(Vector3.Distance(original, canvas.transform.position) < .0001f, "Repeated Ready action changed its world anchor.");
            report.AppendLine("PASS: Ready correction has one button, no full-panel background, a stationary desktop anchor, and the headset layout remains 13 cm below Start.");
        }

        static void CapturePair(Camera camera, KinoBackendPanel panel, string name, float playerFov, StringBuilder report, bool ready = false)
        {
            var canvas = PanelCanvas(panel);
            RefreshAndValidate(canvas);
            if (ready)
            {
                var experience = panel.GetComponent<KinoExperienceController>();
                RefreshAndValidate(experience.contentCanvas, false);
                RefreshAndValidate(experience.modeCanvas, false);
            }
            var eyeRotation = camera.transform.rotation;
            camera.fieldOfView = playerFov;
            camera.aspect = 16f / 9;
            foreach (var button in canvas.GetComponentsInChildren<KinoBackendTouchButton>())
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var viewport = camera.WorldToViewportPoint(corner);
                    Require(viewport.z > 0 && viewport.x >= .005f && viewport.x <= .995f && viewport.y >= .005f && viewport.y <= .995f,
                        "Desktop control is outside the viewport: " + button.name);
                }
            }
            Render(camera, Output + "/" + name + "-player-view.png", 1600, 900);
            try
            {
                // Fit the actual menu from the same eye position, preserving perspective.
                var target = canvas.transform.position;
                camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
                camera.aspect = 1;
                if (ready)
                {
                    camera.fieldOfView = 70;
                }
                else
                {
                    var corners = new Vector3[4];
                    ((RectTransform)canvas.transform).GetWorldCorners(corners);
                    float halfAngle = 0;
                    foreach (var corner in corners)
                    {
                        var point = camera.transform.InverseTransformPoint(corner);
                        Require(point.z > 0, "A backend panel corner is behind the eye.");
                        halfAngle = Mathf.Max(halfAngle, Mathf.Atan(Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.y)) / point.z));
                    }
                    camera.fieldOfView = halfAngle * 2 * Mathf.Rad2Deg * 1.08f;
                }
                Render(camera, Output + "/" + name + "-panel.png", 1200, 1200);
            }
            finally { camera.transform.rotation = eyeRotation; camera.fieldOfView = playerFov; camera.aspect = 16f / 9; }
            report.AppendLine("PASS: " + name + " active backend labels fit without overflow, rich text is disabled, and all button corners fit the desktop viewport.");
        }

        static void RefreshAndValidate(Canvas canvas, bool backend = true)
        {
            if (!canvas || !canvas.gameObject.activeInHierarchy) return;
            Canvas.ForceUpdateCanvases();
            foreach (var label in canvas.GetComponentsInChildren<TMP_Text>()) label.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            foreach (var label in canvas.GetComponentsInChildren<TMP_Text>())
            {
                Require(!label.isTextOverflowing, "Text overflow in " + label.transform.parent.name + "/" + label.name);
                if (backend) Require(!label.richText, "A backend text label enables rich text: " + label.name);
            }
        }

        static void Render(Camera camera, string path, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                // The second shot changes projection and target size without an Editor
                // frame between renders. Rebuild graphics so logo batches are current.
                foreach (var graphic in Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None))
                    graphic.SetAllDirty();
                Canvas.ForceUpdateCanvases();
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

        static void Invoke(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, PrivateInstance);
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
            method.Invoke(target, arguments);
        }

        static Canvas PanelCanvas(KinoBackendPanel panel) =>
            (Canvas)typeof(KinoBackendPanel).GetField("canvas", PrivateInstance).GetValue(panel);

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
