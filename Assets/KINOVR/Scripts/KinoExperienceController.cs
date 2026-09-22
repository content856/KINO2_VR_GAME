using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR;

namespace KinoVR
{
    [Serializable] public sealed class KinoSessionEvent : UnityEvent<string> { }
    [Serializable] public sealed class KinoSessionRecord
    {
        public string sessionId, startedUtc, safetyVersion, safetyDisplayedUtc, safetyElapsedUtc;
        public string externalConfirmationUtc, completedUtc, closedUtc, abortedUtc;
        public bool placeholderSafety;
        public int score, normalCatches, secondChanceCatches;
        public bool includeBoost;
        public int boostCatches;
    }

    [DefaultExecutionOrder(-300), DisallowMultipleComponent]
    public sealed class KinoExperienceController : MonoBehaviour
    {
        public KinoRoundController round;
        [Header("Session timing (seconds)")]
        [Min(.1f)] public float startupSeconds = 1.2f;
        [Min(1)] public float safetySeconds = 12;
        [Tooltip("Duration of EACH logo, including its fade in/out.")]
        [Min(1)] public float brandingSeconds = 3;
        [Min(.1f)] public float logoBlackSeconds = .5f;
        [Min(.1f)] public float logoFadeSeconds = .65f;
        public float BrandingDuration => 2 * brandingSeconds + logoBlackSeconds;
        [Min(1)] public float introductionSeconds = 6;
        [Min(1)] public float finaleSeconds = 10;
        [Tooltip("Time from showing the removal instruction until the next visitor's mode selection.")]
        [Min(2)] public float closingSeconds = 5;
        [Header("Replace this draft before production")]
        public string safetyVersion = "placeholder-v1";
        public bool placeholderSafety = true;
        [TextArea(5, 12)] public string safetyText = "Παίξε καθιστός και παρέμεινε στη θέση σου.\nΒεβαιώσου ότι το headset εφαρμόζει άνετα και ο χώρος γύρω από τα χέρια σου είναι ελεύθερος.\nΑν νιώσεις ζάλη, ναυτία ή δυσφορία, σταμάτησε και ζήτησε βοήθεια από το προσωπικό.\nΗ εμπειρία είναι προσομοίωση ψυχαγωγίας.";
        [Tooltip("When enabled, the timed safety screen also waits for ConfirmSafetyFromOperator().")]
        public bool requireExternalSafetyConfirmation;
        [Header("Local handoff; no tablet or leaderboard service is connected")]
        public bool writeLocalRecords = true;
        public KinoSessionEvent onSafetyDisplayed = new KinoSessionEvent();
        public KinoSessionEvent onResultReady = new KinoSessionEvent();
        public UnityEvent onSessionClosed = new UnityEvent();
        [Header("Presentation")]
        public Canvas contentCanvas;
        public CanvasGroup content;
        public TMP_Text title, body, footer;
        public RawImage allwynLogo, kinoLogo;
        public KinoBlackEnclosure enclosure;
        public float BlackoutAlpha => enclosure ? enclosure.FadeAlpha : 0;
        [Header("Ending background transparency (%)")]
        [Tooltip("0 = solid black, 100 = fully transparent. Text stays opaque.")]
        [Range(0, 100)] public float finaleBackgroundTransparency = 85;
        [Range(0, 100)] public float closingBackgroundTransparency = 45;
        [Header("VR layout (metres)")]
        [Range(1.2f, 3)] public float contentDistance = 2.5f;
        [Range(1.2f, 3)] public float contentWidth = 2.2f;
        public Canvas modeCanvas;
        public KinoExperienceModeButton normalModeButton, boostModeButton;
        [Tooltip("Direct-touch controls, anchored once in front of the seated player.")]
        public Vector3 modeViewOffset = new Vector3(0, -.18f, .46f);
        public KinoExperienceStage Stage => State.Stage;
        public KinoExperienceState State { get; } = new KinoExperienceState();
        public KinoSessionRecord Record { get; private set; }
        public string LastStorageError { get; private set; }
        bool initialized, mounted, awaitingTrackedView;
        OVRCameraRig trackedRig;
        KinoModeHands modeHands;
        int lastAnchorFrame = -1;
        float audioMix = 1;
        double clock;
        static string Utc => DateTime.UtcNow.ToString("O");

        void Awake()
        {
            if (!round) round = GetComponent<KinoRoundController>();
            if (!round) { enabled = false; return; }
            round.experience = this;
            if (round.playerView && round.playerView.vrRig) trackedRig = round.playerView.vrRig.GetComponent<OVRCameraRig>();
            modeHands = new KinoModeHands(round.playerView && round.playerView.vrRig ? round.playerView.vrRig.transform : null);
            round.startAutomatically = false;
            round.showcaseBoostAfterSecondChance = false;
            round.onRoundFinished.AddListener(FinishSessionRound);
            SetBlackout(1);
            if (content) content.alpha = 0;
        }
        void OnEnable()
        {
            OVRManager.HMDMounted += Mounted;
            OVRManager.HMDUnmounted += Unmounted;
            if (trackedRig) trackedRig.UpdatedAnchors += AnchorsUpdated;
            if (initialized)
            {
                State.Reset(clock);
                if (mounted) awaitingTrackedView = true;
            }
        }
        void Start()
        {
            initialized = true;
            mounted = Application.isEditor && !XRSettings.isDeviceActive || OVRManager.instance && OVRManager.instance.isUserPresent;
            if (mounted) awaitingTrackedView = true;
        }
        void Mounted() { mounted = true; if (initialized && Stage == KinoExperienceStage.Waiting) awaitingTrackedView = true; }
        void AnchorsUpdated(OVRCameraRig rig) => lastAnchorFrame = Time.frameCount;
        void Unmounted()
        {
            mounted = false;
            awaitingTrackedView = false;
            if (!initialized) return;
            if (Record != null && string.IsNullOrEmpty(Record.completedUtc) && Stage != KinoExperienceStage.ModeSelection) { Record.abortedUtc = Utc; SaveRecord(); }
            StopRound();
            State.Reset(clock);
            if (content) content.alpha = 0;
            if (modeCanvas) modeCanvas.gameObject.SetActive(false);
            modeHands?.SetVisible(false);
            if (enclosure) enclosure.SetBackground(0);
            SetBlackout(1);
            SetAudio(0);
        }
        [ContextMenu("Show Normal / Boost selection")]
        public void ShowModeSelection()
        {
            if (!isActiveAndEnabled || !round || (Stage != KinoExperienceStage.Waiting && Stage != KinoExperienceStage.Complete)) return;
            mounted = true;
            awaitingTrackedView = false;
            StopRound();
            round.showcaseBoostAfterSecondChance = false;
            if (round.score) round.score.ResetScore();
            if (round.board) round.board.ResetBoard();
            State.SelectMode(clock);
            PresentStage();
        }
        [ContextMenu("Begin Normal session")]
        public void BeginNormalSession() => BeginSelectedSession(false);
        [ContextMenu("Begin session with Boost")]
        public void BeginBoostSession() => BeginSelectedSession(true);
        public void BeginSession() => BeginNormalSession();
        void BeginSelectedSession(bool withBoost)
        {
            if (!isActiveAndEnabled || !round || (Stage != KinoExperienceStage.Waiting && Stage != KinoExperienceStage.Complete && Stage != KinoExperienceStage.ModeSelection)) return;
            mounted = true;
            awaitingTrackedView = false;
            StopRound();
            round.showcaseBoostAfterSecondChance = withBoost;
            if (round.score) round.score.ResetScore();
            if (round.board) round.board.ResetBoard();
            Record = new KinoSessionRecord { sessionId = Guid.NewGuid().ToString("N"), startedUtc = Utc,
                safetyVersion = safetyVersion, placeholderSafety = placeholderSafety, includeBoost = withBoost };
            if (round.audioController) round.audioController.BeginSessionAudio();
            State.Begin(clock, withBoost);
            PresentStage();
        }
        [ContextMenu("Confirm safety from operator")]
        public void ConfirmSafetyFromOperator()
        {
            if (!isActiveAndEnabled || Record == null || !State.ConfirmSafety()) return;
            Record.externalConfirmationUtc = Utc;
            SaveRecord();
        }
        void Update()
        {
            if (!initialized || !mounted) return;
            if (awaitingTrackedView)
            {
                // Placement happens after the rig has written its anchors, in LateUpdate.
                return;
            }
            // No fast-forward after app suspension; each screen remains observable.
            clock += Math.Min(Time.unscaledDeltaTime, .1f);
            // Complete is observable for one frame so receipts and completion events finish
            // exactly once. A new visitor can then choose a mode without a headset cycle.
            if (Stage == KinoExperienceStage.Complete) { ShowModeSelection(); return; }
            if (Stage == KinoExperienceStage.Gameplay && round.State.Phase >= KinoRoundPhase.BoardHold &&
                round.State.Phase <= KinoRoundPhase.SecondChance && State.BeginSecondChance(clock)) PresentStage();
            if (Stage == KinoExperienceStage.SecondChance && (round.State.Phase == KinoRoundPhase.Boost ||
                round.State.Phase == KinoRoundPhase.BoostSettling) && State.BeginBoost(clock)) PresentStage();
            var previous = Stage;
            if (State.Advance(clock, startupSeconds, safetySeconds, BrandingDuration, introductionSeconds,
                finaleSeconds, closingSeconds, requireExternalSafetyConfirmation))
            {
                if (previous == KinoExperienceStage.Safety) { Record.safetyElapsedUtc = Utc; SaveRecord(); }
                PresentStage();
            }
            AnimateStage();
        }
        void FinishSessionRound()
        {
            if (!isActiveAndEnabled || !State.Finish(clock)) return;
            Record.score = round.State.Score;
            Record.normalCatches = round.State.NormalCatchCount;
            Record.secondChanceCatches = round.State.SecondChanceCatchCount;
            Record.boostCatches = round.State.BoostCatchCount;
            Record.completedUtc = Utc;
            SaveRecord();
            PresentStage();
            onResultReady.Invoke(JsonUtility.ToJson(Record));
        }
        void PresentStage()
        {
            if (!content) return;
            bool overlay = Stage == KinoExperienceStage.ModeSelection || Stage == KinoExperienceStage.Safety || Stage == KinoExperienceStage.Branding ||
                Stage == KinoExperienceStage.Introduction || Stage == KinoExperienceStage.Finale || Stage == KinoExperienceStage.Closing;
            content.alpha = overlay ? 1 : 0;
            ApplyBackground();
            PositionContent();
            if (modeCanvas)
            {
                modeCanvas.gameObject.SetActive(Stage == KinoExperienceStage.ModeSelection);
                if (Stage == KinoExperienceStage.ModeSelection) { PositionModeSelection(); normalModeButton.Show(); boostModeButton.Show(); }
                else { normalModeButton.Hide(); boostModeButton.Hide(); }
            }
            allwynLogo.gameObject.SetActive(false);
            kinoLogo.gameObject.SetActive(Stage == KinoExperienceStage.Finale);
            allwynLogo.color = kinoLogo.color = Color.white;
            title.color = Color.white;
            body.color = Color.white;
            allwynLogo.rectTransform.anchoredPosition = Vector2.zero;
            allwynLogo.rectTransform.sizeDelta = new Vector2(600, 600f * allwynLogo.texture.height / allwynLogo.texture.width);
            bool finale = Stage == KinoExperienceStage.Finale;
            kinoLogo.rectTransform.anchoredPosition = new Vector2(0, finale ? 235 : 0);
            float logoWidth = finale ? 160 : 540;
            kinoLogo.rectTransform.sizeDelta = new Vector2(logoWidth, logoWidth * kinoLogo.texture.height / kinoLogo.texture.width);
            title.rectTransform.anchoredPosition = new Vector2(0, finale ? 110 : 225);
            body.rectTransform.anchoredPosition = new Vector2(0, finale ? -65 : -5);
            body.rectTransform.sizeDelta = new Vector2(1040, finale ? 230 : 330);
            title.text = body.text = footer.text = "";
            switch (Stage)
            {
                case KinoExperienceStage.ModeSelection:
                    SetBlackout(0); SetAudio(0);
                    title.text = "ΕΠΙΛΕΞΕ ΕΜΠΕΙΡΙΑ";
                    body.text = "Άγγιξε μία επιλογή για να ξεκινήσεις.";
                    break;
                case KinoExperienceStage.Startup: SetBlackout(1); SetAudio(0); break;
                case KinoExperienceStage.Safety:
                    title.text = "ΠΡΙΝ ΞΕΚΙΝΗΣΟΥΜΕ"; body.text = safetyText;
                    footer.text = placeholderSafety ? "ΠΡΟΣΩΡΙΝΟ ΚΕΙΜΕΝΟ • ΠΡΟΣ ΑΝΤΙΚΑΤΑΣΤΑΣΗ" : "";
                    Record.safetyDisplayedUtc = Utc; SaveRecord(); onSafetyDisplayed.Invoke(JsonUtility.ToJson(Record)); break;
                case KinoExperienceStage.Branding: AnimateBranding(0); break;
                case KinoExperienceStage.Introduction:
                    title.text = "ΚΑΛΩΣ ΗΡΘΕΣ ΣΤΟ KINO VR";
                    body.text = "Πιάσε τις μπάλες με τα χέρια σου.\nΚάθε πιάσιμο μετράει!";
                    footer.text = "Μείνε καθιστός και κράτα τα χέρια σου ελεύθερα."; break;
                case KinoExperienceStage.Gameplay:
                    SetAudio(1); round.BeginRound(Mathf.Clamp(round.roundDuration, 60, 90)); break;
                case KinoExperienceStage.Finale:
                    title.text = "ΜΠΡΑΒΟ!"; body.text = "ΤΕΛΙΚΟ ΣΚΟΡ\n<size=100><color=#FFD42A>" + Record.score + "</color></size>";
                    footer.text = "Ευχαριστούμε που έπαιξες!"; break;
                case KinoExperienceStage.Closing:
                    title.text = "Η ΕΜΠΕΙΡΙΑ ΟΛΟΚΛΗΡΩΘΗΚΕ";
                    body.text = "Παρέμεινε καθιστός.\nΒγάλε ήρεμα το headset\nμε τη βοήθεια του προσωπικού."; break;
                case KinoExperienceStage.Complete:
                    SetBlackout(1); SetAudio(0); if (round.audioController) round.audioController.StopAll();
                    Record.closedUtc = Utc; SaveRecord(); onSessionClosed.Invoke(); break;
            }
        }
        void AnimateStage()
        {
            ApplyBackground();
            float elapsed = (float)(clock - State.EnteredAt);
            if (Stage == KinoExperienceStage.Safety) SetBlackout(1 - Mathf.SmoothStep(0, 1, elapsed / .8f));
            else if (Stage != KinoExperienceStage.Startup && Stage != KinoExperienceStage.Complete) SetBlackout(0);
            if (Stage == KinoExperienceStage.Safety && !placeholderSafety)
                footer.text = requireExternalSafetyConfirmation && !State.ExternalConfirmation ? "Περιμένουμε επιβεβαίωση από το προσωπικό." : "Η εμπειρία ξεκινά σε λίγο.";
            if (Stage == KinoExperienceStage.Branding)
                AnimateBranding(elapsed);
            if (Stage == KinoExperienceStage.ModeSelection)
                SetBlackout(1 - Mathf.SmoothStep(0, 1, elapsed / .4f));
            if (Stage == KinoExperienceStage.Gameplay)
                SetBlackout(1 - Mathf.SmoothStep(0, 1, elapsed / .8f));
            if (Stage == KinoExperienceStage.Introduction)
            {
                SetBlackout(1 - Mathf.SmoothStep(0, 1, elapsed / 1.2f));
                content.alpha = Mathf.Min(Mathf.Clamp01(elapsed / .8f), Mathf.Clamp01((introductionSeconds - elapsed) / .8f));
                // Reading stays on an opaque 360-degree background. The last fade
                // conceals the switch to the room before the first gameplay ball.
                SetBlackout(Mathf.Max(BlackoutAlpha, Mathf.SmoothStep(0, 1, (elapsed - introductionSeconds + .5f) / .5f)));
                SetAudio(Mathf.SmoothStep(0, 1, elapsed / 2));
            }
            if (Stage == KinoExperienceStage.Finale)
            {
                content.alpha = Mathf.Clamp01(elapsed / .8f);
                float pulse = 1 + .025f * Mathf.Sin(elapsed * 2);
                kinoLogo.transform.localScale = Vector3.one * pulse;
            }
            else kinoLogo.transform.localScale = Vector3.one;
            if (Stage == KinoExperienceStage.Closing)
            {
                float fade = Mathf.SmoothStep(0, 1, (elapsed - closingSeconds + .5f) / .5f);
                // Hold the requested transparency for the whole removal instruction.
                // Full black belongs to Complete, after the five-second message.
                SetAudio(1 - fade);
            }
        }
        void ApplyBackground()
        {
            if (!enclosure) return;
            bool instructions = Stage == KinoExperienceStage.ModeSelection || Stage == KinoExperienceStage.Safety ||
                Stage == KinoExperienceStage.Branding || Stage == KinoExperienceStage.Introduction;
            float alpha = Stage == KinoExperienceStage.Finale ? 1 - Mathf.Clamp01(finaleBackgroundTransparency / 100) :
                Stage == KinoExperienceStage.Closing ? 1 - Mathf.Clamp01(closingBackgroundTransparency / 100) : instructions ? 1 : 0;
            enclosure.SetBackground(alpha);
        }
        void PositionContent()
        {
            var view = round.playerView ? round.playerView.View : null;
            if (!view || !contentCanvas) return;
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            var heading = Quaternion.LookRotation(forward.normalized);
            contentCanvas.transform.SetPositionAndRotation(view.position + heading * new Vector3(0, 0, contentDistance), heading);
            contentCanvas.transform.localScale = Vector3.one * (contentWidth / 1100);
            contentCanvas.worldCamera = view.GetComponent<Camera>();
        }
        void PositionModeSelection()
        {
            var view = round.playerView ? round.playerView.View : null;
            if (!view || !modeCanvas) return;
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            var heading = Quaternion.LookRotation(forward.normalized);
            var position = view.position + heading * modeViewOffset;
            modeCanvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - view.position, Vector3.up));
            modeCanvas.worldCamera = view.GetComponent<Camera>();
        }
        void AnimateBranding(float elapsed)
        {
            // Fade only the logo: the background stays fully black between both marks.
            content.alpha = 1;
            float secondStart = brandingSeconds + logoBlackSeconds;
            bool first = elapsed < brandingSeconds;
            bool second = elapsed >= secondStart;
            allwynLogo.gameObject.SetActive(first);
            kinoLogo.gameObject.SetActive(second);
            float local = second ? elapsed - secondStart : elapsed;
            float fade = Mathf.Min(logoFadeSeconds, brandingSeconds * .45f);
            float alpha = Mathf.Min(Mathf.Clamp01(local / fade), Mathf.Clamp01((brandingSeconds - local) / fade));
            allwynLogo.color = new Color(1, 1, 1, first ? alpha : 0);
            kinoLogo.color = new Color(1, 1, 1, second ? alpha : 0);
        }
        void LateUpdate()
        {
            if (awaitingTrackedView && initialized && mounted)
            {
                var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                bool desktop = Application.isEditor && !XRSettings.isDeviceActive;
                if (desktop || lastAnchorFrame == Time.frameCount &&
                    device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
                    ShowModeSelection();
            }
            modeHands?.SetVisible(enclosure && enclosure.BackgroundAlpha > 0 && mounted);
        }
        void SetBlackout(float alpha)
        {
            if (enclosure) enclosure.SetSessionFade(alpha);
        }
        void SetAudio(float mix)
        {
            audioMix = mix;
            if (round && round.audioController) round.audioController.sessionVolume = audioMix;
        }
        void StopRound()
        {
            if (!round) return;
            round.State.Stop();
            if (round.launcher) round.launcher.StopLaunching(true);
            if (round.restartButton) round.restartButton.Hide();
            if (round.secondChancePresentation) round.secondChancePresentation.ResetPresentation();
            if (round.boostPresentation) round.boostPresentation.SetBoost(false);
        }
        void SaveRecord()
        {
            if (!writeLocalRecords || Record == null) return;
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "KinoSessions");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, Record.sessionId + ".json"), JsonUtility.ToJson(Record, true));
                LastStorageError = null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { LastStorageError = error.Message; Debug.LogError("KINO session record could not be saved: " + error.Message, this); }
        }
        void OnDisable()
        {
            OVRManager.HMDMounted -= Mounted; OVRManager.HMDUnmounted -= Unmounted;
            if (trackedRig) trackedRig.UpdatedAnchors -= AnchorsUpdated;
            if (Record != null && string.IsNullOrEmpty(Record.completedUtc) && Stage != KinoExperienceStage.Waiting && Stage != KinoExperienceStage.ModeSelection)
            { Record.abortedUtc = Utc; SaveRecord(); }
            StopRound(); SetAudio(1);
            modeHands?.SetVisible(false);
            if (content) content.alpha = 0;
            if (modeCanvas) modeCanvas.gameObject.SetActive(false);
            if (enclosure) enclosure.SetBackground(0);
            SetBlackout(0);
        }
        void OnDestroy() { modeHands?.Dispose(); if (round) round.onRoundFinished.RemoveListener(FinishSessionRound); }
    }
}
