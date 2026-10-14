using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    /// <summary>Texture-free player-picker surfaces, inspired by the supplied KINO artwork.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class KinoBackendSelectionGraphic : MaskableGraphic
    {
        public bool background;
        public bool showArrow;
        bool available = true, highlighted, pressed;

        public void SetState(bool enabled, bool hover, bool down)
        {
            if (available == enabled && highlighted == hover && pressed == down) return;
            available = enabled; highlighted = hover; pressed = down;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            if (background) DrawBackground(vh, rect);
            else DrawRow(vh, rect);
            var vertex = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.color = (Color)vertex.color * color;
                vh.SetUIVertex(vertex, i);
            }
        }

        static void DrawBackground(VertexHelper vh, Rect r)
        {
            const float radius = 24;
            KinoPanelMesh.RoundedFill(vh, r, radius, new Color(.003f, .018f, .054f, .97f), new Color(.004f, .08f, .20f, .97f));
            // Fine blue ribbons live around the logo; the text and touch rows stay quiet.
            for (int line = 0; line < 30; line++)
            {
                float offset = line - 14.5f;
                Vector2 previous = default;
                for (int segment = 0; segment <= 72; segment++)
                {
                    float t = segment / 72f;
                    float x = Mathf.Lerp(r.xMin + 5, r.xMax - 5, t);
                    float y = r.yMax - 124 + Mathf.Sin(t * Mathf.PI * 2.1f + offset * .025f) * (40 + offset * .5f) + offset * 2.1f;
                    var point = new Vector2(x, y);
                    float edge = Mathf.Sin(t * Mathf.PI);
                    float alpha = (.10f + .20f * Mathf.Abs(offset) / 14.5f) * Mathf.Clamp01(edge * 3);
                    var shade = Color.Lerp(new Color(.02f, .25f, 1, alpha), new Color(.04f, .66f, 1, alpha), line / 29f);
                    if (segment > 0) KinoPanelMesh.Line(vh, previous, point, .65f, shade, shade);
                    previous = point;
                }
            }
            // Rounded navy list tray and electric-blue frame echo the portrait artwork.
            var tray = new Rect(r.xMin + 13, r.yMin + 13, r.width - 26, r.height * .63f);
            KinoPanelMesh.RoundedFill(vh, tray, 18, new Color(.002f, .015f, .042f, .97f), new Color(.008f, .055f, .13f, .97f));
            KinoPanelMesh.RoundedRing(vh, tray, 18, 1.05f, new Color(.10f, .30f, 1, .90f), new Color(.015f, .13f, .68f, .80f));
            KinoPanelMesh.RoundedRing(vh, r, radius, 1.2f, new Color(.10f, .38f, 1, .62f), new Color(.01f, .08f, .40f, .4f));
        }

        void DrawRow(VertexHelper vh, Rect r)
        {
            float radius = Mathf.Min(13, r.height * .25f);
            bool gold = highlighted || pressed;
            float alpha = available ? 1 : .50f;
            var upper = gold ? new Color(.98f, .81f, .32f, alpha) : new Color(.75f, .83f, .92f, alpha * .90f);
            var lower = gold ? new Color(.65f, .39f, .06f, alpha) : new Color(.32f, .46f, .64f, alpha * .85f);
            if (gold)
                for (int i = 3; i > 0; i--)
                    KinoPanelMesh.RoundedRing(vh, KinoPanelMesh.Inset(r, -i * 1.2f), radius + i * 1.2f, 1.2f,
                        new Color(1, .64f, .12f, .045f * (4 - i)), new Color(1, .64f, .12f, .02f * (4 - i)));
            KinoPanelMesh.RoundedFill(vh, r, radius,
                new Color(.002f, .018f, .050f, .99f), new Color(.008f, gold ? .105f : .075f, gold ? .20f : .16f, .99f));
            KinoPanelMesh.RoundedRing(vh, r, radius, gold ? 1.7f : 1.1f, upper, lower);
            // A subtle inner top highlight gives the metal rim depth without a raster image.
            KinoPanelMesh.RoundedRing(vh, KinoPanelMesh.Inset(r, 2.1f), Mathf.Max(0, radius - 2.1f), .65f,
                new Color(1, gold ? .85f : .95f, gold ? .44f : 1, gold ? .24f : .09f), Color.clear);
            if (showArrow)
            {
                var center = new Vector2(r.xMax - 22, r.center.y);
                var arrow = gold ? new Color(1, .82f, .33f, alpha) : new Color(.29f, .56f, .86f, alpha);
                KinoPanelMesh.Line(vh, center + new Vector2(-3, -5), center + new Vector2(2, 0), 1.7f, arrow, arrow);
                KinoPanelMesh.Line(vh, center + new Vector2(2, 0), center + new Vector2(-3, 5), 1.7f, arrow, arrow);
            }
        }
    }
}
