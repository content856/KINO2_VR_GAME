using TMPro;
using UnityEngine;

namespace KinoVR
{
    public sealed class KinoNumberBoard : MonoBehaviour
    {
        public TMP_Text[] numberLabels = new TMP_Text[80];
        public GameObject[] caughtMarkers = new GameObject[80];
        public TMP_Text catchCountText;
        public TMP_Text timeText;
        public TMP_Text statusText;
        public RectTransform timeFill;
        public Color waitingColor = new Color(.10f, .85f, 1f);
        public Color caughtColor = new Color(.025f, .045f, .08f);
        readonly float[] pulseUntil = new float[80];
        public void ResetBoard()
        {
            for (int i = 0; i < 80; i++)
            {
                if (i < numberLabels.Length && numberLabels[i]) numberLabels[i].color = waitingColor;
                if (i < caughtMarkers.Length && caughtMarkers[i])
                {
                    caughtMarkers[i].SetActive(false);
                    caughtMarkers[i].transform.localScale = Vector3.one;
                }
                pulseUntil[i] = 0;
            }
        }
        public void MarkCaught(int number)
        {
            if (number < 1 || number > 80) return;
            int i = number - 1;
            if (i < numberLabels.Length && numberLabels[i]) numberLabels[i].color = caughtColor;
            if (i < caughtMarkers.Length && caughtMarkers[i]) caughtMarkers[i].SetActive(true);
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
            if (timeFill) timeFill.anchorMax = new Vector2(Mathf.Clamp01(seconds / Mathf.Max(.01f, duration)), 1);
        }
    }
}
