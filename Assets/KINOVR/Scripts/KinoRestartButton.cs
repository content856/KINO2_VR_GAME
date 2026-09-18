using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KinoVR
{
    // A stationary, reachable world-space button using the same hands as ball catching.
    public sealed class KinoRestartButton : MonoBehaviour
    {
        public KinoRoundController round;
        public Button button;
        public BoxCollider pressArea;
        [Tooltip("Metres from the player's head: left/right, height, forward. Placed once when the round ends.")]
        public Vector3 viewOffset = new Vector3(-.32f, -.25f, .65f);
        public bool IsVisible => gameObject.activeInHierarchy;
        public bool CanPress => IsVisible && round && round.isActiveAndEnabled &&
            round.State.Phase == KinoRoundPhase.Complete && !round.IsRunning &&
            Time.unscaledTime >= readyAt;
        float readyAt;

        void Awake() => button.onClick.AddListener(Press);
        void OnDestroy() { if (button) button.onClick.RemoveListener(Press); }

        public void Show()
        {
            if (!round || !round.isActiveAndEnabled || round.IsRunning || round.State.Phase != KinoRoundPhase.Complete) return;
            var view = round.playerView ? round.playerView.View : null;
            if (!view) return;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) forward = Vector3.ProjectOnPlane(round.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            var heading = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 position = view.position + heading * viewOffset;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - view.position, Vector3.up));
            // Do not turn the final catch, or an already-held click, into an accidental restart.
            readyAt = Time.unscaledTime + .35f;
            button.interactable = false;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            button.interactable = false;
            gameObject.SetActive(false);
        }

        public void Press()
        {
            if (!CanPress) return;
            Hide();
            round.BeginRound();
        }

        void OnTriggerEnter(Collider other)
        {
            var hand = other.GetComponentInParent<HandCatcher>();
            if (CanPress && hand && hand.isActiveAndEnabled) button.onClick.Invoke();
        }

        void Update()
        {
            button.interactable = CanPress;
            if (!CanPress || !round.playerView || !round.playerView.desktopCamera ||
                !round.playerView.desktopCamera.isActiveAndEnabled) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            Ray ray = round.playerView.desktopCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (pressArea.Raycast(ray, out _, 10)) button.onClick.Invoke();
        }
    }
}
