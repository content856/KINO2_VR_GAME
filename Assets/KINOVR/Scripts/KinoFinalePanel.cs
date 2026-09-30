using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    // A single, texture-free UI mesh: opaque reading surface, architectural gold
    // trim and restrained cyan accents. It shares the screen's world-space canvas.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class KinoFinalePanel : MaskableGraphic
    {
        protected KinoFinalePanel() { useLegacyMeshGeneration = false; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Fill(vh, new Rect(r.x - 8, r.y - 12, r.width + 16, r.height + 16), 26,
                new Color(0, 0, 0, .3f), new Color(0, 0, 0, .3f));
            Fill(vh, r, 22, new Color(.025f, .065f, .12f, .98f), new Color(.045f, .12f, .2f, .98f));
            Ring(vh, r, 22, 3, new Color(.92f, .69f, .28f), new Color(.49f, .29f, .095f));
            Rect inset = new Rect(r.x + 11, r.y + 11, r.width - 22, r.height - 22);
            Ring(vh, inset, 16, 1, new Color(.6f, .45f, .22f, .55f), new Color(.3f, .48f, .58f, .35f));
            // Short rules frame the logo and separate the thank-you from the score.
            Rule(vh, -300, 300, 160, new Color(.75f, .57f, .25f, .6f));
            Rule(vh, -240, 240, -198, new Color(.25f, .66f, .78f, .5f));
            Rule(vh, -70, 70, r.yMax - 12, new Color(.35f, .8f, .9f, .9f), 3);
        }

        static Vector2 Corner(Rect r, float cut, int index)
        {
            switch (index)
            {
                case 0: return new Vector2(r.xMin + cut, r.yMin);
                case 1: return new Vector2(r.xMax - cut, r.yMin);
                case 2: return new Vector2(r.xMax, r.yMin + cut);
                case 3: return new Vector2(r.xMax, r.yMax - cut);
                case 4: return new Vector2(r.xMax - cut, r.yMax);
                case 5: return new Vector2(r.xMin + cut, r.yMax);
                case 6: return new Vector2(r.xMin, r.yMax - cut);
                default: return new Vector2(r.xMin, r.yMin + cut);
            }
        }

        static void Fill(VertexHelper vh, Rect rect, float cut, Color bottom, Color top)
        {
            int start = vh.currentVertCount;
            vh.AddVert(rect.center, Color.Lerp(bottom, top, .5f), Vector2.zero);
            for (int i = 0; i < 8; i++)
            {
                var p = Corner(rect, cut, i);
                vh.AddVert(p, Color.Lerp(bottom, top, Mathf.InverseLerp(rect.yMin, rect.yMax, p.y)), Vector2.zero);
            }
            for (int i = 0; i < 8; i++) vh.AddTriangle(start, start + 1 + (i + 1) % 8, start + 1 + i);
        }

        static void Ring(VertexHelper vh, Rect rect, float cut, float width, Color top, Color bottom)
        {
            int start = vh.currentVertCount;
            var inner = new Rect(rect.x + width, rect.y + width, rect.width - width * 2, rect.height - width * 2);
            for (int i = 0; i < 8; i++)
            {
                var p = Corner(rect, cut, i);
                var color = Color.Lerp(bottom, top, Mathf.InverseLerp(rect.yMin, rect.yMax, p.y));
                vh.AddVert(p, color, Vector2.zero);
                vh.AddVert(Corner(inner, Mathf.Max(0, cut - width * .6f), i), color, Vector2.zero);
            }
            for (int i = 0; i < 8; i++)
            {
                int a = start + i * 2, b = start + ((i + 1) % 8) * 2;
                vh.AddTriangle(a, a + 1, b);
                vh.AddTriangle(b, a + 1, b + 1);
            }
        }

        static void Rule(VertexHelper vh, float left, float right, float y, Color color, float thickness = 1)
        {
            Fill(vh, new Rect(left, y, right - left, thickness), 0, color, color);
        }
    }
}
