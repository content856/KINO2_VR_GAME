using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    // Static emerald announcement art. Separate TMP labels remain editable.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class KinoSecondChancePanel : MaskableGraphic
    {
        public KinoSecondChancePanel() { useLegacyMeshGeneration = false; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var face = new Rect(-510, -275, 1020, 550);
            var gold = new Color(1, .82f, .43f);
            var emerald = new Color(.12f, 1, .35f);
            // Leave the board's blue number-field artwork visible through the decoration.
            KinoPanelMesh.Glow(vh, new Vector2(-296, 68), new Vector2(266, 234), new Color(.015f, .48f, .13f, .13f));
            KinoPanelMesh.Glow(vh, new Vector2(0, -269), new Vector2(263, 53), new Color(.11f, 1, .2f, .16f));
            KinoPanelMesh.Glow(vh, new Vector2(0, 270), new Vector2(238, 35), new Color(.14f, 1, .18f, .19f));
            for (int i = 0; i < 4; i++)
                KinoPanelMesh.Arc(vh, new Vector2(-109, 6), new Vector2(231 + i * 35, 209 + i * 28),
                    -78, 227, .65f, new Color(.12f, .61f, .26f, .09f), new Color(.12f, .61f, .26f, .055f), 60);
            AddSwoosh(vh, new Vector2(-474, 19), new Vector2(-553, 146), new Vector2(-297, 251), new Vector2(-57, 250), .82f);
            AddSwoosh(vh, new Vector2(314, 141), new Vector2(583, -27), new Vector2(437, -182), new Vector2(132, -205), .72f);
            KinoPanelMesh.Curve(vh, new Vector2(-406, -187), new Vector2(-185, -276), new Vector2(289, -195), new Vector2(477, 17),
                .7f, new Color(.19f, .7f, .25f, 0), new Color(.26f, .8f, .33f, .27f), 52);
            for (int i = 3; i > 0; i--)
                KinoPanelMesh.RoundedRing(vh, KinoPanelMesh.Inset(face, -i * 2), 54 + i * 2, 2,
                    new Color(.35f, 1, .25f, .025f * (4 - i)), new Color(.65f, 1, .2f, .034f * (4 - i)));
            KinoPanelMesh.RoundedRing(vh, face, 54, 2.2f, gold, new Color(.78f, .61f, .22f));
            KinoPanelMesh.RoundedRing(vh, KinoPanelMesh.Inset(face, 13), 42, 1,
                new Color(.88f, .73f, .33f, .63f), new Color(.67f, .61f, .23f, .61f));
            AddVeins(vh);

            // Circular restart mark frames the separately rendered gold "2η".
            Vector2 arrowCenter = new Vector2(-326, 65);
            KinoPanelMesh.Arc(vh, arrowCenter + new Vector2(2, -4), new Vector2(103, 111), 52, 358, 20,
                new Color(.015f, .16f, .07f), new Color(.012f, .2f, .07f), 56);
            KinoPanelMesh.Arc(vh, arrowCenter, new Vector2(103, 111), 52, 358, 18,
                new Color(.06f, .95f, .24f), new Color(.015f, .38f, .12f), 56);
            KinoPanelMesh.Arc(vh, arrowCenter, new Vector2(112, 120), 55, 356, 1,
                new Color(.43f, 1, .54f, .9f), new Color(.08f, .68f, .19f, .55f), 56);
            // Tip follows the clockwise tangent at the arc's 52-degree endpoint.
            KinoPanelMesh.Triangle(vh, arrowCenter + new Vector2(91, 65), arrowCenter + new Vector2(70, 116), arrowCenter + new Vector2(34, 75),
                new Color(.08f, .53f, .16f), new Color(.35f, 1, .45f), new Color(.05f, .71f, .19f));
            KinoPanelMesh.Line(vh, arrowCenter + new Vector2(70, 116), arrowCenter + new Vector2(91, 65), 1.25f,
                new Color(.7f, 1, .68f), new Color(.3f, .85f, .28f));
            KinoPanelMesh.Glow(vh, new Vector2(0, -80), new Vector2(243, 17), new Color(.22f, 1, .29f, .15f));
            KinoPanelMesh.Line(vh, new Vector2(-327, -80), new Vector2(0, -80), 1.35f, new Color(.05f, .8f, .19f, 0), emerald);
            KinoPanelMesh.Line(vh, new Vector2(0, -80), new Vector2(360, -80), 1.35f, emerald, new Color(.05f, .8f, .19f, 0));
            KinoPanelMesh.Diamond(vh, new Vector2(0, -80), new Vector2(10, 10), new Color(.84f, 1, .53f));
            KinoPanelMesh.Diamond(vh, new Vector2(0, -80), new Vector2(6.5f, 6.5f), new Color(.23f, .7f, .21f));
            KinoPanelMesh.Diamond(vh, new Vector2(0, -80), new Vector2(3, 3), new Color(1, .92f, .58f));
            KinoPanelMesh.Line(vh, new Vector2(-359, -142), new Vector2(-236, -142), 1.1f, new Color(.92f, .67f, .21f, 0), gold);
            KinoPanelMesh.Line(vh, new Vector2(236, -142), new Vector2(359, -142), 1.1f, gold, new Color(.92f, .67f, .21f, 0));
            KinoPanelMesh.Diamond(vh, new Vector2(-236, -142), new Vector2(4.5f, 4.5f), gold);
            KinoPanelMesh.Diamond(vh, new Vector2(236, -142), new Vector2(4.5f, 4.5f), gold);
            KinoPanelMesh.Spark(vh, new Vector2(7, 273), 99, 18, new Color(.46f, 1, .3f, .94f));
            KinoPanelMesh.Spark(vh, new Vector2(-445, 68), 19, 31, new Color(.5f, 1, .53f, .8f));
            KinoPanelMesh.Spark(vh, new Vector2(466, -77), 15, 30, new Color(.33f, 1, .43f, .7f));
            KinoPanelMesh.Spark(vh, new Vector2(4, -272), 93, 13, new Color(.54f, 1, .25f, .72f));
            KinoPanelMesh.Fit(vh, GetPixelAdjustedRect(), new Vector2(1020, 550), color);
        }

        static void AddSwoosh(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float intensity)
        {
            for (int i = 3; i > 0; i--)
                KinoPanelMesh.Curve(vh, a, b, c, d, i * 7,
                    new Color(.015f, .9f, .14f, .025f * intensity), new Color(.08f, .95f, .23f, .008f * intensity), 48);
            KinoPanelMesh.Curve(vh, a, b, c, d, 2,
                new Color(.32f, 1, .4f, .72f * intensity), new Color(.06f, .76f, .2f, .02f), 48);
        }

        static void AddVeins(VertexHelper vh)
        {
            var gold = new Color(.8f, .61f, .18f, .22f);
            for (int side = -1; side <= 1; side += 2)
            {
                KinoPanelMesh.Line(vh, new Vector2(side * 469, 236), new Vector2(side * 429, 199), .8f, gold, gold);
                KinoPanelMesh.Line(vh, new Vector2(side * 429, 199), new Vector2(side * 450, 148), .8f, gold, Color.clear);
                KinoPanelMesh.Line(vh, new Vector2(side * 429, 199), new Vector2(side * 365, 227), .8f, gold, Color.clear);
                KinoPanelMesh.Line(vh, new Vector2(side * 475, -242), new Vector2(side * 427, -204), .8f, gold, gold);
                KinoPanelMesh.Line(vh, new Vector2(side * 427, -204), new Vector2(side * 443, -168), .8f, gold, Color.clear);
                KinoPanelMesh.Line(vh, new Vector2(side * 427, -204), new Vector2(side * 378, -228), .8f, gold, Color.clear);
                KinoPanelMesh.Diamond(vh, new Vector2(side * 429, 199), new Vector2(1.8f, 1.8f), gold);
                KinoPanelMesh.Diamond(vh, new Vector2(side * 427, -204), new Vector2(1.8f, 1.8f), gold);
            }
        }
    }
}
