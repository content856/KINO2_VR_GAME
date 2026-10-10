using System;
using System.Collections.Generic;
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
        public int glowCatches, mysteryCatches, mainScore;
    }

    [DefaultExecutionOrder(-300), DisallowMultipleComponent]
    public sealed class KinoExperienceController : MonoBehaviour
    {
        public KinoRoundController round;
        [Header("Session timing (seconds)")]
        [Min(.1f)] public float startupSeconds = 1.2f;
        [Min(1)] public float safetySeconds = 12;
        [Tooltip("Duration of the Allwyn introduction, including its fade in/out.")]
        [Min(1)] public float brandingSeconds = 3;
        [Min(0)] public float logoBlackSeconds = .5f;
        [Min(.1f)] public float logoFadeSeconds = .65f;
        public float BrandingDuration => brandingSeconds * 2 + logoBlackSeconds;
        [Min(1)] public float introductionSeconds = 6;
        [Tooltip("KINO logo after the welcome: the lines collapse into a beam while the background fades to reveal the room.")]
        [Min(1)] public float kinoSplashSeconds = 3;
        [Tooltip("Seconds in the revealed room before the first ball launches.")]
        [Min(0)] public float roundStartDelay = 2;
        [Min(1)] public float finaleSeconds = 10;
        [Tooltip("Time from showing the removal instruction until standby, retaining the selected mode.")]
        [Min(2)] public float closingSeconds = 5;
        [Header("Replace this draft before production")]
        public const string DefaultSafetyText =
            "Παίζεις καθιστός. Μείνε στη θέση σου μέχρι το τέλος της εμπειρίας.\n" +
            "Βεβαιώσου ότι το headset εφαρμόζει άνετα και ότι ο χώρος γύρω από τα χέρια σου είναι ελεύθερος.\n" +
            "Αν νιώσεις ζάλη, ναυτία ή οποιαδήποτε δυσφορία, σταμάτα και ζήτησε βοήθεια από το προσωπικό.\n" +
            "Η εμπειρία περιέχει γρήγορες κινήσεις και φωτεινά εφέ και δεν συνιστάται σε άτομα με επιληψία ή ευαισθησία στο φως.";
        public string safetyVersion = "v1";
        [Tooltip("Shows a 'temporary text' footer on the safety screen. Off for the approved text.")]
        public bool placeholderSafety;
        [TextArea(5, 12)] public string safetyText = DefaultSafetyText;
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
        public Material goldScoreMaterial;
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
        public KinoExperienceModeButton startButton;
        [Tooltip("Hands-only app: hide the Meta Touch controller models (they still appear over Quest Link in the Editor).")]
        public bool hideControllerModels = true;
        [Header("Touch controls (Meta hands guidance: 42 to 46 cm from the user for direct touch)")]
        [Tooltip("Straight-line distance from the eyes to the ΞΕΚΙΝΑ button.")]
        [Range(.42f, .46f)] public float touchDistance = .46f;
        [Tooltip("How far below eye height the button sits (metres).")]
        [Range(0, .3f)] public float touchDrop = .14f;
        [HideInInspector] public Vector3 modeViewOffset = new Vector3(0, -.18f, .46f);
        public Vector3 TouchOffset => new Vector3(0, -touchDrop, Mathf.Sqrt(Mathf.Max(0, touchDistance * touchDistance - touchDrop * touchDrop)));
        [Header("Pre-game blue environment")]
        [Tooltip("Animated wave lines, particles and navy sky behind every pre-game screen.")]
        public KinoWaveEnvironment waves;
        [Tooltip("Operator NORMAL / ΜΕ BOOST menu before the ready screen. Off: no buttons, every visitor gets includeBoost.")]
        public bool offerBoostSelection;
        [Header("Sequence (Tools > KINO VR > Experience Sequence)")]
        [Tooltip("Screens before the game. Steps above the Ready screen play once when the app starts; steps below it play for every visitor.")]
        public List<KinoSequenceEntry> preGame = KinoSequence.DefaultPreGame();
        [Tooltip("Screens after the game, before the next visitor.")]
        public List<KinoSequenceEntry> postGame = KinoSequence.DefaultPostGame();
        [Tooltip("Inactive at launch. When activated, the KINO Boost wave runs after Second Chance for every visitor, without a choice or buttons.")]
        public bool includeBoost;
        [Tooltip("After the score, the level fades out and the remove-headset message appears on the blue environment.")]
        public bool outroOnEnvironment = true;
        public RawImage brandLogo, vrExperienceLogo;
        public KinoGlassPanel safetyPanel;
        [Tooltip("Fill bar for the minimum safety reading time; its parent is the track.")]
        public RectTransform safetyProgress;
        [Tooltip("CF Asty brand fonts for the pre-game screens. The final score keeps its gold lettering font.")]
        public TMP_FontAsset headingFont, textFont;
        public KinoExperienceStage Stage => State.Stage;
        public KinoExperienceState State { get; } = new KinoExperienceState();
        public KinoSessionRecord Record { get; private set; }
        public string LastStorageError { get; private set; }
        bool initialized, mounted, awaitingTrackedView, appEntryDone;
        int sequenceIndex = -1, postIndex = -1;
        bool roundPending;
        OVRCameraRig trackedRig;
        KinoModeHands modeHands;
        KinoFinalePanel finalePanel;
        Material bodyMaterial, titleMaterial, footerMaterial;
        TMP_FontAsset defaultTitleFont, defaultBodyFont, defaultFooterFont;
        bool UseWaves => waves && waves.isActiveAndEnabled;
        bool Outro => outroOnEnvironment && UseWaves;
        bool SessionOpen => Record != null && string.IsNullOrEmpty(Record.completedUtc) && string.IsNullOrEmpty(Record.abortedUtc);
        /// <summary>Index of the enabled Ready step, or -1 when visitors start automatically.</summary>
        public int ReadyIndex
        {
            get
            {
                if (preGame == null) return -1;
                for (int i = 0; i < preGame.Count; i++)
                    if (preGame[i] != null && preGame[i].enabled && preGame[i].step == KinoSequenceStep.Ready) return i;
                return -1;
            }
        }
        int VisitorStart => Mathf.Max(0, ReadyIndex);
        Transform contentParent;
        int lastAnchorFrame = -1;
        float audioMix = 1;
        double clock;
        static string Utc => DateTime.UtcNow.ToString("O");
        public const string ReadyTouchLine = "Κάθισε άνετα.\nΆγγιξε το ΞΕΚΙΝΑ όταν είσαι έτοιμος.";
        public const string ReadyHandsLine = "Κάθισε άνετα.\nΣήκωσε τα χέρια σου μπροστά σου.";

        void Awake()
        {
            if (body) { bodyMaterial = body.fontSharedMaterial; defaultBodyFont = body.font; }
            if (title) { titleMaterial = title.fontSharedMaterial; defaultTitleFont = title.font; }
            if (footer) { footerMaterial = footer.fontSharedMaterial; defaultFooterFont = footer.font; }
            if (!round) round = GetComponent<KinoRoundController>();
            if (!round) { enabled = false; return; }
            round.experience = this;
            if (round.playerView && round.playerView.vrRig) trackedRig = round.playerView.vrRig.GetComponent<OVRCameraRig>();
            modeHands = new KinoModeHands(round.playerView && round.playerView.vrRig ? round.playerView.vrRig.transform : null);
            round.startAutomatically = false;
            round.showcaseBoostAfterSecondChance = false;
            round.onRoundFinished.AddListener(FinishSessionRound);
            if (contentCanvas)
            {
                contentParent = contentCanvas.transform.parent;
                var panel = new GameObject("Final score panel", typeof(RectTransform), typeof(KinoFinalePanel));
                panel.transform.SetParent(contentCanvas.transform, false);
                panel.transform.SetAsFirstSibling();
                finalePanel = panel.GetComponent<KinoFinalePanel>();
                finalePanel.rectTransform.sizeDelta = KinoBoardOverlay.ArtworkSize;
                finalePanel.raycastTarget = false;
                var caption = new GameObject("Final score caption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                caption.transform.SetParent(panel.transform, false);
                caption.rectTransform.anchoredPosition = new Vector2(0, 105);
                caption.rectTransform.sizeDelta = new Vector2(440, 50);
                caption.font = body.font;
                caption.fontSharedMaterial = body.fontSharedMaterial;
                caption.fontSize = 31;
                caption.characterSpacing = 4;
                caption.alignment = TextAlignmentOptions.Center;
                caption.color = KinoScreenTypography.Ivory;
                caption.raycastTarget = false;
                caption.text = "ΤΕΛΙΚΟ ΣΚΟΡ";
                panel.SetActive(false);
            }
            HideLegacyButtons();
            SetBlackout(1);
            SetAudio(0);
            if (content) content.alpha = 0;
        }
        // Old menu buttons stay out of view until the flow asks for them, whatever state the
        // prefab or scene was saved in. Controller models never appear in this hands-only app.
        void HideLegacyButtons()
        {
            if (normalModeButton) normalModeButton.Hide();
            if (boostModeButton) boostModeButton.Hide();
            if (startButton) startButton.Hide();
            if (modeCanvas) modeCanvas.gameObject.SetActive(false);
            if (round && round.restartButton) round.restartButton.Hide();
            if (!hideControllerModels) return;
            foreach (var helper in FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (helper && helper.gameObject.scene.IsValid()) helper.gameObject.SetActive(false);
        }
        void OnEnable()
        {
            OVRManager.HMDMounted += Mounted;
            OVRManager.HMDUnmounted += Unmounted;
            if (trackedRig) trackedRig.UpdatedAnchors += AnchorsUpdated;
            if (initialized)
            {
                State.Suspend(clock);
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
            if (SessionOpen && Stage != KinoExperienceStage.Waiting && Stage != KinoExperienceStage.ModeSelection && Stage != KinoExperienceStage.Standby)
            { Record.abortedUtc = Utc; SaveRecord(); }
            StopRound();
            State.Suspend(clock);
            if (content) content.alpha = 0;
            if (modeCanvas) modeCanvas.gameObject.SetActive(false);
            modeHands?.SetVisible(false);
            if (enclosure) enclosure.SetBackground(0);
            if (waves) waves.HideImmediately();
            SetBlackout(1);
            SetAudio(0);
        }
        [ContextMenu("Show Normal / Boost selection")]
        public void ShowModeSelection()
        {
            if (!isActiveAndEnabled || !round || (Stage != KinoExperienceStage.Waiting && Stage != KinoExperienceStage.Complete && Stage != KinoExperienceStage.Standby)) return;
            mounted = true;
            awaitingTrackedView = false;
            ResetForVisitor();
            sequenceIndex = ReadyIndex;
            if (!offerBoostSelection) { ShowReady(); return; }
            round.showcaseBoostAfterSecondChance = false;
            State.SelectMode(clock);
            PresentStage();
        }
        public void SelectMode(bool withBoost)
        {
            if (!isActiveAndEnabled || Stage != KinoExperienceStage.ModeSelection) return;
            round.showcaseBoostAfterSecondChance = withBoost;
            State.ChooseMode(clock, withBoost);
            PresentStage();
        }
        [ContextMenu("Start selected session from standby")]
        public void StartSelectedSession()
        {
            if (Stage == KinoExperienceStage.Standby && State.HasSelectedMode) BeginSelectedSession(State.IncludeBoost);
        }
        [ContextMenu("Begin Normal session")]
        public void BeginNormalSession() => BeginSelectedSession(false);
        [ContextMenu("Begin session with Boost")]
        public void BeginBoostSession() => BeginSelectedSession(true);
        public void BeginSession() => BeginSelectedSession(offerBoostSelection ? false : includeBoost);
        void BeginSelectedSession(bool withBoost)
        {
            if (!isActiveAndEnabled || !round || (Stage != KinoExperienceStage.Waiting && Stage != KinoExperienceStage.Complete && Stage != KinoExperienceStage.ModeSelection && Stage != KinoExperienceStage.Standby)) return;
            mounted = true;
            awaitingTrackedView = false;
            OpenSession(withBoost);
            // Startup is the short settle after ΞΕΚΙΝΑ; the sequence continues below the Ready step.
            State.Begin(clock, withBoost);
            PresentStage();
        }
        void ResetForVisitor()
        {
            StopRound();
            if (round.score) round.score.ResetScore();
            if (round.board) round.board.ResetBoard();
        }
        void OpenSession(bool withBoost)
        {
            ResetForVisitor();
            State.SetMode(withBoost);
            round.showcaseBoostAfterSecondChance = withBoost;
            Record = new KinoSessionRecord { sessionId = Guid.NewGuid().ToString("N"), startedUtc = Utc,
                safetyVersion = safetyVersion, placeholderSafety = placeholderSafety, includeBoost = withBoost };
            if (round.audioController) round.audioController.BeginSessionAudio();
        }
        void ShowReady()
        {
            awaitingTrackedView = false;
            ResetForVisitor();
            if (offerBoostSelection && !State.HasSelectedMode)
            {
                round.showcaseBoostAfterSecondChance = false;
                State.SelectMode(clock);
            }
            else
            {
                // No buttons for the mode: every visitor gets the configured Boost setting.
                if (!offerBoostSelection) State.SetMode(includeBoost);
                round.showcaseBoostAfterSecondChance = State.IncludeBoost;
                State.Standby(clock);
            }
            PresentStage();
        }
        /// <summary>Plays the first enabled pre-game step at or after index, or starts the game.</summary>
        void RunPreGameFrom(int index)
        {
            int ready = ReadyIndex;
            if (preGame != null)
                for (int i = Mathf.Max(0, index); i < preGame.Count; i++)
                {
                    var entry = preGame[i];
                    if (entry == null || !entry.enabled || !KinoSequence.IsPreGame(entry.step)) continue;
                    sequenceIndex = i;
                    if (ready >= 0 && i >= ready) appEntryDone = true;
                    if (entry.step == KinoSequenceStep.Ready) { ShowReady(); return; }
                    // Without a Ready step each visitor's session opens with the first screen.
                    if (ready < 0 && !SessionOpen) OpenSession(includeBoost);
                    State.Go(KinoSequence.StageFor(entry.step), clock);
                    PresentStage();
                    return;
                }
            sequenceIndex = preGame != null ? preGame.Count : 0;
            appEntryDone = true;
            if (!SessionOpen) OpenSession(offerBoostSelection && State.HasSelectedMode ? State.IncludeBoost : includeBoost);
            State.Go(KinoExperienceStage.Gameplay, clock);
            PresentStage();
        }
        /// <summary>Plays the first enabled post-game step at or after index, or completes the session.</summary>
        void RunPostFrom(int index)
        {
            if (postGame != null)
                for (int i = Mathf.Max(0, index); i < postGame.Count; i++)
                {
                    var entry = postGame[i];
                    if (entry == null || !entry.enabled || KinoSequence.IsPreGame(entry.step)) continue;
                    postIndex = i;
                    State.Go(KinoSequence.StageFor(entry.step), clock);
                    PresentStage();
                    return;
                }
            postIndex = postGame != null ? postGame.Count : 0;
            State.Go(KinoExperienceStage.Complete, clock);
            PresentStage();
        }
        bool NextPostIsOutro()
        {
            if (postGame == null) return false;
            for (int i = postIndex + 1; i < postGame.Count; i++)
                if (postGame[i] != null && postGame[i].enabled) return postGame[i].step == KinoSequenceStep.RemoveHeadset;
            return false;
        }
        bool StageFinished(float elapsed)
        {
            switch (Stage)
            {
                case KinoExperienceStage.AllwynLogo:
                case KinoExperienceStage.KinoLogo: return elapsed >= brandingSeconds;
                case KinoExperienceStage.Startup: return elapsed >= startupSeconds;
                case KinoExperienceStage.Safety: return elapsed >= safetySeconds && (!requireExternalSafetyConfirmation || State.ExternalConfirmation);
                case KinoExperienceStage.Introduction: return elapsed >= introductionSeconds;
                case KinoExperienceStage.KinoSplash: return elapsed >= kinoSplashSeconds;
                case KinoExperienceStage.Finale: return elapsed >= finaleSeconds;
                case KinoExperienceStage.Closing: return elapsed >= closingSeconds;
                default: return false;
            }
        }
        void AdvanceSequence()
        {
            var previous = Stage;
            if (previous == KinoExperienceStage.Safety && Record != null) { Record.safetyElapsedUtc = Utc; SaveRecord(); }
            if (previous == KinoExperienceStage.Startup) RunPreGameFrom(ReadyIndex + 1);
            else if (previous == KinoExperienceStage.Finale || previous == KinoExperienceStage.Closing) RunPostFrom(postIndex + 1);
            else RunPreGameFrom(sequenceIndex + 1);
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
            // exactly once. The next visitor starts at the Ready step (or the top of the list).
            if (Stage == KinoExperienceStage.Complete) { awaitingTrackedView = false; ResetForVisitor(); RunPreGameFrom(VisitorStart); return; }
            if (Stage == KinoExperienceStage.Gameplay && !roundPending && round.State.Phase >= KinoRoundPhase.BoardHold &&
                round.State.Phase <= KinoRoundPhase.SecondChance && State.BeginSecondChance(clock)) PresentStage();
            if (Stage == KinoExperienceStage.SecondChance && (round.State.Phase == KinoRoundPhase.BoostIntro || round.State.Phase == KinoRoundPhase.Boost ||
                round.State.Phase == KinoRoundPhase.BoostSettling) && State.BeginBoost(clock)) PresentStage();
            if (StageFinished((float)(clock - State.EnteredAt))) AdvanceSequence();
            AnimateStage();
        }
        void FinishSessionRound()
        {
            if (!isActiveAndEnabled || !State.Finish(clock)) return;
            Record.score = round.State.Score;
            Record.normalCatches = round.State.NormalCatchCount;
            Record.glowCatches = round.State.GlowCatchCount;
            Record.mysteryCatches = round.State.MysteryCatchCount;
            Record.mainScore = round.State.MainScore;
            Record.secondChanceCatches = round.State.SecondChanceCatchCount;
            Record.boostCatches = round.State.BoostCatchCount;
            Record.completedUtc = Utc;
            SaveRecord();
            onResultReady.Invoke(JsonUtility.ToJson(Record));
            RunPostFrom(0);
        }
        void PresentStage()
        {
            if (!content) return;
            bool overlay = Stage == KinoExperienceStage.ModeSelection || Stage == KinoExperienceStage.Safety ||
                Stage == KinoExperienceStage.AllwynLogo || Stage == KinoExperienceStage.KinoLogo ||
                Stage == KinoExperienceStage.Introduction || Stage == KinoExperienceStage.KinoSplash || Stage == KinoExperienceStage.Standby ||
                Stage == KinoExperienceStage.Finale || Stage == KinoExperienceStage.Closing;
            content.alpha = overlay ? 1 : 0;
            ApplyBackground();
            PositionContent();
            if (modeCanvas)
            {
                modeCanvas.gameObject.SetActive(Stage == KinoExperienceStage.ModeSelection || Stage == KinoExperienceStage.Standby);
                bool menu = Stage == KinoExperienceStage.ModeSelection;
                if (menu) PositionModeSelection();
                if (normalModeButton) { if (menu) normalModeButton.Show(); else normalModeButton.Hide(); }
                if (boostModeButton) { if (menu) boostModeButton.Show(); else boostModeButton.Hide(); }
                if (startButton)
                {
                    if (Stage == KinoExperienceStage.Standby) { PositionModeSelection(); startButton.Show(); }
                    else startButton.Hide();
                }
            }
            allwynLogo.gameObject.SetActive(false);
            kinoLogo.gameObject.SetActive(false);
            allwynLogo.color = kinoLogo.color = Color.white;
            HidePregameDecor();
            title.color = Color.white;
            body.color = Color.white;
            allwynLogo.rectTransform.anchoredPosition = Vector2.zero;
            allwynLogo.rectTransform.sizeDelta = new Vector2(600, 600f * allwynLogo.texture.height / allwynLogo.texture.width);
            kinoLogo.rectTransform.anchoredPosition = Vector2.zero;
            kinoLogo.rectTransform.sizeDelta = new Vector2(600, 600f * kinoLogo.texture.height / kinoLogo.texture.width);
            bool finale = Stage == KinoExperienceStage.Finale;
            if (round.board) round.board.SetFinaleCover(finale);
            if (finalePanel) finalePanel.gameObject.SetActive(finale);
            title.rectTransform.anchoredPosition = new Vector2(0, finale ? 165 : 225);
            title.rectTransform.sizeDelta = new Vector2(1100, Stage == KinoExperienceStage.Introduction ? 120 : 100);
            title.fontSize = finale ? 52 : Stage == KinoExperienceStage.Introduction ? 42 : 48;
            title.color = finale ? KinoScreenTypography.Ivory : Color.white;
            body.rectTransform.anchoredPosition = new Vector2(0, finale ? -8 : -5);
            body.rectTransform.sizeDelta = finale ? new Vector2(360, 195) : new Vector2(1040, 330);
            body.fontSize = finale ? 156 : 34;
            body.fontSizeMax = 156;
            body.fontSizeMin = 80;
            body.enableAutoSizing = finale;
            body.enableVertexGradient = false;
            body.fontStyle = FontStyles.Normal;
            body.textWrappingMode = finale ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            ApplyFonts(!finale);
            if (finale) KinoScreenTypography.Gold(body, goldScoreMaterial);
            footer.rectTransform.anchoredPosition = new Vector2(0, finale ? -177 : -245);
            footer.rectTransform.sizeDelta = finale ? new Vector2(650, 54) : new Vector2(1080, 65);
            footer.fontSize = finale ? 30 : 25;
            footer.color = finale ? KinoScreenTypography.Ivory : new Color(.68f, .83f, .93f);
            title.text = body.text = footer.text = "";
            switch (Stage)
            {
                case KinoExperienceStage.ModeSelection:
                    SetBlackout(0); SetAudio(0);
                    title.text = "ΕΠΙΛΕΞΕ ΕΜΠΕΙΡΙΑ";
                    body.text = "Επίλεξε το παιχνίδι για τους επισκέπτες.";
                    break;
                case KinoExperienceStage.Standby:
                    SetBlackout(0); SetAudio(0);
                    title.text = "ΕΤΟΙΜΟΣ ΓΙΑ ΠΑΙΧΝΙΔΙ;";
                    body.text = ReadyTouchLine;
                    LayoutReady();
                    break;
                // With the environment, startup stays on the dimmed waves instead of black.
                case KinoExperienceStage.Startup: SetBlackout(UseWaves ? 0 : 1); SetAudio(0); break;
                case KinoExperienceStage.Safety:
                    title.text = "ΠΡΙΝ ΞΕΚΙΝΗΣΟΥΜΕ"; body.text = safetyText;
                    LayoutSafety();
                    footer.text = placeholderSafety ? "ΠΡΟΣΩΡΙΝΟ ΚΕΙΜΕΝΟ • ΠΡΟΣ ΑΝΤΙΚΑΤΑΣΤΑΣΗ" : "";
                    // Above the Ready step the safety screen plays at app entry, before any visitor session.
                    if (Record != null) { Record.safetyDisplayedUtc = Utc; SaveRecord(); onSafetyDisplayed.Invoke(JsonUtility.ToJson(Record)); }
                    break;
                case KinoExperienceStage.AllwynLogo:
                case KinoExperienceStage.KinoLogo: SetBlackout(0); AnimateLogo(0); break;
                case KinoExperienceStage.Introduction:
                    title.text = brandLogo ? "ΚΑΛΩΣ ΗΡΘΕΣ\n<color=#FFD400>ΣΤΟΝ ΚΟΣΜΟ ΤΟΥ ΚΙΝΟ</color>" : "ΚΑΛΩΣ ΗΡΘΕΣ\nΣΤΟΝ ΚΟΣΜΟ ΤΟΥ ΚΙΝΟ";
                    body.text = "Πιάσε τις μπάλες με τα χέρια σου.\nΚάθε πιάσιμο μετράει!";
                    footer.text = "Μείνε καθιστός και κράτα τα χέρια σου ελεύθερα.";
                    LayoutWelcome(); break;
                case KinoExperienceStage.KinoSplash:
                    SetBlackout(0); SetAudio(1);
                    kinoLogo.gameObject.SetActive(true);
                    break;
                case KinoExperienceStage.Gameplay:
                    // The room is already revealed; balls start after a short settle.
                    SetAudio(1);
                    roundPending = true;
                    if (roundStartDelay <= 0) { roundPending = false; round.BeginRound(); }
                    break;
                case KinoExperienceStage.Finale:
                    title.text = "ΜΠΡΑΒΟ!"; body.text = Record.score.ToString();
                    footer.text = "Ευχαριστούμε που έπαιξες!"; break;
                case KinoExperienceStage.Closing:
                    title.text = "Η ΕΜΠΕΙΡΙΑ ΟΛΟΚΛΗΡΩΘΗΚΕ";
                    body.text = "Παρέμεινε καθιστός.\nΒγάλε ήρεμα το headset\nμε τη βοήθεια του προσωπικού.";
                    if (Outro) { footer.text = "Ευχαριστούμε που έπαιξες!"; LayoutOutro(); }
                    break;
                case KinoExperienceStage.Complete:
                    SetBlackout(1); SetAudio(0); if (round.audioController) round.audioController.StopAll();
                    Record.closedUtc = Utc; SaveRecord(); onSessionClosed.Invoke(); break;
            }
        }
        void AnimateStage()
        {
            ApplyBackground();
            float elapsed = (float)(clock - State.EnteredAt);
            if (Stage == KinoExperienceStage.Safety)
            {
                // The environment stays continuous: the panel fades in instead of a fade from black.
                if (UseWaves) { SetBlackout(0); content.alpha = Mathf.SmoothStep(0, 1, elapsed / .8f); }
                else SetBlackout(1 - Mathf.SmoothStep(0, 1, elapsed / .8f));
                if (safetyProgress) SetProgress(Mathf.Clamp01(elapsed / safetySeconds));
            }
            else if (Stage != KinoExperienceStage.Startup && Stage != KinoExperienceStage.Complete) SetBlackout(0);
            if (Stage == KinoExperienceStage.Safety && !placeholderSafety)
                footer.text = requireExternalSafetyConfirmation && !State.ExternalConfirmation ? "Περιμένουμε επιβεβαίωση από το προσωπικό." : "Η εμπειρία ξεκινά σε λίγο.";
            if (Stage == KinoExperienceStage.AllwynLogo || Stage == KinoExperienceStage.KinoLogo)
                AnimateLogo(elapsed);
            if (Stage == KinoExperienceStage.Standby && brandLogo)
            {
                // Hand tracking starts only when the hands are in front of the cameras: ask for them
                // until the first hand is rendered, then ask for the touch.
                bool hands = modeHands == null || modeHands.AnyHandVisible || !XRSettings.isDeviceActive;
                string line = hands ? ReadyTouchLine : ReadyHandsLine;
                if (body.text != line) body.text = line;
            }
            if (Stage == KinoExperienceStage.ModeSelection || Stage == KinoExperienceStage.Standby)
                SetBlackout(1 - Mathf.SmoothStep(0, 1, elapsed / .4f));
            if (Stage == KinoExperienceStage.Gameplay)
            {
                // With the environment the splash already revealed the room: no fade from black.
                SetBlackout(UseWaves ? 0 : 1 - Mathf.SmoothStep(0, 1, elapsed / .8f));
                if (roundPending && elapsed >= roundStartDelay) { roundPending = false; round.BeginRound(); }
            }
            if (Stage == KinoExperienceStage.KinoSplash && UseWaves)
            {
                // The logo leaves last, once the room is visible.
                float t = elapsed / kinoSplashSeconds;
                kinoLogo.color = new Color(1, 1, 1, 1 - Mathf.SmoothStep(0, 1, (t - .8f) / .2f));
            }
            if (Stage == KinoExperienceStage.Introduction)
            {
                SetBlackout(UseWaves ? 0 : 1 - Mathf.SmoothStep(0, 1, elapsed / 1.2f));
                content.alpha = Mathf.Min(Mathf.Clamp01(elapsed / .8f), Mathf.Clamp01((introductionSeconds - elapsed) / .8f));
                // Reading stays on an opaque 360-degree background. Without the environment the
                // last fade conceals the switch to the static KINO splash; with it the waves carry over.
                if (!UseWaves) SetBlackout(Mathf.Max(BlackoutAlpha, Mathf.SmoothStep(0, 1, (elapsed - introductionSeconds + .5f) / .5f)));
                SetAudio(Mathf.SmoothStep(0, 1, elapsed / 2));
            }
            if (Stage == KinoExperienceStage.Finale)
            {
                content.alpha = Mathf.Clamp01(elapsed / .8f);
                // Outro: the level fades out at the end of the score, before the blue environment.
                if (Outro && NextPostIsOutro())
                    SetBlackout(Mathf.SmoothStep(0, 1, (elapsed - (finaleSeconds - .6f)) / .6f));
            }
            if (Stage == KinoExperienceStage.Closing)
            {
                if (Outro)
                {
                    SetBlackout(1 - Mathf.SmoothStep(0, 1, elapsed / .6f));
                    content.alpha = Mathf.SmoothStep(0, 1, (elapsed - .2f) / .8f);
                }
                float fade = Mathf.SmoothStep(0, 1, (elapsed - closingSeconds + .5f) / .5f);
                // Hold the requested transparency for the whole removal instruction.
                // Full black belongs to Complete, after the five-second message.
                SetAudio(1 - fade);
            }
            DriveEnvironment(elapsed);
        }
        void DriveEnvironment(float elapsed)
        {
            if (!waves) return;
            switch (Stage)
            {
                case KinoExperienceStage.AllwynLogo:
                case KinoExperienceStage.KinoLogo: waves.Drive(1, .3f); break;
                case KinoExperienceStage.Closing:
                    if (Outro) waves.Drive(1, .2f); else waves.Drive(0, 0, 0, 0, true);
                    break;
                case KinoExperienceStage.ModeSelection:
                case KinoExperienceStage.Standby: waves.Drive(1, .15f); break;
                case KinoExperienceStage.Startup: waves.Drive(1, .45f); break;
                case KinoExperienceStage.Safety: waves.Drive(1, .6f); break;
                case KinoExperienceStage.Introduction: waves.Drive(1, .25f); break;
                case KinoExperienceStage.KinoSplash:
                {
                    // The ribbons collapse into a single beam behind the logo; as the beam forms the
                    // navy background fades away to reveal the room, then the beam itself clears.
                    float t = elapsed / kinoSplashSeconds;
                    float collapse = Mathf.SmoothStep(0, 1, t / .55f);
                    float flash = Mathf.Clamp01(1 - Mathf.Abs(t - .6f) / .15f);
                    float beam = 1 - Mathf.SmoothStep(0, 1, (t - .75f) / .25f);
                    waves.Drive(beam, 0, collapse, flash * flash, true, 1 - SplashReveal(elapsed));
                    break;
                }
                default: waves.Drive(0, 0, 0, 0, true); break;
            }
        }
        void HidePregameDecor()
        {
            if (brandLogo) { brandLogo.gameObject.SetActive(false); brandLogo.color = Color.white; }
            if (vrExperienceLogo) { vrExperienceLogo.gameObject.SetActive(false); vrExperienceLogo.color = Color.white; }
            if (safetyPanel) safetyPanel.gameObject.SetActive(false);
            if (safetyProgress && safetyProgress.parent) safetyProgress.parent.gameObject.SetActive(false);
        }
        static void PlaceLogo(RawImage logo, Vector2 position, float width)
        {
            logo.gameObject.SetActive(true);
            logo.rectTransform.anchoredPosition = position;
            logo.rectTransform.sizeDelta = new Vector2(width, width * logo.texture.height / logo.texture.width);
        }
        static void Place(TMP_Text label, float y, Vector2 size, float points)
        {
            label.rectTransform.anchoredPosition = new Vector2(0, y);
            label.rectTransform.sizeDelta = size;
            label.fontSize = points;
        }
        void LayoutReady()
        {
            if (!brandLogo || !vrExperienceLogo) return;
            PlaceLogo(brandLogo, new Vector2(0, 205), 340);
            PlaceLogo(vrExperienceLogo, new Vector2(0, 40), 330);
            Place(title, -88, new Vector2(1100, 80), 46);
            Place(body, -200, new Vector2(1040, 130), 30);
            body.color = new Color(.78f, .88f, .97f);
        }
        void LayoutSafety()
        {
            if (!safetyPanel) return;
            safetyPanel.gameObject.SetActive(true);
            if (brandLogo) PlaceLogo(brandLogo, new Vector2(0, 312), 190);
            Place(title, 205, new Vector2(1000, 80), 44);
            Place(body, -12, new Vector2(900, 320), 31);
            body.enableAutoSizing = true; body.fontSizeMin = 22; body.fontSizeMax = 31;
            footer.rectTransform.anchoredPosition = new Vector2(0, -222);
            if (safetyProgress && safetyProgress.parent) { safetyProgress.parent.gameObject.SetActive(true); SetProgress(0); }
        }
        void LayoutWelcome()
        {
            if (!brandLogo) return;
            PlaceLogo(brandLogo, new Vector2(0, 218), 370);
            Place(title, -12, new Vector2(1100, 190), 60);
            Place(body, -172, new Vector2(1040, 100), 30);
            footer.rectTransform.anchoredPosition = new Vector2(0, -262);
            footer.fontSize = 24;
        }
        void LayoutOutro()
        {
            if (!brandLogo) return;
            PlaceLogo(brandLogo, new Vector2(0, 205), 340);
            Place(title, -38, new Vector2(1100, 80), 48);
            Place(body, -160, new Vector2(1040, 150), 30);
            footer.rectTransform.anchoredPosition = new Vector2(0, -270);
            footer.fontSize = 24;
        }
        void SetProgress(float fraction)
        {
            var track = safetyProgress.parent as RectTransform;
            if (!track) return;
            safetyProgress.sizeDelta = new Vector2(track.rect.width * fraction, safetyProgress.sizeDelta.y);
        }
        void ApplyFonts(bool brand)
        {
            ApplyFont(title, headingFont, defaultTitleFont, titleMaterial, brand);
            ApplyFont(body, textFont, defaultBodyFont, bodyMaterial, brand);
            ApplyFont(footer, textFont, defaultFooterFont, footerMaterial, brand);
        }
        static void ApplyFont(TMP_Text label, TMP_FontAsset brandFont, TMP_FontAsset original, Material originalMaterial, bool brand)
        {
            var font = brand && brandFont ? brandFont : original;
            if (!label || !font) return;
            if (label.font != font) label.font = font;
            // Materials are atlas-specific: never pair one font's material with another font.
            label.fontSharedMaterial = font == original && originalMaterial ? originalMaterial : font.material;
        }
        /// <summary>0 while the splash background is solid, 1 once the room is fully revealed.</summary>
        float SplashReveal(float elapsed) => UseWaves ? Mathf.SmoothStep(0, 1, (elapsed / kinoSplashSeconds - .5f) / .4f) : 0;
        void ApplyBackground()
        {
            if (!enclosure) return;
            bool instructions = Stage == KinoExperienceStage.ModeSelection || Stage == KinoExperienceStage.Safety ||
                Stage == KinoExperienceStage.AllwynLogo || Stage == KinoExperienceStage.KinoLogo || Stage == KinoExperienceStage.Introduction ||
                Stage == KinoExperienceStage.Standby || Stage == KinoExperienceStage.KinoSplash ||
                Stage == KinoExperienceStage.Startup && UseWaves || Stage == KinoExperienceStage.Closing && Outro;
            if (Stage == KinoExperienceStage.KinoSplash) { enclosure.SetBackground(1 - SplashReveal((float)(clock - State.EnteredAt))); return; }
            float alpha = Stage == KinoExperienceStage.Finale ? 1 - Mathf.Clamp01(finaleBackgroundTransparency / 100) :
                Stage == KinoExperienceStage.Closing && !Outro ? 1 - Mathf.Clamp01(closingBackgroundTransparency / 100) : instructions ? 1 : 0;
            enclosure.SetBackground(alpha);
        }
        void PositionContent()
        {
            var view = round.playerView ? round.playerView.View : null;
            if (!contentCanvas) return;
            if (Stage == KinoExperienceStage.Finale)
            {
                KinoBoardOverlay.Place(contentCanvas, round.board, KinoBoardOverlay.ArtworkSize);
                if (view) contentCanvas.worldCamera = view.GetComponent<Camera>();
                return;
            }
            contentCanvas.transform.SetParent(contentParent, false);
            ((RectTransform)contentCanvas.transform).sizeDelta = new Vector2(1100, 700);
            contentCanvas.overrideSorting = false;
            if (!view) return;
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            var heading = Quaternion.LookRotation(forward.normalized);
            // The ring is placed once per appearance so it never jumps during the sequence.
            if (waves && !waves.IsShowing) waves.Anchor(view.position, forward);
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
            var position = view.position + heading * TouchOffset;
            modeCanvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - view.position, Vector3.up));
            modeCanvas.worldCamera = view.GetComponent<Camera>();
        }
        void AnimateLogo(float elapsed)
        {
            // One logo per step, each fading in and out over brandingSeconds.
            content.alpha = 1;
            float fade = Mathf.Min(logoFadeSeconds, brandingSeconds * .45f);
            float alpha = Mathf.Min(Mathf.Clamp01(elapsed / fade), Mathf.Clamp01((brandingSeconds - elapsed) / fade));
            bool allwyn = Stage == KinoExperienceStage.AllwynLogo;
            allwynLogo.gameObject.SetActive(allwyn);
            kinoLogo.gameObject.SetActive(!allwyn);
            if (allwyn) { allwynLogo.color = new Color(1, 1, 1, alpha); return; }
            kinoLogo.color = new Color(1, 1, 1, alpha);
            if (!vrExperienceLogo) return;
            // KINO above the chrome VR EXPERIENCE mark, as in the key visual.
            kinoLogo.rectTransform.anchoredPosition = new Vector2(0, 80);
            kinoLogo.rectTransform.sizeDelta = new Vector2(520, 520f * kinoLogo.texture.height / kinoLogo.texture.width);
            PlaceLogo(vrExperienceLogo, new Vector2(0, -210), 470);
            vrExperienceLogo.color = new Color(1, 1, 1, alpha);
        }
        void LateUpdate()
        {
            if (awaitingTrackedView && initialized && mounted)
            {
                var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                bool desktop = Application.isEditor && !XRSettings.isDeviceActive;
                if (desktop || lastAnchorFrame == Time.frameCount &&
                    device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
                {
                    awaitingTrackedView = false;
                    // App-entry steps (above Ready) play once; later mounts start at Ready.
                    RunPreGameFrom(appEntryDone ? VisitorStart : 0);
                }
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
            roundPending = false;
            round.State.Stop();
            if (round.launcher) round.launcher.StopLaunching(true);
            if (round.restartButton) round.restartButton.Hide();
            if (round.secondChancePresentation) round.secondChancePresentation.ResetPresentation();
            if (round.board) round.board.SetFinaleCover(false);
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
            if (SessionOpen && Stage != KinoExperienceStage.Waiting && Stage != KinoExperienceStage.ModeSelection && Stage != KinoExperienceStage.Standby)
            { Record.abortedUtc = Utc; SaveRecord(); }
            StopRound(); SetAudio(1);
            modeHands?.SetVisible(false);
            if (content) content.alpha = 0;
            if (modeCanvas) modeCanvas.gameObject.SetActive(false);
            if (enclosure) enclosure.SetBackground(0);
            if (waves) waves.HideImmediately();
            SetBlackout(0);
        }
        void OnDestroy() { modeHands?.Dispose(); if (round) round.onRoundFinished.RemoveListener(FinishSessionRound); }
    }
}
