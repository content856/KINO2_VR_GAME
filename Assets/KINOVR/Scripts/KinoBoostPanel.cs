using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    // Transparent gold announcement frame over the board's own artwork.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class KinoBoostPanel : MaskableGraphic
    {
        public KinoBoostPanel() { useLegacyMeshGeneration = false; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var face = new Rect(-510, -240, 1020, 480);
            var gold = new Color(1, .81f, .35f);
            var bronze = new Color(.69f, .42f, .10f);
            for (int i = 3; i > 0; i--)
                KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, -i * 2), 26 + i * 2, 2,
                    new Color(1, .67f, .15f, .04f), new Color(1, .75f, .2f, .06f));
            KinoPanelMesh.ChamferRing(vh, face, 26, 2.4f, gold, bronze);
            KinoPanelMesh.ChamferRing(vh, KinoPanelMesh.Inset(face, 12), 19, 1,
                new Color(1, .82f, .42f, .6f), new Color(.8f, .54f, .18f, .55f));
            KinoPanelMesh.Glow(vh, new Vector2(0, 55), new Vector2(410, 150), new Color(1, .64f, .08f, .10f));
            for (int side = -1; side <= 1; side += 2)
            {
                KinoPanelMesh.Line(vh, new Vector2(side * 370, -55), new Vector2(side * 225, -55), 1.5f,
                    new Color(1, .72f, .24f, 0), gold);
                KinoPanelMesh.Diamond(vh, new Vector2(side * 225, -55), new Vector2(5, 5), gold);
                // Double chevrons give the announcement a forward-motion accent.
                for (int i = 0; i < 2; i++)
                {
                    float x = side * (410 + i * 28);
                    KinoPanelMesh.Line(vh, new Vector2(x - side * 18, 108), new Vector2(x + side * 10, 70), 4, bronze, gold);
                    KinoPanelMesh.Line(vh, new Vector2(x + side * 10, 70), new Vector2(x - side * 18, 32), 4, gold, bronze);
                }
            }
            KinoPanelMesh.Spark(vh, new Vector2(0, 238), 88, 14, gold);
            KinoPanelMesh.Spark(vh, new Vector2(0, -238), 88, 14, gold);
            KinoPanelMesh.Fit(vh, GetPixelAdjustedRect(), new Vector2(1020, 480), color);
        }
    }
}
