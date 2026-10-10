using UnityEngine;

namespace KinoVR
{
    // The announcement occupies the board's number field over a dimmed room.
    // The draw clock still drives the complete spherical transition fade.
    public sealed class KinoSecondChancePresentation : MonoBehaviour
    {
        public KinoPlayerView playerView;
        public KinoNumberBoard board;
        public GameObject announcement;
        public GameObject activeHeader;
        public GameObject normalBrand;
        public KinoBlackEnclosure enclosure;
        public Canvas announcementCanvas;
        public KinoBoostPresentation roomTreatment;
        bool secondChanceRoom, revealing;
        public float FadeAlpha { get; private set; }

        public void Present(KinoRoundState state, double now)
        {
            var phase = state.Phase;
            bool reveal = phase == KinoRoundPhase.SecondChanceReveal;
            bool active = phase == KinoRoundPhase.SecondChance;
            if (board) board.SetSecondChanceCover(reveal);
            if (reveal && !revealing) PositionAnnouncement();
            revealing = reveal;
            if (enclosure) enclosure.SetRoundBackground(reveal ? .15f : 0);
            bool green = reveal || active;
            if (secondChanceRoom != green) { secondChanceRoom = green; if (roomTreatment) roomTreatment.SetSecondChanceLighting(green); }
            if (announcement) announcement.SetActive(reveal);
            if (activeHeader) activeHeader.SetActive(active);
            if (normalBrand) normalBrand.SetActive(!active && phase != KinoRoundPhase.Boost && phase != KinoRoundPhase.BoostSettling);
            float elapsed = (float)(now - state.PhaseStartedAt);
            // Fake-out: a slow fade to black that then holds, then a quicker fade back in on the announcement.
            float fadeOut = Mathf.SmoothStep(0, 1, elapsed / state.FadeOutDuration);
            float fadeIn = 1 - Mathf.SmoothStep(0, 1, elapsed / state.RevealFadeDuration);
            SetFade(phase == KinoRoundPhase.FadeOut ? fadeOut : reveal ? fadeIn : 0);
        }
        void SetFade(float alpha)
        {
            FadeAlpha = Mathf.Clamp01(alpha);
            if (enclosure) enclosure.SetRoundFade(FadeAlpha);
        }
        void PositionAnnouncement()
        {
            KinoBoardOverlay.Place(announcementCanvas, board, KinoBoardOverlay.ArtworkSize);
            if (announcementCanvas && playerView && playerView.View)
                announcementCanvas.worldCamera = playerView.View.GetComponent<Camera>();
        }
        public void ResetPresentation()
        {
            if (secondChanceRoom && roomTreatment) roomTreatment.SetSecondChanceLighting(false);
            secondChanceRoom = false;
            revealing = false;
            if (board) board.SetSecondChanceCover(false);
            if (enclosure) enclosure.SetRoundBackground(0);
            if (announcement) announcement.SetActive(false);
            if (activeHeader) activeHeader.SetActive(false);
            if (normalBrand) normalBrand.SetActive(true);
            SetFade(0);
        }
        void OnDisable() => ResetPresentation();
    }
}
