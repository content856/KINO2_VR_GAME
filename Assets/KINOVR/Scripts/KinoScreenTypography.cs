using TMPro;
using UnityEngine;

namespace KinoVR
{
    // Vertex colours keep the metallic lettering on the shared TMP material.
    public static class KinoScreenTypography
    {
        public static readonly Color Ivory = new Color(1, .95f, .81f);

        public static void Gold(TMP_Text label, Material material = null)
        {
            if (material) label.fontSharedMaterial = material;
            label.color = Color.white;
            label.fontStyle = FontStyles.Bold;
            label.enableVertexGradient = true;
            label.colorGradient = new VertexGradient(
                new Color(1, .97f, .77f), new Color(1, .92f, .62f),
                new Color(.77f, .43f, .1f), new Color(1, .73f, .27f));
        }
    }
}
