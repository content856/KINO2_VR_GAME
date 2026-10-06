using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    // All decoration shares one texture-free UI mesh and the default UI material.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class KinoFinalePanel : MaskableGraphic
    {
        public KinoFinalePanel() { useLegacyMeshGeneration = false; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var face = new Rect(-510, -240, 1020, 480);
            var gold = new Color(1, .79f, .36f);
            var bronze = new Color(.58f, .35f, .12f);
            // The existing blue board supplies the background; only gold decoration is drawn.
            KinoPanelMesh.Glow(vh, new Vector2(0, -230), new Vector2(390, 58), new Color(.96f, .51f, .13f, .09f));
            for (int i = 3; i > 0; i--)
                KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, -i * 2), 26 + i * 2, 2,
                    new Color(1, .67f, .24f, .025f * (4 - i)), new Color(1, .60f, .17f, .025f * (4 - i)));
            KinoPanelMesh.ChamferRing(vh, face, 26, 2.4f, gold, bronze);
            KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, 8), 21, .85f,
                new Color(1, .82f, .43f, .56f), new Color(.74f, .48f, .16f, .55f));
            KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, 15), 16, .7f,
                new Color(.78f, .58f, .24f, .14f), new Color(.78f, .58f, .24f, .08f));
            AddVeins(vh);
            AddLaurel(vh, -1);
            AddLaurel(vh, 1);
            var transparentGold = new Color(1, .73f, .29f, 0);
            KinoPanelMesh.Line(vh, new Vector2(-340, 105), new Vector2(-181, 105), 1.2f, transparentGold, gold * new Color(1, 1, 1, .7f));
            KinoPanelMesh.Line(vh, new Vector2(181, 105), new Vector2(340, 105), 1.2f, gold * new Color(1, 1, 1, .7f), transparentGold);
            KinoPanelMesh.Diamond(vh, new Vector2(-181, 105), new Vector2(2.4f, 2.4f), gold);
            KinoPanelMesh.Diamond(vh, new Vector2(181, 105), new Vector2(2.4f, 2.4f), gold);
            KinoPanelMesh.Line(vh, new Vector2(-74, -129), new Vector2(-13, -129), 1.3f, transparentGold, gold);
            KinoPanelMesh.Line(vh, new Vector2(13, -129), new Vector2(74, -129), 1.3f, gold, transparentGold);
            KinoPanelMesh.Glow(vh, new Vector2(0, -129), new Vector2(28, 13), new Color(1, .64f, .18f, .19f));
            KinoPanelMesh.Diamond(vh, new Vector2(0, -129), new Vector2(7, 7), gold);
            KinoPanelMesh.Diamond(vh, new Vector2(0, -129), new Vector2(3.6f, 3.6f), new Color(1, .94f, .69f));
            var capsule = new Rect(-325, -204, 650, 54);
            KinoPanelMesh.RoundedRing(vh, capsule, 23, 1.55f,
                new Color(1, .82f, .44f, .96f), new Color(.77f, .5f, .18f, .85f));
            KinoPanelMesh.Spark(vh, new Vector2(0, 238), 52, 12, new Color(1, .81f, .4f, .6f));
            KinoPanelMesh.Spark(vh, new Vector2(-490, -222), 26, 13, new Color(1, .81f, .4f, .62f));
            KinoPanelMesh.Spark(vh, new Vector2(490, -222), 26, 13, new Color(1, .81f, .4f, .62f));
            KinoPanelMesh.Fit(vh, GetPixelAdjustedRect(), new Vector2(1020, 480), color);
        }

        static void AddLaurel(VertexHelper vh, int side)
        {
            Vector2 a = new Vector2(side * 180, -112);
            Vector2 b = new Vector2(side * 250, -100);
            Vector2 c = new Vector2(side * 280, -18);
            Vector2 d = new Vector2(side * 252, 62);
            var low = new Color(.6f, .32f, .07f, .83f);
            var high = new Color(1, .79f, .31f, .97f);
            KinoPanelMesh.Curve(vh, a, b, c, d, 1.8f, low, high, 32);
            for (int i = 0; i < 8; i++)
            {
                float t = .2f + i * .092f;
                Vector2 root = KinoPanelMesh.Bezier(a, b, c, d, t);
                Vector2 forward = (KinoPanelMesh.Bezier(a, b, c, d, t + .035f) - root).normalized;
                Vector2 outward = new Vector2(side, .15f);
                float length = Mathf.Lerp(30, 22, i / 7f);
                KinoPanelMesh.Leaf(vh, root, root + forward * length + outward * 12, 6.2f - i * .23f, low, high);
                if (i < 6)
                {
                    Vector2 innerRoot = KinoPanelMesh.Bezier(a, b, c, d, t + .034f);
                    KinoPanelMesh.Leaf(vh, innerRoot, innerRoot + forward * (length * .87f) - outward * 9, 4.5f, low, high);
                }
            }
            KinoPanelMesh.Leaf(vh, KinoPanelMesh.Bezier(a, b, c, d, .85f), d + new Vector2(-side * 2, 7), 5.4f, low, high);
            for (int i = 0; i < 2; i++)
                KinoPanelMesh.Arc(vh, new Vector2(0, -16), new Vector2(220 + i * 16, 110 + i * 11),
                    side < 0 ? 139 : -46, side < 0 ? 226 : 41, .65f,
                    new Color(.86f, .52f, .17f, .19f), new Color(.86f, .52f, .17f, .08f), 32);
        }

        static void AddVeins(VertexHelper vh)
        {
            var vein = new Color(.77f, .48f, .16f, .11f);
            KinoPanelMesh.Line(vh, new Vector2(-478, 210), new Vector2(-417, 185), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(-417, 185), new Vector2(-437, 139), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(-437, 139), new Vector2(-403, 101), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(-417, 185), new Vector2(-365, 202), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(488, 185), new Vector2(436, 149), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(436, 149), new Vector2(457, 104), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(436, 149), new Vector2(397, 147), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(-488, -192), new Vector2(-453, -171), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(-453, -171), new Vector2(-462, -125), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(482, -202), new Vector2(443, -178), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(443, -178), new Vector2(458, -140), .65f, vein, Color.clear);
        }
    }
}
