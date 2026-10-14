using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KinoVR
{
    /// <summary>A backend menu target for tracked-hand touch and the Editor mouse.</summary>
    [DisallowMultipleComponent]
    public sealed class KinoBackendTouchButton : MonoBehaviour
    {
        KinoBackendPanel panel;
        Button button;
        TMP_Text label;
        Image originalFace;
        Transform originalAccent;
        KinoBackendSelectionGraphic selectionFace;
        TMP_FontAsset originalFont;
        Action selected;
        bool available, polished, hovered, appearanceInitialized;
        float readyAt;
        float pressedUntil;
        readonly HashSet<Collider> releaseBeforePress = new HashSet<Collider>();
        internal BoxCollider PressArea { get; private set; }

        internal void Initialize(KinoBackendPanel owner, Button target, TMP_Text caption, BoxCollider area)
        {
            panel = owner;
            button = target;
            label = caption;
            originalFont = caption.font;
            originalFace = target.targetGraphic as Image;
            originalAccent = transform.Find("Accent");
            PressArea = area;
            button.onClick.AddListener(PressFromPointer);
        }

        internal void Configure(string text, Action action, bool enabled)
        {
            var caption = text ?? string.Empty;
            if (label.text != caption) label.text = caption;
            selected = action;
            available = enabled && action != null;
            label.color = available ? KinoBackendPanel.Ivory : KinoBackendPanel.Muted;
            button.interactable = CanPress;
            RefreshAppearance();
        }

        internal void SetAppearance(bool useSelectionStyle, TMP_FontAsset font = null, bool arrow = false)
        {
            var desiredFont = useSelectionStyle && font ? font : originalFont;
            if (appearanceInitialized && polished == useSelectionStyle && label.font == desiredFont &&
                (!selectionFace || selectionFace.showArrow == arrow)) return;
            appearanceInitialized = true;
            polished = useSelectionStyle;
            if (polished && !selectionFace)
            {
                var go = new GameObject("Rounded selection surface", typeof(RectTransform), typeof(KinoBackendSelectionGraphic));
                go.transform.SetParent(transform, false);
                go.transform.SetAsFirstSibling();
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                selectionFace = go.GetComponent<KinoBackendSelectionGraphic>();
                selectionFace.raycastTarget = false;
            }
            if (originalFace) originalFace.enabled = !polished;
            if (originalAccent) originalAccent.gameObject.SetActive(!polished);
            if (selectionFace)
            {
                selectionFace.gameObject.SetActive(polished);
                if (selectionFace.showArrow != arrow) { selectionFace.showArrow = arrow; selectionFace.SetVerticesDirty(); }
            }
            button.transition = polished ? Selectable.Transition.None : Selectable.Transition.ColorTint;
            button.targetGraphic = polished ? (Graphic)selectionFace : originalFace;
            label.font = desiredFont;
            var size = ((RectTransform)transform).sizeDelta;
            label.rectTransform.sizeDelta = size - new Vector2(polished && arrow ? 64 : 24, 4);
            label.rectTransform.anchoredPosition = polished && arrow ? new Vector2(-7, 0) : Vector2.zero;
            RefreshAppearance();
        }

        void RefreshAppearance()
        {
            if (selectionFace) selectionFace.SetState(available, hovered && available, Time.unscaledTime < pressedUntil);
        }

        internal void Reveal()
        {
            if (gameObject.activeSelf) return;
            readyAt = Time.unscaledTime + .45f;
            gameObject.SetActive(true);
        }

        internal void Conceal() => gameObject.SetActive(false);

        bool CanPress => available && panel && panel.CanPress && gameObject.activeInHierarchy &&
            Time.unscaledTime >= readyAt;

        void PressFromPointer() => Press(null);

        void Press(Collider contact)
        {
            if (!CanPress || !panel.TryBeginPress(this, contact)) return;
            pressedUntil = Time.unscaledTime + .3f;
            RefreshAppearance();
            // Take a copy: the callback may replace this row, change screens, or hide the panel.
            var callback = selected;
            callback?.Invoke();
        }

        void OnTriggerEnter(Collider other)
        {
            Touch(other);
        }

        void OnTriggerStay(Collider other)
        {
            Touch(other);
        }

        void OnTriggerExit(Collider other)
        {
            releaseBeforePress.Remove(other);
        }

        void Touch(Collider other)
        {
            var hand = other.GetComponentInParent<HandCatcher>();
            if (!hand || !hand.isActiveAndEnabled) return;
            if (!IsTouching(PressArea, other)) { releaseBeforePress.Remove(other); return; }
            // Existing catch spheres have a 10 cm radius. Test the palm centre against
            // this row so their broad overlap cannot select a neighbouring row or key.
            if (releaseBeforePress.Contains(other)) return;
            if (CanPress) Press(other);
            // Also require a fresh touch if a hand was already inside while loading,
            // disabled, or while another hand's press was being processed.
            releaseBeforePress.Add(other);
        }

        internal static bool IsTouching(BoxCollider area, Collider contact, float extraMetres = 0)
        {
            if (!area || !contact) return false;
            var sphere = contact as SphereCollider;
            var center = sphere ? sphere.transform.TransformPoint(sphere.center) : contact.bounds.center;
            var local = area.transform.InverseTransformPoint(center) - area.center;
            var scale = area.transform.lossyScale;
            float paddingX = extraMetres / Mathf.Max(.00001f, Mathf.Abs(scale.x));
            float paddingY = extraMetres / Mathf.Max(.00001f, Mathf.Abs(scale.y));
            float paddingZ = (.025f + extraMetres) / Mathf.Max(.00001f, Mathf.Abs(scale.z));
            return Mathf.Abs(local.x) <= area.size.x * .5f + paddingX &&
                Mathf.Abs(local.y) <= area.size.y * .5f + paddingY &&
                Mathf.Abs(local.z) <= area.size.z * .5f + paddingZ;
        }

        void Update()
        {
            if (!panel) return;
            if (button) button.interactable = CanPress;
            var experience = panel.Experience;
            var view = experience && experience.round ? experience.round.playerView : null;
            hovered = false;
            if (!view || !view.desktopCamera || !view.desktopCamera.isActiveAndEnabled)
            {
                RefreshAppearance();
                return;
            }
            var mouse = Mouse.current;
            if (mouse == null) { RefreshAppearance(); return; }
            var ray = view.desktopCamera.ScreenPointToRay(mouse.position.ReadValue());
            hovered = PressArea.Raycast(ray, out _, 4);
            RefreshAppearance();
            if (CanPress && hovered && mouse.leftButton.wasPressedThisFrame) Press(null);
        }

        void OnDestroy()
        {
            if (button) button.onClick.RemoveListener(PressFromPointer);
        }
    }
}
