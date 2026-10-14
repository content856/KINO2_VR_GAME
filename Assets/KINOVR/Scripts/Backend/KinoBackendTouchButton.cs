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
        Action selected;
        bool available;
        float readyAt;
        readonly HashSet<Collider> releaseBeforePress = new HashSet<Collider>();
        internal BoxCollider PressArea { get; private set; }

        internal void Initialize(KinoBackendPanel owner, Button target, TMP_Text caption, BoxCollider area)
        {
            panel = owner;
            button = target;
            label = caption;
            PressArea = area;
            button.onClick.AddListener(PressFromPointer);
        }

        internal void Configure(string text, Action action, bool enabled)
        {
            label.text = text ?? string.Empty;
            selected = action;
            available = enabled && action != null;
            label.color = available ? KinoBackendPanel.Ivory : KinoBackendPanel.Muted;
            button.interactable = CanPress;
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
            if (button) button.interactable = CanPress;
            if (!CanPress) return;
            var experience = panel.Experience;
            var view = experience && experience.round ? experience.round.playerView : null;
            if (!view || !view.desktopCamera || !view.desktopCamera.isActiveAndEnabled) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var ray = view.desktopCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (PressArea.Raycast(ray, out _, 4)) Press(null);
        }

        void OnDestroy()
        {
            if (button) button.onClick.RemoveListener(PressFromPointer);
        }
    }
}
