using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    public sealed class KinoNumberBoard : MonoBehaviour
    {
        public TMP_Text[] numberLabels = new TMP_Text[80];
        public GameObject[] caughtMarkers = new GameObject[80];
        public TMP_Text catchCountText;
        public TMP_Text timeText;
        public TMP_Text statusText;
        public TMP_Text caughtTotalText;
        public RectTransform timeFill;
        public Color waitingColor = new Color(.10f, .85f, 1f);
        public Color caughtColor = new Color(.025f, .045f, .08f);
        public Material kinoBonusMarkerMaterial;
        public Material secondChanceMarkerMaterial;
        public TMP_Text[] multiplierLabels = new TMP_Text[80];
        public Color bonusCaughtColor = Color.white;
        public RectTransform numberField;
        public CanvasGroup numberGrid;
        bool secondChanceCover, finaleCover;
        public bool NumbersVisible => !secondChanceCover && !finaleCover;

        public void SetSecondChanceCover(bool visible)
        {
            secondChanceCover = visible;
            ApplyNumberVisibility();
        }
        public void SetFinaleCover(bool visible)
        {
            finaleCover = visible;
            ApplyNumberVisibility();
        }
        void ApplyNumberVisibility()
        {
            // Hide the whole grid without losing caught markers or multiplier state.
            if (numberGrid) numberGrid.alpha = NumbersVisible ? 1 : 0;
        }
        readonly float[] pulseUntil = new float[80];
        readonly float[] multiplierUntil = new float[80];
        readonly bool[] bonusCaught = new bool[80];
        readonly bool[] secondChanceCaught = new bool[80];
        readonly Graphic[] markerGraphics = new Graphic[80];
        readonly Material[] normalMarkerMaterials = new Material[80];
        bool boosted;
        public void SetBoostColors(bool active)
        {
            boosted = active;
            for (int i = 0; i < numberLabels.Length; i++)
                if (numberLabels[i] && (i >= caughtMarkers.Length || !caughtMarkers[i] || !caughtMarkers[i].activeSelf))
                    numberLabels[i].color = active ? new Color(1, .72f, .18f) : waitingColor;
            if (timeFill) timeFill.GetComponent<UnityEngine.UI.Graphic>().color = active ? new Color(1, .64f, .08f) : new Color(.03f, .68f, 1);
        }
        public void SetRoundProgress(KinoRoundState state, bool finished)
        {
            SetProgress(state.Score, state.RemainingSeconds, state.PhaseDuration, finished);
            if (statusText && !finished)
                statusText.text = state.Phase == KinoRoundPhase.Bonus ? "KINO BONUS <color=#FF394F>x3</color>" :
                    state.Phase == KinoRoundPhase.Boost || state.Phase == KinoRoundPhase.BoostSettling ? "KINO BOOST <color=#FFD34D>x3</color>" :
                    state.Phase == KinoRoundPhase.SecondChance ? "SECOND CHANCE <color=#36EB69>x3</color>" :
                    state.Phase == KinoRoundPhase.Settling ? "LAST BALLS" : "SCORE";
            if (caughtTotalText) caughtTotalText.text = state.Phase == KinoRoundPhase.SecondChance ?
                $"EXTRA {state.SecondChanceCatchCount} / {KinoRoundState.SecondChanceBallLimit}" :
                $"CAUGHT {state.NormalCatchCount + state.SecondChanceCatchCount:00} / {(state.SecondChanceLaunchCount > 0 ? 23 : 20)}";
        }
        public void ResetBoard()
        {
            secondChanceCover = finaleCover = false;
            ApplyNumberVisibility();
            for (int i = 0; i < 80; i++)
            {
                bonusCaught[i] = false;
                secondChanceCaught[i] = false;
                if (i < numberLabels.Length && numberLabels[i]) numberLabels[i].color = boosted ? new Color(1, .72f, .18f) : waitingColor;
                if (i < caughtMarkers.Length && caughtMarkers[i])
                {
                    caughtMarkers[i].SetActive(false);
                    caughtMarkers[i].transform.localScale = Vector3.one;
                    CacheMarker(i);
                    if (markerGraphics[i]) markerGraphics[i].material = normalMarkerMaterials[i];
                }
                pulseUntil[i] = 0;
                multiplierUntil[i] = 0;
                if (i < multiplierLabels.Length && multiplierLabels[i]) multiplierLabels[i].gameObject.SetActive(false);
            }
        }
        void CacheMarker(int i)
        {
            if (markerGraphics[i] || i >= caughtMarkers.Length || !caughtMarkers[i]) return;
            markerGraphics[i] = caughtMarkers[i].GetComponent<Graphic>();
            if (markerGraphics[i]) normalMarkerMaterials[i] = markerGraphics[i].material;
        }
        public void MarkCaught(int number, bool isKinoBonus = false, bool isSecondChance = false)
        {
            if (number < 1 || number > 80) return;
            int i = number - 1;
            // Once this number earns a bonus, ordinary repeats cannot downgrade it.
            bonusCaught[i] |= isKinoBonus;
            secondChanceCaught[i] |= isSecondChance;
            if (i < numberLabels.Length && numberLabels[i]) numberLabels[i].color = bonusCaught[i] || secondChanceCaught[i] ? bonusCaughtColor : caughtColor;
            if (i < caughtMarkers.Length && caughtMarkers[i])
            {
                CacheMarker(i);
                if (markerGraphics[i]) markerGraphics[i].material = bonusCaught[i] && kinoBonusMarkerMaterial ? kinoBonusMarkerMaterial :
                    secondChanceCaught[i] && secondChanceMarkerMaterial ? secondChanceMarkerMaterial : normalMarkerMaterials[i];
                caughtMarkers[i].SetActive(true);
            }
            pulseUntil[i] = Time.time + .35f;
        }
        public void ShowMultiplier(int number)
        {
            if (number < 1 || number > 80) return;
            int i = number - 1;
            multiplierUntil[i] = Time.time + .85f;
            if (i < multiplierLabels.Length && multiplierLabels[i])
            {
                var label = multiplierLabels[i];
                label.text = "x3";
                label.color = new Color(1, .9f, .3f, 1);
                label.gameObject.SetActive(true);
            }
        }
        void Update()
        {
            for (int i = 0; i < caughtMarkers.Length && i < 80; i++)
            {
                if (i < multiplierLabels.Length && multiplierLabels[i] && multiplierLabels[i].gameObject.activeSelf)
                {
                    float t = Mathf.Clamp01((multiplierUntil[i] - Time.time) / .85f);
                    multiplierLabels[i].color = new Color(1, .9f, .3f, Mathf.Min(1, t * 3));
                    multiplierLabels[i].transform.localScale = Vector3.one * (1 + .25f * Mathf.Sin(t * Mathf.PI));
                    if (t <= 0) multiplierLabels[i].gameObject.SetActive(false);
                }
                if (!caughtMarkers[i] || !caughtMarkers[i].activeSelf) continue;
                float remaining = Mathf.Clamp01((pulseUntil[i] - Time.time) / .35f);
                caughtMarkers[i].transform.localScale = Vector3.one * (1 + .18f * Mathf.Sin(remaining * Mathf.PI));
            }
        }
        public void SetProgress(int caught, float seconds, float duration, bool finished)
        {
            if (catchCountText) catchCountText.text = caught.ToString("000");
            int total = Mathf.CeilToInt(Mathf.Max(0, seconds));
            if (timeText) timeText.text = $"{total / 60:00}:{total % 60:00}";
            if (statusText) statusText.text = finished ? "ROUND COMPLETE" : "BALLS CAUGHT";
            if (timeFill) timeFill.anchorMax = new Vector2(Mathf.Clamp01(1 - seconds / Mathf.Max(.01f, duration)), 1);
        }
    }
}
