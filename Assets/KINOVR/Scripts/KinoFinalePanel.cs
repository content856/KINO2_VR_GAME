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
            var face = new Rect(-380, -360, 760, 720);
            var gold = new Color(1, .79f, .36f);
            var bronze = new Color(.58f, .35f, .12f);
            KinoPanelMesh.ChamferFill(vh, new Rect(-385, -370, 770, 730), 37,
                new Color(0, 0, 0, .28f), new Color(0, 0, 0, .28f));
            KinoPanelMesh.ChamferFill(vh, face, 34,
                new Color(.018f, .028f, .048f, .994f), new Color(.024f, .047f, .083f, .994f));
            KinoPanelMesh.Glow(vh, new Vector2(0, -347), new Vector2(290, 83), new Color(.96f, .51f, .13f, .09f));
            for (int i = 3; i > 0; i--)
                KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, -i * 2), 34 + i * 2, 2,
                    new Color(1, .67f, .24f, .025f * (4 - i)), new Color(1, .60f, .17f, .025f * (4 - i)));
            KinoPanelMesh.ChamferRing(vh, face, 34, 2.4f, gold, bronze);
            KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, 8), 29, .85f,
                new Color(1, .82f, .43f, .56f), new Color(.74f, .48f, .16f, .55f));
            KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, 15), 24, .7f,
                new Color(.78f, .58f, .24f, .14f), new Color(.78f, .58f, .24f, .08f));
            AddVeins(vh);
            AddLaurel(vh, -1);
            AddLaurel(vh, 1);
            var transparentGold = new Color(1, .73f, .29f, 0);
            KinoPanelMesh.Line(vh, new Vector2(-267, 64), new Vector2(-163, 64), 1.2f, transparentGold, gold * new Color(1, 1, 1, .7f));
            KinoPanelMesh.Line(vh, new Vector2(163, 64), new Vector2(267, 64), 1.2f, gold * new Color(1, 1, 1, .7f), transparentGold);
            KinoPanelMesh.Diamond(vh, new Vector2(-163, 64), new Vector2(2.4f, 2.4f), gold);
            KinoPanelMesh.Diamond(vh, new Vector2(163, 64), new Vector2(2.4f, 2.4f), gold);
            KinoPanelMesh.Line(vh, new Vector2(-74, -217), new Vector2(-13, -217), 1.3f, transparentGold, gold);
            KinoPanelMesh.Line(vh, new Vector2(13, -217), new Vector2(74, -217), 1.3f, gold, transparentGold);
            KinoPanelMesh.Glow(vh, new Vector2(0, -217), new Vector2(28, 13), new Color(1, .64f, .18f, .19f));
            KinoPanelMesh.Diamond(vh, new Vector2(0, -217), new Vector2(7, 7), gold);
            KinoPanelMesh.Diamond(vh, new Vector2(0, -217), new Vector2(3.6f, 3.6f), new Color(1, .94f, .69f));
            var capsule = new Rect(-285, -314, 570, 64);
            KinoPanelMesh.RoundedFill(vh, capsule, 26,
                new Color(.034f, .052f, .081f, .73f), new Color(.028f, .05f, .081f, .65f));
            KinoPanelMesh.RoundedRing(vh, capsule, 26, 1.55f,
                new Color(1, .82f, .44f, .96f), new Color(.77f, .5f, .18f, .85f));
            KinoPanelMesh.Spark(vh, new Vector2(0, 358), 52, 12, new Color(1, .81f, .4f, .6f));
            KinoPanelMesh.Spark(vh, new Vector2(-358, -337), 26, 13, new Color(1, .81f, .4f, .62f));
            KinoPanelMesh.Spark(vh, new Vector2(358, -337), 26, 13, new Color(1, .81f, .4f, .62f));
            KinoPanelMesh.Fit(vh, GetPixelAdjustedRect(), new Vector2(760, 720), color);
        }

        static void AddLaurel(VertexHelper vh, int side)
        {
            Vector2 a = new Vector2(side * 135, -186);
            Vector2 b = new Vector2(side * 247, -169);
            Vector2 c = new Vector2(side * 281, -73);
            Vector2 d = new Vector2(side * 252, 39);
            var low = new Color(.6f, .32f, .07f, .83f);
            var high = new Color(1, .79f, .31f, .97f);
            KinoPanelMesh.Curve(vh, a, b, c, d, 1.8f, low, high, 32);
            for (int i = 0; i < 8; i++)
            {
                float t = .2f + i * .092f;
                Vector2 root = KinoPanelMesh.Bezier(a, b, c, d, t);
                Vector2 forward = (KinoPanelMesh.Bezier(a, b, c, d, t + .035f) - root).normalized;
                Vector2 outward = new Vector2(side, .15f);
                float length = Mathf.Lerp(42, 30, i / 7f);
                KinoPanelMesh.Leaf(vh, root, root + forward * length + outward * 15, 7.3f - i * .27f, low, high);
                if (i < 6)
                {
                    Vector2 innerRoot = KinoPanelMesh.Bezier(a, b, c, d, t + .034f);
                    KinoPanelMesh.Leaf(vh, innerRoot, innerRoot + forward * (length * .87f) - outward * 11, 5.2f, low, high);
                }
            }
            KinoPanelMesh.Leaf(vh, KinoPanelMesh.Bezier(a, b, c, d, .85f), d + new Vector2(-side * 2, 7), 5.4f, low, high);
            for (int i = 0; i < 2; i++)
                KinoPanelMesh.Arc(vh, new Vector2(0, -55), new Vector2(209 + i * 18, 150 + i * 12),
                    side < 0 ? 139 : -46, side < 0 ? 226 : 41, .65f,
                    new Color(.86f, .52f, .17f, .19f), new Color(.86f, .52f, .17f, .08f), 32);
        }

        static void AddVeins(VertexHelper vh)
        {
            var vein = new Color(.77f, .48f, .16f, .11f);
            KinoPanelMesh.Line(vh, new Vector2(-350, 305), new Vector2(-277, 281), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(-277, 281), new Vector2(-297, 224), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(-297, 224), new Vector2(-263, 178), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(-277, 281), new Vector2(-225, 298), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(358, 252), new Vector2(306, 216), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(306, 216), new Vector2(327, 167), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(306, 216), new Vector2(267, 214), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(-358, -252), new Vector2(-323, -231), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(-323, -231), new Vector2(-332, -185), .65f, vein, Color.clear);
            KinoPanelMesh.Line(vh, new Vector2(352, -262), new Vector2(313, -238), .65f, vein, vein);
            KinoPanelMesh.Line(vh, new Vector2(313, -238), new Vector2(328, -200), .65f, vein, Color.clear);
        }
    }
}
