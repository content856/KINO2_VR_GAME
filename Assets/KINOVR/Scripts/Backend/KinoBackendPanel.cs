using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    public sealed class KinoBackendChoice
    {
        public string Label;
        public Action Selected;
        public bool Enabled = true;
        public bool IsUtility;

        public KinoBackendChoice(string label, Action selected, bool enabled = true, bool isUtility = false)
        {
            Label = label;
            Selected = selected;
            Enabled = enabled;
            IsUtility = isUtility;
        }
    }

    /// <summary>
    /// Runtime-only world UI for device setup and visitor selection. The caller owns all
    /// requests and transitions; backend strings are always displayed as plain text.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KinoBackendPanel : MonoBehaviour
    {
        const int PageSize = 5;
        const int BrandedPageSize = 4;
        const int PlayerPageSize = 3;
        const string KeyCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        static readonly Color Navy = new Color(.018f, .045f, .10f, .98f);
        static readonly Color Row = new Color(.035f, .105f, .18f, 1);
        static readonly Color Gold = new Color(.98f, .72f, .30f, 1);
        internal static readonly Color Ivory = new Color(1, .97f, .88f, 1);
        internal static readonly Color Muted = new Color(.53f, .65f, .74f, 1);
        readonly List<KinoBackendTouchButton> rows = new List<KinoBackendTouchButton>(PageSize);
        readonly List<KinoBackendTouchButton> keys = new List<KinoBackendTouchButton>(36);
        IReadOnlyList<KinoBackendChoice> currentChoices;
        Canvas canvas;
        Image background, topRule, headerDivider;
        GameObject playerDecoration;
        TMP_Text headingText, statusText, footerText, pageText, codeText;
        KinoBackendTouchButton previous, next, eraseButton, submitButton, retryButton, changePlayerButton;
        int page;
        bool pairing, readyActions, playerSkin, layoutInitialized, nameSelection;
        string currentHeading;
        float readyAt;
        Collider heldContact;
        BoxCollider heldArea;
        TMP_FontAsset bodyFont, headingFont;

        internal KinoExperienceController Experience { get; private set; }
        public bool IsVisible => canvas && canvas.gameObject.activeInHierarchy;
        internal bool CanPress => IsVisible && Experience && Experience.isActiveAndEnabled &&
            Time.unscaledTime >= readyAt && !heldContact;

        public void Initialize(KinoExperienceController experience)
        {
            if (!experience) throw new ArgumentNullException(nameof(experience));
            Experience = experience;
            if (canvas) return;
            bodyFont = experience.textFont ? experience.textFont : experience.body ? experience.body.font : TMP_Settings.defaultFontAsset;
            headingFont = experience.headingFont ? experience.headingFont : bodyFont;
            Build();
            Hide();
        }

        public void Show(string heading, string status, IReadOnlyList<KinoBackendChoice> choices, string footer = null)
            => ShowChoices(heading, status, choices, footer, true);

        /// <summary>All selection screens share the branded name-picker presentation.</summary>
        public void ShowPlayerSelection(string heading, string status, IReadOnlyList<KinoBackendChoice> choices, string footer = null)
            => ShowChoices(heading, status, choices, footer, true, true);

        void ShowChoices(string heading, string status, IReadOnlyList<KinoBackendChoice> choices, string footer, bool usePlayerSkin, bool spacePlayerChoices = false)
        {
            RequireInitialized();
            bool newScreen = !IsVisible || pairing || readyActions || playerSkin != usePlayerSkin || nameSelection != spacePlayerChoices || !string.Equals(currentHeading, heading, StringComparison.Ordinal);
            readyActions = false;
            nameSelection = spacePlayerChoices;
            if (pairing) layoutInitialized = false;
            ApplyPlayerSkin(usePlayerSkin);
            SetFullPanelVisible(true);
            changePlayerButton.Conceal();
            bool sameChoices = SameChoices(currentChoices, choices);
            if (newScreen || !sameChoices) page = 0;
            currentChoices = choices;
            pairing = false;
            currentHeading = heading;
            headingText.text = heading ?? string.Empty;
            statusText.text = status ?? string.Empty;
            footerText.text = footer ?? "Άγγιξε την επιλογή σου με τον δείκτη.";
            codeText.gameObject.SetActive(false);
            foreach (var key in keys) key.Conceal();
            eraseButton.Conceal(); submitButton.Conceal(); retryButton.Conceal();
            RenderPage();
            Present(newScreen);
        }

        public void ShowPairing(string code, string status, Action<char> key, Action erase, Action submit, Action retry = null)
        {
            RequireInitialized();
            bool newScreen = !IsVisible || !pairing || readyActions;
            readyActions = false;
            ApplyPlayerSkin(false);
            SetFullPanelVisible(true);
            changePlayerButton.Conceal();
            pairing = true;
            currentChoices = null;
            currentHeading = "ΣΥΝΔΕΣΗ QUEST";
            headingText.text = currentHeading;
            statusText.text = status ?? "Πληκτρολόγησε τον κωδικό από το admin.";
            Place(statusText.rectTransform, new Vector2(0, 147), new Vector2(554, 28));
            footerText.text = "Ο κωδικός σύνδεσης δημιουργείται από τον χειριστή.";
            codeText.gameObject.SetActive(true);
            codeText.text = string.IsNullOrEmpty(code) ? "— — — — — — — —" : code;
            foreach (var row in rows) row.Conceal();
            previous.Conceal(); next.Conceal(); pageText.gameObject.SetActive(false);
            for (int i = 0; i < keys.Count; i++)
            {
                char character = KeyCharacters[i];
                keys[i].Configure(character.ToString(), () => key?.Invoke(character), key != null);
                keys[i].Reveal();
            }
            eraseButton.Configure("ΣΒΗΣΙΜΟ", erase, erase != null && !string.IsNullOrEmpty(code));
            submitButton.Configure("ΣΥΝΔΕΣΗ", submit, submit != null && !string.IsNullOrEmpty(code));
            retryButton.Configure("ΞΑΝΑ", retry, retry != null);
            eraseButton.Reveal(); submitButton.Reveal();
            if (retry != null) retryButton.Reveal(); else retryButton.Conceal();
            Present(newScreen);
        }

        /// <summary>Optional correction beneath the existing Ready start button.</summary>
        public void ShowReadyActions(Action changePlayer)
        {
            RequireInitialized();
            bool newScreen = !IsVisible || !readyActions;
            readyActions = true;
            ApplyPlayerSkin(false);
            pairing = false;
            currentChoices = null;
            currentHeading = null;
            SetFullPanelVisible(false);
            foreach (var row in rows) row.Conceal();
            foreach (var key in keys) key.Conceal();
            previous.Conceal(); next.Conceal();
            eraseButton.Conceal(); submitButton.Conceal(); retryButton.Conceal();
            codeText.gameObject.SetActive(false);
            pageText.gameObject.SetActive(false);
            changePlayerButton.Configure("Αλλαγή παίκτη", changePlayer, changePlayer != null);
            changePlayerButton.SetAppearance(true, headingFont);
            changePlayerButton.Reveal();
            Present(newScreen);
        }

        void SetFullPanelVisible(bool visible)
        {
            background.gameObject.SetActive(visible && !playerSkin);
            topRule.gameObject.SetActive(visible && !playerSkin);
            headerDivider.gameObject.SetActive(visible && !playerSkin);
            playerDecoration.SetActive(visible && playerSkin);
            headingText.gameObject.SetActive(visible);
            statusText.gameObject.SetActive(visible);
            footerText.gameObject.SetActive(visible);
        }

        void ApplyPlayerSkin(bool enabled)
        {
            if (layoutInitialized && playerSkin == enabled) return;
            layoutInitialized = true;
            playerSkin = enabled;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(620, enabled ? 620 : 520);
            Place(headingText.rectTransform, new Vector2(0, enabled ? 70 : 225), new Vector2(554, enabled ? 34 : 44));
            headingText.fontSize = enabled ? 26 : 29;
            headingText.color = enabled ? Ivory : Gold;
            Place(statusText.rectTransform, new Vector2(0, enabled ? 33 : 173), new Vector2(554, enabled ? 40 : 52));
            statusText.fontSize = enabled ? 19 : 21;
            statusText.color = enabled ? new Color(.66f, .78f, .92f) : Ivory;
            Place(footerText.rectTransform, new Vector2(0, enabled ? -285 : -246), new Vector2(570, 25));
            Place(pageText.rectTransform, new Vector2(0, enabled ? -250 : -207), new Vector2(100, 40));
            for (int i = 0; i < rows.Count; i++)
            {
                PositionButton(rows[i], new Vector2(0, enabled ? -15 - i * 58 : 111 - i * 59), new Vector2(554, enabled ? 49 : 51));
                rows[i].SetAppearance(enabled, headingFont, enabled);
            }
            PositionButton(previous, new Vector2(-185, enabled ? -250 : -207), new Vector2(178, 40));
            PositionButton(next, new Vector2(185, enabled ? -250 : -207), new Vector2(178, 40));
            previous.SetAppearance(enabled, headingFont);
            next.SetAppearance(enabled, headingFont);
        }

        static void PositionButton(KinoBackendTouchButton button, Vector2 position, Vector2 size)
        {
            Place((RectTransform)button.transform, position, size);
            button.PressArea.size = new Vector3(size.x, size.y, 32);
        }

        public void SetStatus(string text)
        {
            if (statusText) statusText.text = text ?? string.Empty;
        }

        public void Hide()
        {
            if (canvas) canvas.gameObject.SetActive(false);
            // Keep the release latch: a callback can hide and immediately replace the screen.
        }

        void RequireInitialized()
        {
            if (!canvas) throw new InvalidOperationException("Initialize the backend panel before showing it.");
        }

        static bool SameChoices(IReadOnlyList<KinoBackendChoice> first, IReadOnlyList<KinoBackendChoice> second)
        {
            if (first == null || second == null) return first == second;
            if (first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
            {
                if (first[i] == null || second[i] == null)
                {
                    if (first[i] != second[i]) return false;
                }
                else if (!string.Equals(first[i].Label, second[i].Label, StringComparison.Ordinal) || first[i].Enabled != second[i].Enabled || first[i].IsUtility != second[i].IsUtility)
                    return false;
            }
            return true;
        }

        void RenderPage()
        {
            int count = currentChoices?.Count ?? 0;
            int pageSize = nameSelection ? PlayerPageSize : playerSkin ? BrandedPageSize : PageSize;
            int pages = Mathf.Max(1, (count + pageSize - 1) / pageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            int visibleUtilities = 0, utilityIndex = 0;
            if (nameSelection)
                for (int i = page * pageSize; i < Mathf.Min(count, (page + 1) * pageSize); i++)
                    if (currentChoices[i] != null && currentChoices[i].IsUtility) visibleUtilities++;
            for (int i = 0; i < rows.Count; i++)
            {
                int index = page * pageSize + i;
                if (i >= pageSize || index >= count || currentChoices[index] == null) { rows[i].Conceal(); continue; }
                var choice = currentChoices[index];
                // Keep utility actions at the bottom even with no registered players.
                // Three rows leave at least 61 mm between names and these controls.
                float y = playerSkin ? -15 - i * (nameSelection ? 70 : 58) : 111 - i * 59;
                if (nameSelection && choice.IsUtility) y = -195 + (visibleUtilities - 1 - utilityIndex++) * 70;
                PositionButton(rows[i], new Vector2(0, y), new Vector2(554, playerSkin ? 49 : 51));
                // Resolve the current list at click time, including callbacks refreshed by polling.
                rows[i].Configure(choice.Label, () => Select(index), choice.Enabled);
                rows[i].Reveal();
            }
            pageText.text = (page + 1) + " / " + pages;
            pageText.gameObject.SetActive(pages > 1);
            previous.Configure("ΠΙΣΩ", () => ChangePage(-1), page > 0);
            next.Configure("ΕΠΟΜΕΝΑ", () => ChangePage(1), page < pages - 1);
            if (pages > 1) { previous.Reveal(); next.Reveal(); }
            else { previous.Conceal(); next.Conceal(); }
        }

        void Select(int index)
        {
            if (pairing || currentChoices == null || index < 0 || index >= currentChoices.Count) return;
            var choice = currentChoices[index];
            if (choice != null && choice.Enabled) choice.Selected?.Invoke();
        }

        void ChangePage(int delta)
        {
            page += delta;
            RenderPage();
        }

        void Present(bool reposition)
        {
            if (reposition)
            {
                readyAt = Time.unscaledTime + .45f;
                var view = Experience.round && Experience.round.playerView ? Experience.round.playerView.View : null;
                if (view)
                {
                    var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
                    if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
                    var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                    var position = view.position + rotation * Experience.TouchOffset;
                    if (readyActions) position += Vector3.down * .13f;
                    canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - view.position, Vector3.up));
                    canvas.worldCamera = view.GetComponent<Camera>();
                    var desktop = Experience.round.playerView.desktopCamera;
                    if (desktop && desktop.isActiveAndEnabled && view == desktop.transform)
                        PositionForDesktop(desktop);
                }
            }
            canvas.gameObject.SetActive(true);
        }

        void PositionForDesktop(Camera camera)
        {
            // Desktop cannot look down like a headset. Fit the world controls to its
            // current viewport without changing the camera or the Quest reach layout.
            float tangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            if (!readyActions)
            {
                var rect = (RectTransform)canvas.transform;
                var scale = canvas.transform.lossyScale;
                float width = rect.rect.width * Mathf.Abs(scale.x);
                float height = rect.rect.height * Mathf.Abs(scale.y);
                float distance = Mathf.Max(.65f, height / (2 * tangent * .84f),
                    width / (2 * tangent * Mathf.Max(.25f, camera.aspect) * .84f));
                canvas.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward * distance, camera.transform.rotation);
                return;
            }

            float top = .16f;
            if (Experience.startButton && Experience.startButton.transform is RectTransform startRect)
            {
                var corners = new Vector3[4];
                startRect.GetWorldCorners(corners);
                foreach (var corner in corners) top = Mathf.Min(top, camera.WorldToViewportPoint(corner).y - .015f);
            }
            const float bottom = .015f;
            float availableHeight = Mathf.Max(.035f, top - bottom);
            float buttonHeight = 46 * Mathf.Abs(canvas.transform.lossyScale.y);
            float depth = Mathf.Max(.65f, buttonHeight / (2 * tangent * Mathf.Min(.075f, availableHeight)));
            float viewportHeight = buttonHeight / (2 * depth * tangent);
            var position = camera.ViewportToWorldPoint(new Vector3(.5f, bottom + viewportHeight * .5f, depth));
            canvas.transform.SetPositionAndRotation(position, camera.transform.rotation);
        }

        internal bool TryBeginPress(KinoBackendTouchButton source, Collider contact)
        {
            if (!CanPress) return false;
            readyAt = Time.unscaledTime + .3f;
            if (contact)
            {
                heldContact = contact;
                heldArea = source.PressArea;
            }
            return true;
        }

        void Update()
        {
            if (!heldContact) { heldArea = null; return; }
            if (!heldContact.enabled || !heldContact.gameObject.activeInHierarchy || !heldArea || !StillTouching())
            {
                heldContact = null;
                heldArea = null;
            }
        }

        bool StillTouching()
        {
            // Uses local shape data, which remains valid while the button is hidden.
            return KinoBackendTouchButton.IsTouching(heldArea, heldContact, .012f);
        }

        void Build()
        {
            var root = new GameObject("Quest connection and player selection", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            root.transform.localScale = Vector3.one * .001f;
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;
            ((RectTransform)root.transform).sizeDelta = new Vector2(620, 520);
            background = Image("Panel", canvas.transform, Vector2.zero, new Vector2(620, 520), Navy);
            topRule = Image("Gold top rule", canvas.transform, new Vector2(0, 257), new Vector2(620, 4), Gold);
            headerDivider = Image("Header divider", canvas.transform, new Vector2(0, 199), new Vector2(554, 1), new Color(.3f, .4f, .5f, .45f));
            headingText = Label("Heading", canvas.transform, new Vector2(0, 225), new Vector2(554, 44), 29, headingFont, Gold);
            statusText = Label("Status", canvas.transform, new Vector2(0, 173), new Vector2(554, 52), 21, bodyFont, Ivory);
            statusText.textWrappingMode = TextWrappingModes.Normal;
            footerText = Label("Footer", canvas.transform, new Vector2(0, -246), new Vector2(570, 25), 17, bodyFont, Muted);
            codeText = Label("Pairing code", canvas.transform, new Vector2(0, 177), new Vector2(554, 40), 30, headingFont, Ivory);
            codeText.characterSpacing = 5;
            pageText = Label("Page", canvas.transform, new Vector2(0, -207), new Vector2(100, 40), 23, bodyFont, Ivory);
            for (int i = 0; i < PageSize; i++)
                rows.Add(CreateButton("Choice " + (i + 1), new Vector2(0, 111 - i * 59), new Vector2(554, 51), 26));
            previous = CreateButton("Previous page", new Vector2(-185, -207), new Vector2(178, 40), 21);
            next = CreateButton("Next page", new Vector2(185, -207), new Vector2(178, 40), 21);
            for (int i = 0; i < KeyCharacters.Length; i++)
                keys.Add(CreateButton("Pairing key " + i, new Vector2((i % 6 - 2.5f) * 92, 109 - i / 6 * 55), new Vector2(82, 46), 27));
            eraseButton = CreateButton("Erase code", new Vector2(-196, -215), new Vector2(165, 40), 21);
            retryButton = CreateButton("Retry pairing", new Vector2(0, -215), new Vector2(165, 40), 21);
            submitButton = CreateButton("Submit code", new Vector2(196, -215), new Vector2(165, 40), 21);
            changePlayerButton = CreateButton("Change selected player", Vector2.zero, new Vector2(240, 46), 23);
            BuildPlayerDecoration();
            foreach (var row in rows) row.Conceal();
            foreach (var key in keys) key.Conceal();
            previous.Conceal(); next.Conceal(); eraseButton.Conceal(); submitButton.Conceal(); retryButton.Conceal();
            changePlayerButton.Conceal();
        }

        void BuildPlayerDecoration()
        {
            playerDecoration = new GameObject("Branded player picker", typeof(RectTransform));
            playerDecoration.transform.SetParent(canvas.transform, false);
            playerDecoration.transform.SetAsFirstSibling();
            Place((RectTransform)playerDecoration.transform, Vector2.zero, new Vector2(620, 620));
            var face = new GameObject("Navy wave frame", typeof(RectTransform), typeof(KinoBackendSelectionGraphic));
            face.transform.SetParent(playerDecoration.transform, false);
            Place((RectTransform)face.transform, Vector2.zero, new Vector2(620, 620));
            var graphic = face.GetComponent<KinoBackendSelectionGraphic>();
            graphic.background = true;
            graphic.raycastTarget = false;
            var kino = Experience.brandLogo ? Experience.brandLogo.texture : Experience.kinoLogo ? Experience.kinoLogo.texture : null;
            var vr = Experience.vrExperienceLogo ? Experience.vrExperienceLogo.texture : null;
            BrandLogo("KINO", kino, new Vector2(0, 232), 172);
            BrandLogo("VR EXPERIENCE", vr, new Vector2(0, 137), 214);
            playerDecoration.SetActive(false);
        }

        void BrandLogo(string name, Texture texture, Vector2 position, float width)
        {
            if (!texture) return;
            var logo = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            logo.transform.SetParent(playerDecoration.transform, false);
            Place(logo.rectTransform, position, new Vector2(width, width * texture.height / texture.width));
            logo.texture = texture;
            logo.raycastTarget = false;
        }

        KinoBackendTouchButton CreateButton(string name, Vector2 position, Vector2 size, float fontSize)
        {
            var face = Image(name, canvas.transform, position, size, Row);
            Image("Accent", face.transform, new Vector2(-size.x * .5f + 2, 0), new Vector2(3, size.y), Gold);
            var label = Label("Label", face.transform, Vector2.zero, size - new Vector2(24, 4), fontSize, bodyFont, Ivory);
            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.1f, 1);
            colors.pressedColor = new Color(.70f, .77f, .85f, 1);
            colors.disabledColor = new Color(.65f, .70f, .76f, 1);
            colors.fadeDuration = .08f;
            button.colors = colors;
            var area = face.gameObject.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.size = new Vector3(size.x, size.y, 32);
            var rigidbody = face.gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            var control = face.gameObject.AddComponent<KinoBackendTouchButton>();
            control.Initialize(this, button, label, area);
            return control;
        }

        static Image Image(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            Place(image.rectTransform, position, size);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, float fontSize, TMP_FontAsset font, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            Place(label.rectTransform, position, size);
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = false;
            label.raycastTarget = false;
            label.color = color;
            return label;
        }

        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
