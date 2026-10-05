using UnityEngine;

namespace KinoVR
{
    // Both announcements replace the board's number field, below its existing header.
    public static class KinoBoardOverlay
    {
        public static readonly Vector2 ArtworkSize = new Vector2(1020, 480);

        public static void Place(Canvas canvas, KinoNumberBoard board, Vector2 artworkSize)
        {
            if (!canvas || !board || !board.numberField) return;
            var field = board.numberField;
            var rect = (RectTransform)canvas.transform;
            // Keep a root canvas: nested canvases inherit the board's render batch
            // and can disappear behind its animated number-field material.
            rect.SetParent(board.transform.parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = artworkSize;
            rect.SetPositionAndRotation(field.TransformPoint(new Vector3(field.rect.center.x, field.rect.center.y, -2)), field.rotation);
            Vector3 parentScale = rect.parent ? rect.parent.lossyScale : Vector3.one;
            Vector3 fieldScale = field.lossyScale;
            rect.localScale = new Vector3(fieldScale.x * field.rect.width / artworkSize.x / parentScale.x,
                fieldScale.y * field.rect.height / artworkSize.y / parentScale.y, fieldScale.z / parentScale.z);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            var boardCanvas = board.GetComponent<Canvas>();
            if (boardCanvas) canvas.worldCamera = boardCanvas.worldCamera;
        }
    }
}
