using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KinoVR
{
    // A direct-touch target for the tracked index fingertip.
    public sealed class KinoExperienceModeButton : MonoBehaviour
    {
        public KinoExperienceController experience;
        public bool includeBoost;
        public bool startSelectedMode;
        public Button button;
        public BoxCollider pressArea;
        float readyAt;
        readonly HashSet<Collider> releaseBeforePress = new HashSet<Collider>();
        public bool CanPress => gameObject.activeInHierarchy && experience && experience.isActiveAndEnabled &&
            experience.Stage == (startSelectedMode ? KinoExperienceStage.Standby : KinoExperienceStage.ModeSelection) && Time.unscaledTime >= readyAt;

        void Awake() { if (button) button.onClick.AddListener(Press); }
        void OnEnable() => releaseBeforePress.Clear();
        void OnDestroy() { if (button) button.onClick.RemoveListener(Press); }
        public void Show()
        {
            readyAt = Time.unscaledTime + .45f;
            button.interactable = false;
            gameObject.SetActive(true);
        }
        public void Hide() { if (button) button.interactable = false; gameObject.SetActive(false); }
        public void Press()
        {
            if (!CanPress) return;
            if (startSelectedMode) experience.StartSelectedSession(); else experience.SelectMode(includeBoost);
        }
        void OnTriggerEnter(Collider other)
        {
            if (!KinoFingerTip.IsFinger(other)) return;
            if (!KinoFingerTip.IsTouching(pressArea, other)) { releaseBeforePress.Remove(other); return; }
            if (releaseBeforePress.Contains(other)) return;
            if (CanPress) Press();
            releaseBeforePress.Add(other);
        }
        void OnTriggerExit(Collider other) => releaseBeforePress.Remove(other);
        void OnTriggerStay(Collider other) => OnTriggerEnter(other);
        void Update()
        {
            button.interactable = CanPress;
            if (!CanPress || !experience.round.playerView || !experience.round.playerView.desktopCamera ||
                !experience.round.playerView.desktopCamera.isActiveAndEnabled) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var ray = experience.round.playerView.desktopCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (pressArea.Raycast(ray, out _, 4)) Press();
        }
    }
}
