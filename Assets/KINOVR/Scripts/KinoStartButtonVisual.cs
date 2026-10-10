using TMPro;
using UnityEngine;

namespace KinoVR
{
    // Look and feel of the ΞΕΚΙΝΑ touch button: a glowing KINO-blue glass pill that breathes
    // while waiting, brightens and grows as a tracked hand approaches, and bursts into sparks
    // when pressed. Pressing still goes through KinoExperienceModeButton.
    [DisallowMultipleComponent]
    public sealed class KinoStartButtonVisual : MonoBehaviour
    {
        public KinoExperienceModeButton button;
        public KinoGlassPanel face;
        public KinoGlassPanel halo;
        public TMP_Text label, hint;
        [Tooltip("Hands closer than this (metres) start to light the button.")]
        [Min(.05f)] public float approachDistance = .28f;
        public Color idleFill = new Color(0, .2f, .55f, .82f);
        public Color hotFill = new Color(.05f, .42f, 1f, .92f);
        public Color edge = new Color(.4f, .78f, 1f, 1f);

        HandCatcher[] hands;
        float nextHandSearch, proximity, armed;
        Vector3 lastPosition;

        void OnEnable()
        {
            armed = 0;
            proximity = 0;
            FindHands();
        }

        void FindHands()
        {
            hands = FindObjectsByType<HandCatcher>(FindObjectsSortMode.None);
            nextHandSearch = Time.unscaledTime + 2;
        }

        void LateUpdate()
        {
            if (!face) return;
            if (Time.unscaledTime > nextHandSearch) FindHands();
            float dt = Time.unscaledDeltaTime;
            bool canPress = button && button.CanPress;
            armed = Mathf.MoveTowards(armed, canPress ? 1 : 0, dt / .35f);

            float nearest = float.MaxValue;
            var centre = button && button.pressArea ? button.pressArea.bounds.center : transform.position;
            lastPosition = centre;
            if (hands != null)
                foreach (var hand in hands)
                    if (hand && hand.isActiveAndEnabled) nearest = Mathf.Min(nearest, Vector3.Distance(hand.transform.position, centre));
            float target = canPress ? Mathf.Clamp01(1 - (nearest - .04f) / Mathf.Max(.01f, approachDistance)) : 0;
            proximity = Mathf.MoveTowards(proximity, target, dt / .15f);

            // Breathing glow while waiting, stronger as a hand approaches.
            float breathe = .5f + .5f * Mathf.Sin(Time.unscaledTime * 2.4f);
            float heat = Mathf.Max(proximity, 0);
            face.fill = Color.Lerp(idleFill, hotFill, heat);
            face.edge = edge;
            face.glowAlpha = Mathf.Lerp(.35f, .9f, Mathf.Max(heat, breathe * .35f)) * Mathf.Lerp(.4f, 1, armed);
            face.color = new Color(1, 1, 1, Mathf.Lerp(.55f, 1, armed));
            face.SetVerticesDirty();
            if (halo)
            {
                halo.glowAlpha = (.18f + .3f * breathe + .5f * heat) * armed;
                halo.rectTransform.localScale = Vector3.one * (1.04f + .06f * breathe + .05f * heat);
                halo.SetVerticesDirty();
            }
            transform.localScale = Vector3.one * (1 + .07f * heat);
            if (label) label.color = Color.Lerp(new Color(.82f, .9f, 1f, Mathf.Lerp(.6f, 1, armed)), Color.white, heat);
            if (hint) hint.alpha = Mathf.Lerp(.45f, .85f, armed) * (1 - .4f * heat);
        }

        void OnDisable()
        {
            // The button hides the moment the session starts: celebrate the press.
            if (button && button.experience && button.experience.Stage == KinoExperienceStage.Startup &&
                KinoCatchBurst.Instance && KinoCatchBurst.Instance.isActiveAndEnabled)
                KinoCatchBurst.Instance.Emit(lastPosition, Catchable.BallType.MoreWins);
            transform.localScale = Vector3.one;
        }
    }
}
