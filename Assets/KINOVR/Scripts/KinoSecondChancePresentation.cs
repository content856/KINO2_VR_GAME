using UnityEngine;

namespace KinoVR
{
    // The announcement stays fixed at reading distance while the enclosure hides
    // the room in every direction. The draw clock also drives its spherical fade.
    public sealed class KinoSecondChancePresentation : MonoBehaviour
    {
        public KinoPlayerView playerView;
        public GameObject announcement;
        public GameObject activeHeader;
        public GameObject normalBrand;
        public KinoBlackEnclosure enclosure;
        public Canvas announcementCanvas;
        public KinoBoostPresentation roomTreatment;
        bool blueRoom, revealing;
        public float FadeAlpha { get; private set; }

        public void Present(KinoRoundState state, double now)
        {
            var phase = state.Phase;
            bool reveal = phase == KinoRoundPhase.SecondChanceReveal;
            bool active = phase == KinoRoundPhase.SecondChance;
            if (reveal && !revealing) PositionAnnouncement();
            revealing = reveal;
            if (enclosure) enclosure.SetRoundBackground(reveal ? 1 : 0);
            bool blue = reveal || active;
            if (blueRoom != blue) { blueRoom = blue; if (roomTreatment) roomTreatment.SetSecondChanceLighting(blue); }
            if (announcement) announcement.SetActive(reveal);
            if (activeHeader) activeHeader.SetActive(active);
            if (normalBrand) normalBrand.SetActive(!reveal && !active && phase != KinoRoundPhase.Boost && phase != KinoRoundPhase.BoostSettling);
            float elapsed = (float)(now - state.PhaseStartedAt);
            SetFade(phase == KinoRoundPhase.FadeOut ? Mathf.Clamp01(elapsed / KinoRoundState.FadeSeconds) :
                reveal ? 1 - Mathf.Clamp01(elapsed / KinoRoundState.FadeSeconds) : 0);
        }
        void SetFade(float alpha)
        {
            FadeAlpha = Mathf.Clamp01(alpha);
            if (enclosure) enclosure.SetRoundFade(FadeAlpha);
        }
        void PositionAnnouncement()
        {
            if (!playerView || !playerView.View || !announcementCanvas) return;
            var view = playerView.View;
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            var heading = Quaternion.LookRotation(forward.normalized);
            announcementCanvas.transform.SetPositionAndRotation(view.position + heading * new Vector3(0, 0, 2.5f), heading);
            announcementCanvas.worldCamera = view.GetComponent<Camera>();
        }
        public void ResetPresentation()
        {
            if (blueRoom && roomTreatment) roomTreatment.SetSecondChanceLighting(false);
            blueRoom = false;
            revealing = false;
            if (enclosure) enclosure.SetRoundBackground(0);
            if (announcement) announcement.SetActive(false);
            if (activeHeader) activeHeader.SetActive(false);
            if (normalBrand) normalBrand.SetActive(true);
            SetFade(0);
        }
        void OnDisable() => ResetPresentation();
    }
}
