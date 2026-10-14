using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KinoVR
{
    // A direct-touch target, using the same tracked hands as the ball catches.
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
            var hand = other.GetComponentInParent<HandCatcher>();
            if (!hand || !hand.isActiveAndEnabled) return;
            if (startSelectedMode && experience && experience.LeaderboardEnabled)
            {
                if (!TouchInside(other)) { releaseBeforePress.Remove(other); return; }
                if (releaseBeforePress.Contains(other)) return;
                if (CanPress) Press();
                releaseBeforePress.Add(other);
            }
            else if (CanPress) Press();
        }
        void OnTriggerExit(Collider other) => releaseBeforePress.Remove(other);
        void OnTriggerStay(Collider other)
        {
            // The catch sphere is wider than a button. Use the palm centre for the online menu,
            // where a separate Change player action sits below Start.
            if (startSelectedMode && experience && experience.LeaderboardEnabled) OnTriggerEnter(other);
        }
        bool TouchInside(Collider other)
        {
            if (!startSelectedMode || !experience || !experience.LeaderboardEnabled || !pressArea) return true;
            var local = pressArea.transform.InverseTransformPoint(other.bounds.center) - pressArea.center;
            return Mathf.Abs(local.x) <= pressArea.size.x * .5f && Mathf.Abs(local.y) <= pressArea.size.y * .5f &&
                Mathf.Abs(local.z * pressArea.transform.lossyScale.z) <= .04f;
        }
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
