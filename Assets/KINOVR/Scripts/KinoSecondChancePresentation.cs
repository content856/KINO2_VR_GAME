using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    // One world-space canvas follows the active view, including both XR eyes.
    // The board/title use the same game clock as the draw, so pause stays coherent.
    public sealed class KinoSecondChancePresentation : MonoBehaviour
    {
        public KinoPlayerView playerView;
        public GameObject announcement;
        public GameObject activeHeader;
        public GameObject normalBrand;
        public Canvas viewFade;
        public Image fadeImage;
        public KinoBoostPresentation roomTreatment;
        bool blueRoom;
        public float FadeAlpha => fadeImage ? fadeImage.color.a : 0;

        public void Present(KinoRoundState state, double now)
        {
            var phase = state.Phase;
            bool reveal = phase == KinoRoundPhase.SecondChanceReveal;
            bool active = phase == KinoRoundPhase.SecondChance;
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
            if (!viewFade || !fadeImage) return;
            fadeImage.color = new Color(0, 0, 0, alpha);
            viewFade.gameObject.SetActive(alpha > 0);
            FollowView();
        }
        void LateUpdate() { if (viewFade && viewFade.gameObject.activeSelf) FollowView(); }
        void FollowView()
        {
            if (!playerView || !playerView.View || !viewFade) return;
            var view = playerView.View;
            var camera = view.GetComponent<Camera>();
            float distance = camera ? camera.nearClipPlane + .02f : .2f;
            viewFade.transform.SetPositionAndRotation(view.position + view.forward * distance, view.rotation);
            viewFade.transform.localScale = Vector3.one * Mathf.Max(2, distance * 10);
        }
        public void ResetPresentation()
        {
            if (blueRoom && roomTreatment) roomTreatment.SetSecondChanceLighting(false);
            blueRoom = false;
            if (announcement) announcement.SetActive(false);
            if (activeHeader) activeHeader.SetActive(false);
            if (normalBrand) normalBrand.SetActive(true);
            SetFade(0);
        }
        void OnDisable() => ResetPresentation();
    }
}
