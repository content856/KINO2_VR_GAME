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
        public Color bonusCaughtColor = Color.white;
        readonly float[] pulseUntil = new float[80];
        readonly bool[] bonusCaught = new bool[80];
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
                statusText.text = state.Phase == KinoRoundPhase.Boost ? "SCORE  <color=#FFC43D>x3</color>" : "SCORE";
            if (caughtTotalText) caughtTotalText.text = $"CAUGHT {state.CatchCount:000}";
        }
        public void ResetBoard()
        {
            for (int i = 0; i < 80; i++)
            {
                bonusCaught[i] = false;
                if (i < numberLabels.Length && numberLabels[i]) numberLabels[i].color = boosted ? new Color(1, .72f, .18f) : waitingColor;
                if (i < caughtMarkers.Length && caughtMarkers[i])
                {
                    caughtMarkers[i].SetActive(false);
                    caughtMarkers[i].transform.localScale = Vector3.one;
                    CacheMarker(i);
                    if (markerGraphics[i]) markerGraphics[i].material = normalMarkerMaterials[i];
                }
                pulseUntil[i] = 0;
            }
        }
        void CacheMarker(int i)
        {
            if (markerGraphics[i] || i >= caughtMarkers.Length || !caughtMarkers[i]) return;
            markerGraphics[i] = caughtMarkers[i].GetComponent<Graphic>();
            if (markerGraphics[i]) normalMarkerMaterials[i] = markerGraphics[i].material;
        }
        public void MarkCaught(int number, bool isKinoBonus = false)
        {
            if (number < 1 || number > 80) return;
            int i = number - 1;
            // Once this number earns a bonus, ordinary repeats cannot downgrade it.
            bonusCaught[i] |= isKinoBonus;
            if (i < numberLabels.Length && numberLabels[i]) numberLabels[i].color = bonusCaught[i] ? bonusCaughtColor : caughtColor;
            if (i < caughtMarkers.Length && caughtMarkers[i])
            {
                CacheMarker(i);
                if (markerGraphics[i]) markerGraphics[i].material = bonusCaught[i] && kinoBonusMarkerMaterial ? kinoBonusMarkerMaterial : normalMarkerMaterials[i];
                caughtMarkers[i].SetActive(true);
            }
            pulseUntil[i] = Time.time + .35f;
        }
        void Update()
        {
            for (int i = 0; i < caughtMarkers.Length && i < 80; i++)
            {
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
