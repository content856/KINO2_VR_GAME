using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KinoVR
{
    [DisallowMultipleComponent]
    public sealed class KinoBackendFlow : MonoBehaviour
    {
        public KinoApiClient api;
        public KinoBackendPanel panel;
        public bool StoreReady { get; private set; }
        public bool PlayerReady => ActiveSession != null && ActiveSession.status == "active" && !HasUnresolvedResult && !recovering;
        public KinoApiGameSession ActiveSession { get; private set; }
        public bool HasUnresolvedResult => transaction != null && transaction.pendingScore;
        public bool Busy { get; private set; }
        public string ErrorCode { get; private set; }
        public string LastStorageError { get; private set; }
        public string SelectedCampaignId => campaign?.id;
        public string SelectedRegion => region;
        public string SelectedStoreId => store?.id;
        public KinoApiPlayer[] Players { get; private set; } = Array.Empty<KinoApiPlayer>();
        public event Action SetupRequested;
        KinoExperienceController experience;
        KinoBackendJournal journal;
        KinoBackendTransaction transaction;
        KinoApiContext context;
        KinoApiCampaign[] campaigns;
        KinoApiCampaign campaign;
        KinoApiStore store;
        string region, pairingCode = "", status = "";
        bool initialized, visible, contextChecked, recovering, storageFailed, terminalError, loadingPlayers, operatorMenu;
        int generation, failures;
        double retryAt, nextPoll, serverRetryAt;
        KinoSequenceStep step;
        Action completed;
        static double Now => Time.realtimeSinceStartupAsDouble;

        public void Initialize(KinoExperienceController owner, KinoApiClient client = null, string storageDirectory = null, KinoBackendPanel view = null)
        {
            if (initialized) return;
            initialized = true;
            experience = owner;
            api = client ? client : GetComponent<KinoApiClient>();
            if (!api) api = gameObject.AddComponent<KinoApiClient>();
            if (owner && !client) api.apiBaseUrl = owner.leaderboardApiBaseUrl;
            panel = view;
            if (!panel && owner && owner.contentCanvas) panel = gameObject.AddComponent<KinoBackendPanel>();
            if (panel) panel.Initialize(owner);
            journal = new KinoBackendJournal(storageDirectory ?? Path.Combine(Application.persistentDataPath, "KinoBackend"), api.apiBaseUrl);
            try { transaction = journal.Load(); recovering = transaction != null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { StorageError(e); }
        }

        public void EnterStep(KinoSequenceStep requested, Action done)
        {
            step = requested; completed = done; visible = true; generation++;
            operatorMenu = false;
            if (step == KinoSequenceStep.PlayerSelection) nextPoll = 0;
            DrawWaiting("Σύνδεση…"); Pump();
        }
        void Update()
        {
            if (!initialized || Busy || Now < retryAt) return;
            if (visible) Pump();
            else if (transaction != null && (transaction.pendingScore || transaction.needsAbandon || recovering) &&
                !storageFailed && !terminalError && string.IsNullOrEmpty(transaction.rejectionCode) && api.IsPaired)
            {
                // Result delivery continues during the outro and while the headset is off.
                if (!contextChecked) Run(LoadContext()); else Run(Recover());
            }
        }
        void Run(IEnumerator operation)
        {
            if (Busy || !isActiveAndEnabled || !gameObject.activeInHierarchy) return;
            StartCoroutine(Execute(operation));
        }
        IEnumerator Execute(IEnumerator operation)
        {
            Busy = true;
            try { yield return operation; }
            finally { Busy = false; }
        }
        void Pump()
        {
            if (!visible || Busy) return;
            if (Now < retryAt) { DrawError(); return; }
            if (storageFailed) { DrawWaiting("Αδυναμία τοπικής αποθήκευσης. Ζήτησε βοήθεια από το προσωπικό."); return; }
            if (!api.IsPaired) { contextChecked = false; StoreReady = false; ShowPairing(); return; }
            if (!contextChecked) { Run(LoadContext()); return; }
            if (transaction != null && (recovering || transaction.pendingScore || transaction.needsAbandon))
            {
                if (!string.IsNullOrEmpty(transaction.rejectionCode)) { DrawRejected(); return; }
                if (terminalError) { DrawError(); return; }
                Run(Recover()); return;
            }
            if (terminalError) { DrawError(); return; }
            if (campaigns == null) { Run(LoadCampaigns()); return; }
            if (campaign == null)
            {
                if (campaigns.Length == 1) { ChooseCampaign(campaigns[0].id); return; }
                DrawCampaigns(); return;
            }
            if (step == KinoSequenceStep.CampaignSelection) { Finish(); return; }
            if (region == null) { DrawRegions(); return; }
            if (step == KinoSequenceStep.RegionSelection) { Finish(); return; }
            if (!StoreReady) { DrawStores(); return; }
            if (step == KinoSequenceStep.StoreSelection) { Finish(); return; }
            if (ActiveSession != null)
            {
                if (PlayerReady) { Finish(); return; }
                return;
            }
            if (Now >= nextPoll && !loadingPlayers) { Run(LoadPlayers()); return; }
            DrawPlayers();
        }
        IEnumerator LoadContext()
        {
            KinoApiResult<KinoApiContext> result = null;
            yield return api.Request<KinoApiContext>("GET", "/device/context", null, r => result = r);
            if (!Accept(result)) yield break;
            if (string.IsNullOrEmpty(result.Value?.device?.id)) { Failure("INVALID_RESPONSE", true); yield break; }
            context = result.Value; contextChecked = true;
            if (transaction != null && !string.IsNullOrEmpty(transaction.deviceId) && transaction.deviceId != context?.device?.id)
            {
                transaction.rejectionCode = "DEVICE_CHANGED";
                transaction.pendingScore = true; Persist();
            }
        }
        IEnumerator LoadCampaigns()
        {
            DrawWaiting("Φόρτωση καταστημάτων…");
            KinoApiResult<KinoApiSelectableStores> result = null;
            yield return api.Request<KinoApiSelectableStores>("GET", "/device/selectable-stores", null, r => result = r);
            if (!Accept(result)) yield break;
            campaigns = (result.Value?.campaigns ?? Array.Empty<KinoApiCampaign>()).Where(c => c != null && !string.IsNullOrEmpty(c.id)).ToArray();
        }
        public void ChooseCampaign(string id)
        {
            if (!visible || Busy || HasUnresolvedResult || transaction != null) return;
            var chosen = campaigns?.FirstOrDefault(c => c.id == id);
            if (chosen == null) return;
            campaign = chosen; region = null; store = null; StoreReady = false;
            if (step == KinoSequenceStep.CampaignSelection) Finish(); else Pump();
        }
        public void ChooseRegion(string value)
        {
            if (!visible || Busy || campaign == null || transaction != null) return;
            value = value ?? "";
            if (!(campaign.stores ?? Array.Empty<KinoApiStore>()).Any(s => (s.region ?? "") == value)) return;
            region = value; store = null; StoreReady = false;
            if (step == KinoSequenceStep.RegionSelection) Finish(); else Pump();
        }
        public void ChooseStore(string id)
        {
            if (!visible || Busy || transaction != null || campaign == null || region == null) return;
            var chosen = campaign.stores?.FirstOrDefault(s => s.id == id && (s.region ?? "") == region);
            if (chosen != null) Run(SelectStore(chosen));
        }
        IEnumerator SelectStore(KinoApiStore chosen)
        {
            DrawWaiting("Επιβεβαίωση καταστήματος…");
            KinoApiResult<KinoApiContext> result = null;
            yield return api.Request<KinoApiContext>("PUT", "/device/session-store", new KinoApiSelectStoreRequest { campaign_id = campaign.id, store_id = chosen.id }, r => result = r);
            if (!Accept(result)) yield break;
            if (result.Value?.store?.id != chosen.id || result.Value?.campaign?.id != campaign.id) { Failure("INVALID_RESPONSE", true); yield break; }
            context = result.Value; store = chosen; StoreReady = true; nextPoll = 0;
            // This is setup state, independent of the visitor/headset mount state.
            if (visible && step == KinoSequenceStep.StoreSelection) Finish();
        }
        IEnumerator LoadPlayers()
        {
            loadingPlayers = true;
            KinoApiResult<KinoApiPlayers> result = null;
            yield return api.Request<KinoApiPlayers>("GET", "/players", null, r => result = r);
            loadingPlayers = false;
            if (!Accept(result)) yield break;
            Players = result.Value?.players ?? Array.Empty<KinoApiPlayer>();
            nextPoll = Now + 7;
            status = string.IsNullOrEmpty(result.Value?.event_id) ? "Δεν υπάρχει ανοιχτό event. Ενημέρωσε το προσωπικό." :
                Players.Length == 0 ? "Περιμένουμε την εγγραφή σου από το tablet." : "Βρες και επίλεξε το nickname σου.";
            DrawPlayers();
        }
        public void ChoosePlayer(string registrationId)
        {
            if (!visible || Busy || !StoreReady || HasUnresolvedResult || transaction != null || step != KinoSequenceStep.PlayerSelection) return;
            var player = Players.FirstOrDefault(p => p.registration_id == registrationId);
            if (player == null) return;
            // Persist the intent before a request whose successful response can be lost.
            transaction = new KinoBackendTransaction { deviceId = context?.device?.id, campaignId = campaign.id, storeId = store.id,
                registrationId = player.registration_id, nickname = player.nickname, starting = true };
            if (Persist()) Run(StartPlayer(player, generation));
        }
        IEnumerator StartPlayer(KinoApiPlayer player, int requestedGeneration)
        {
            DrawWaiting("Επιβεβαίωση παίκτη…");
            KinoApiResult<KinoApiGameSession> result = null;
            yield return api.Request<KinoApiGameSession>("POST", "/game-sessions/start", new KinoApiStartRequest { registration_id = player.registration_id }, r => result = r);
            if (!Accept(result))
            {
                if (result != null && !result.Retryable && result.Status >= 400 && result.Status < 500 && result.Status != 401)
                { Archive(); nextPoll = 0; }
                else recovering = true;
                yield break;
            }
            if (!ValidSession(result.Value, player.registration_id)) { recovering = true; Failure("INVALID_RESPONSE", true); yield break; }
            ActiveSession = result.Value;
            transaction.gameSessionId = ActiveSession.game_session_id; transaction.expiresAt = ActiveSession.expires_at; transaction.starting = false;
            if (!Persist()) yield break;
            if (!visible || requestedGeneration != generation)
            { transaction.needsAbandon = true; recovering = true; Persist(); yield break; }
            Finish();
        }
        public void BindRecord(KinoSessionRecord record)
        {
            if (!PlayerReady || record == null) return;
            record.backendSessionId = ActiveSession.game_session_id; record.registrationId = ActiveSession.registration_id;
            record.nickname = ActiveSession.nickname; record.campaignId = campaign.id; record.storeId = store.id;
        }
        public void ConfirmReady(Action start)
        {
            if (!PlayerReady || Busy) return;
            Run(CheckReady(generation, start));
        }
        IEnumerator CheckReady(int selectedGeneration, Action start)
        {
            KinoApiResult<KinoApiGameSession> checkedSession = null;
            yield return api.Request<KinoApiGameSession>("GET", "/game-sessions/" + Uri.EscapeDataString(ActiveSession.game_session_id), null, r => checkedSession = r);
            if (selectedGeneration != generation) yield break;
            if (Accept(checkedSession) && ValidSession(checkedSession.Value, transaction?.registrationId) && checkedSession.Value.game_session_id == transaction?.gameSessionId)
            { ActiveSession = checkedSession.Value; panel?.Hide(); start(); }
            else
            {
                if (checkedSession != null && checkedSession.Success && checkedSession.Value.game_session_id == transaction?.gameSessionId && checkedSession.Value.status != "active") { Archive(); }
                else if (transaction != null) { transaction.needsAbandon = true; recovering = true; Persist(); }
                ActiveSession = null;
                experience?.ReturnToPlayerSelection();
            }
        }
        public void SubmitResult(KinoSessionRecord record)
        {
            if (record == null || transaction == null || record.backendSessionId != transaction.gameSessionId || transaction.pendingScore) return;
            transaction.score = record.score; transaction.submissionId = Guid.NewGuid().ToString("D");
            transaction.pendingScore = true; transaction.needsAbandon = false;
            record.submissionId = transaction.submissionId;
            recovering = true; ActiveSession = null;
            if (Persist() && !Busy) Run(Recover());
        }
        public void SuspendVisitor()
        {
            visible = false; completed = null; generation++; panel?.Hide();
            // A finished result must still be sent, never abandoned by headset removal.
            if (transaction != null && !transaction.pendingScore)
            { transaction.needsAbandon = true; recovering = true; Persist(); }
            ActiveSession = null; Players = Array.Empty<KinoApiPlayer>(); nextPoll = 0;
            if (!Busy && Now >= retryAt && transaction != null && api.IsPaired && contextChecked && !terminalError) Run(Recover());
        }
        public void ChangePlayer()
        {
            SuspendVisitor();
            experience?.ReturnToPlayerSelection();
        }
        public void ChangeStore()
        {
            if (Busy || transaction != null) return;
            operatorMenu = false; StoreReady = false; campaign = null; region = null; store = null; campaigns = null;
            SetupRequested?.Invoke();
        }
        public void Retry()
        {
            if (Busy || storageFailed) return;
            terminalError = false; retryAt = Math.Max(Now, serverRetryAt); failures = 0; ErrorCode = null; nextPoll = 0;
            if (campaigns != null && campaigns.Length == 0) campaigns = null;
            Pump();
        }
        // Only offered after a definite rejection. Ambiguous/network failures cannot be skipped.
        public void AcknowledgeRejectedResult()
        {
            if (Busy || transaction == null || string.IsNullOrEmpty(transaction.rejectionCode) || storageFailed) return;
            if (!Archive()) return;
            terminalError = false; ErrorCode = null; retryAt = 0; nextPoll = 0; Pump();
        }
        IEnumerator Recover()
        {
            if (transaction == null || storageFailed) yield break;
            DrawWaiting(transaction.pendingScore ? "Αποστολή του προηγούμενου σκορ…" : "Αποδέσμευση της προηγούμενης συμμετοχής…");
            if (context?.store?.id != transaction.storeId || context?.campaign?.id != transaction.campaignId)
            {
                KinoApiResult<KinoApiContext> selected = null;
                yield return api.Request<KinoApiContext>("PUT", "/device/session-store", new KinoApiSelectStoreRequest { campaign_id = transaction.campaignId, store_id = transaction.storeId }, r => selected = r);
                if (!Accept(selected))
                {
                    if (transaction.pendingScore && selected != null && !selected.Retryable && selected.Status >= 400 && selected.Status < 500 && selected.Status != 401)
                    { transaction.rejectionCode = selected.Code ?? "STORE_UNAVAILABLE"; Persist(); }
                    yield break;
                }
                if (selected.Value?.store?.id != transaction.storeId || selected.Value?.campaign?.id != transaction.campaignId) { Failure("INVALID_RESPONSE", true); yield break; }
                context = selected.Value;
            }
            if (transaction.starting)
            {
                // Recover the exact original registration; never start a different player after a timeout.
                KinoApiResult<KinoApiGameSession> started = null;
                yield return api.Request<KinoApiGameSession>("POST", "/game-sessions/start", new KinoApiStartRequest { registration_id = transaction.registrationId }, r => started = r);
                if (!Accept(started))
                {
                    if (started != null && !started.Retryable && started.Status >= 400 && started.Status < 500 && started.Status != 401) Archive();
                    yield break;
                }
                if (!ValidSession(started.Value, transaction.registrationId)) { Failure("INVALID_RESPONSE", true); yield break; }
                transaction.gameSessionId = started.Value.game_session_id; transaction.expiresAt = started.Value.expires_at;
                transaction.starting = false; transaction.needsAbandon = true;
                if (!Persist()) yield break;
            }
            if (transaction.pendingScore)
            {
                KinoApiResult<KinoApiCompleteResponse> sent = null;
                yield return api.Request<KinoApiCompleteResponse>("POST", "/game-sessions/complete", new KinoApiCompleteRequest {
                    game_session_id = transaction.gameSessionId, score = transaction.score, submission_id = transaction.submissionId }, r => sent = r);
                if (sent != null && sent.Success && sent.Value != null && sent.Value.success && !string.IsNullOrEmpty(sent.Value.score_id) && sent.Value.game_session_id == transaction.gameSessionId && sent.Value.score == transaction.score)
                { ResetError(); Archive(); yield break; }
                if (sent != null && !sent.Success && !sent.Retryable && sent.Status >= 400 && sent.Status < 500 && sent.Status != 401)
                { transaction.rejectionCode = sent.Code ?? "SCORE_REJECTED"; Persist(); ErrorCode = transaction.rejectionCode; DrawRejected(); yield break; }
                if (sent != null && sent.Success) Failure("INVALID_RESPONSE", true); else Accept(sent);
                yield break;
            }
            if (!string.IsNullOrEmpty(transaction.gameSessionId))
            {
                KinoApiResult<KinoApiGameSession> abandoned = null;
                yield return api.Request<KinoApiGameSession>("POST", "/game-sessions/" + Uri.EscapeDataString(transaction.gameSessionId) + "/abandon", null, r => abandoned = r);
                if (!Accept(abandoned))
                {
                    if (abandoned?.Status == 404) { ResetError(); Archive(); }
                    yield break;
                }
            }
            Archive();
        }
        bool Accept<T>(KinoApiResult<T> result) where T : class
        {
            if (result != null && result.Success && result.Value != null) { ResetError(); return true; }
            string code = result?.Code ?? "NETWORK_ERROR";
            if (code == "STORE_ACCESS_REVOKED" || code == "STORE_NOT_SELECTED" || code == "STORE_NOT_ALLOWED")
            {
                StoreReady = false;
                if (transaction == null) { campaign = null; region = null; store = null; campaigns = null; }
            }
            if (result?.Status == 401) { contextChecked = false; StoreReady = false; }
            Failure(code, result == null || result.Retryable, result?.RetryAfterSeconds ?? 0);
            return false;
        }
        static bool ValidSession(KinoApiGameSession value, string registrationId) => value != null &&
            value.status == "active" && !string.IsNullOrEmpty(value.game_session_id) && value.registration_id == registrationId;
        void Failure(string code, bool retryable, double delay = 0)
        {
            ErrorCode = code; failures++;
            terminalError = !retryable;
            serverRetryAt = Math.Max(serverRetryAt, Now + delay);
            retryAt = Math.Max(serverRetryAt, Now + Math.Min(60, Math.Pow(2, Math.Min(6, failures - 1))));
            status = retryable ? "Η σύνδεση διακόπηκε. Θα ξαναδοκιμάσουμε αυτόματα." :
                code == "PLAYER_ALREADY_LOCKED" || code == "REGISTRATION_ALREADY_PLAYED" || code == "REGISTRATION_CANCELLED" ? "Αυτή η συμμετοχή δεν είναι πλέον διαθέσιμη. Ανανέωσε τη λίστα." :
                code == "NO_ACTIVE_EVENT" || code == "EVENT_CLOSED" ? "Δεν υπάρχει ανοιχτό event. Ενημέρωσε το προσωπικό." :
                "Δεν ήταν δυνατή η συνέχεια. Ενημέρωσε το προσωπικό.";
            if (visible) DrawError();
        }
        void ResetError() { terminalError = false; ErrorCode = null; failures = 0; retryAt = 0; serverRetryAt = 0; }
        bool Persist()
        {
            try { journal.Save(transaction); return true; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { StorageError(e); return false; }
        }
        bool Archive()
        {
            try { journal.Archive(transaction); transaction = null; ActiveSession = null; recovering = false; return true; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { StorageError(e); return false; }
        }
        void StorageError(Exception error)
        { storageFailed = true; ErrorCode = "LOCAL_STORAGE_ERROR"; LastStorageError = error.GetType().Name + ": " + error.Message; }
        void Finish()
        {
            if (!visible) return;
            visible = false; panel?.Hide(); var action = completed; completed = null; action?.Invoke();
        }
        KinoBackendChoice Choice(string label, Action action, bool enabled = true)
        {
            int captured = generation;
            return new KinoBackendChoice(label, () => { if (visible && !Busy && captured == generation) action(); }, enabled);
        }
        void Show(string heading, string text, List<KinoBackendChoice> options)
        { if (visible && panel) panel.Show(heading, text, options, store != null ? store.name : campaign?.name); }
        void DrawWaiting(string text) => Show("KINO VR", text, new List<KinoBackendChoice>());
        void DrawCampaigns()
        {
            var options = campaigns.Select(c => Choice(c.name, () => ChooseCampaign(c.id))).ToList();
            if (options.Count == 0) options.Add(Choice("Ανανέωση", () => { campaigns = null; Retry(); }));
            Show("ΕΠΙΛΟΓΗ ΚΑΜΠΑΝΙΑΣ", options.Count == 1 && campaigns.Length == 0 ? "Δεν υπάρχουν διαθέσιμες καμπάνιες." : "Επίλεξε την καμπάνια της εκδήλωσης.", options);
        }
        void DrawRegions()
        {
            var regions = (campaign.stores ?? Array.Empty<KinoApiStore>()).Select(s => s.region ?? "").Distinct().OrderBy(s => s).ToArray();
            var options = regions.Select(r => Choice(string.IsNullOrEmpty(r) ? "Χωρίς περιοχή" : r, () => ChooseRegion(r))).ToList();
            options.Add(Choice("Πίσω στις καμπάνιες", () => { campaign = null; region = null; campaigns = null; }));
            Show("ΕΠΙΛΟΓΗ ΠΕΡΙΟΧΗΣ", regions.Length == 0 ? "Δεν υπάρχουν διαθέσιμα καταστήματα." : "Σε ποια περιοχή βρίσκεσαι;", options);
        }
        void DrawStores()
        {
            var options = (campaign.stores ?? Array.Empty<KinoApiStore>()).Where(s => (s.region ?? "") == region)
                .Select(s => Choice(s.name + " · " + s.code + (s.has_open_event ? "" : " — χωρίς event"), () => ChooseStore(s.id))).ToList();
            options.Add(Choice("Πίσω στις περιοχές", () => { region = null; }));
            Show("ΕΠΙΛΟΓΗ ΚΑΤΑΣΤΗΜΑΤΟΣ", "Επίλεξε το κατάστημα της μάσκας.", options);
        }
        void DrawPlayers()
        {
            if (!visible) return;
            if (operatorMenu)
            {
                Show("ΑΛΛΑΓΗ ΚΑΤΑΣΤΗΜΑΤΟΣ", "Για το προσωπικό της εκδήλωσης.", new List<KinoBackendChoice> {
                    Choice("Αλλαγή καταστήματος", ChangeStore), Choice("Επιστροφή στους παίκτες", () => { operatorMenu = false; DrawPlayers(); }) });
                return;
            }
            var options = Players.Select(p => Choice(p.nickname, () => ChoosePlayer(p.registration_id))).ToList();
            options.Add(Choice("Ανανέωση λίστας", () => { nextPoll = 0; }));
            options.Add(Choice("Ρυθμίσεις καταστήματος", () => { operatorMenu = true; DrawPlayers(); }));
            if (panel) panel.ShowPlayerSelection("ΕΠΙΛΕΞΕ ΤΟ ΟΝΟΜΑ ΣΟΥ", status, options, store != null ? store.name : campaign?.name);
        }
        void DrawError()
        {
            Show("ΣΥΝΔΕΣΗ", status,
                new List<KinoBackendChoice> { Choice("Δοκιμή ξανά", Retry) });
        }
        void DrawRejected()
        {
            Show("ΤΟ ΣΚΟΡ ΔΕΝ ΚΑΤΑΧΩΡΙΣΤΗΚΕ", "Ενημέρωσε το προσωπικό. Το αποτέλεσμα κρατήθηκε στη μάσκα.",
                new List<KinoBackendChoice> { Choice("Εντάξει — επόμενος παίκτης", AcknowledgeRejectedResult) });
        }
        void ShowPairing()
        {
            if (!visible || !panel) return;
            panel.ShowPairing(pairingCode, string.IsNullOrEmpty(ErrorCode) ? "Βάλε τον κωδικό από το admin: Devices → Pairing code." : "Η σύνδεση δεν ολοκληρώθηκε. Έλεγξε το δίκτυο και βάλε νέο pairing code.",
                key => { if (!Busy && pairingCode.Length < 8 && char.IsLetterOrDigit(key)) { pairingCode += char.ToUpperInvariant(key); ShowPairing(); } },
                () => { if (!Busy && pairingCode.Length > 0) { pairingCode = pairingCode.Substring(0, pairingCode.Length - 1); ShowPairing(); } },
                () => { if (!Busy && pairingCode.Length == 8) Run(Pair()); });
        }
        IEnumerator Pair()
        {
            DrawWaiting("Σύνδεση μάσκας…");
            KinoApiResult<KinoApiTokenPair> paired = null;
            yield return api.Activate(pairingCode, r => paired = r);
            pairingCode = "";
            if (Accept(paired))
            { contextChecked = false; campaigns = null; campaign = null; region = null; store = null; StoreReady = false; }
        }
        void OnDisable() { if (initialized) SuspendVisitor(); }
    }
}
