using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static partial class KinoGameplaySetup
    {
        internal static void CaptureSequencePlayerView(KinoRoundController round, string name)
        {
            var go = new GameObject("Sequence player QA camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                var source = round.playerView.View.GetComponent<Camera>();
                if (source) camera.CopyFrom(source);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(round.playerView.View.position, round.playerView.View.rotation);
                Capture(camera, SecondChanceOutput + "/" + name + ".png", 1280, 800);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [MenuItem("Tools/KINO VR/Second Chance/3 - Capture logo, green board and ball")]
        public static void CaptureSecondChance()
        {
            RequireScene();
            Directory.CreateDirectory(SecondChanceOutput);
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            var original = round.board;
            bool wasActive = original.gameObject.activeSelf;
            var board = Object.Instantiate(original, original.transform.parent);
            var go = new GameObject("Second Chance preview camera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true;
            var screen = Screen().bounds;
            camera.orthographicSize = screen.size.y * .505f;
            camera.transform.SetPositionAndRotation(screen.center + Vector3.back * 2, Quaternion.identity);
            camera.nearClipPlane = .02f; camera.farClipPlane = 80;
            GameObject ball = null;
            original.gameObject.SetActive(false);
            try
            {
                board.gameObject.SetActive(true); board.ResetBoard();
                foreach (int n in new[] { 7, 12, 26, 34, 57, 71, 80 }) board.MarkCaught(n);
                board.MarkCaught(12, true);
                var title = board.transform.Find("Second Chance display/Second Chance announcement");
                var header = board.transform.Find("Second Chance display/Second Chance active header");
                var brand = board.transform.Find("BOOST display/Sharp KINO brand");
                title.gameObject.SetActive(true); header.gameObject.SetActive(false); brand.gameObject.SetActive(false);
                board.SetProgress(10, 0, 60, false); board.statusText.text = "SCORE";
                foreach (var label in board.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Capture(camera, SecondChanceOutput + "/SecondChance-announcement.png", 1600, 904);
                title.gameObject.SetActive(false); header.gameObject.SetActive(true);
                board.MarkCaught(26, false, true); board.MarkCaught(48, false, true); board.MarkCaught(80, false, true);
                board.SetProgress(19, 0, 60, false);
                board.statusText.text = "SECOND CHANCE <color=#36EB69>x3</color>"; board.caughtTotalText.text = "EXTRA 3 / 3";
                Capture(camera, SecondChanceOutput + "/SecondChance-board.png", 1600, 904);
                board.ShowMultiplier(26); board.ShowMultiplier(80);
                header.gameObject.SetActive(false);
                board.transform.Find("BOOST display/BOOST active header").gameObject.SetActive(true);
                board.SetBoostColors(true); board.statusText.text = "KINO BOOST <color=#FFD34D>x3</color>";
                Capture(camera, SecondChanceOutput + "/Boost-repeat-multiplier.png", 1600, 904);
                board.gameObject.SetActive(false);
                ball = Object.Instantiate(round.launcher.ballPrefab);
                ball.GetComponent<Rigidbody>().isKinematic = true;
                ball.GetComponent<Catchable>().enabled = false;
                ball.transform.position = new Vector3(0, 30, 0);
                camera.orthographicSize = .17f;
                camera.transform.SetPositionAndRotation(ball.transform.position + Vector3.back, Quaternion.identity);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.012f, .026f, .06f);
                var visual = ball.GetComponent<KinoBallNumber>();
                visual.SetBonus(false, true); visual.SetNumber(80, camera.transform);
                Capture(camera, SecondChanceOutput + "/SecondChance-ball.png", 1000, 650);
            }
            finally
            {
                if (ball) Object.DestroyImmediate(ball);
                Object.DestroyImmediate(board.gameObject); Object.DestroyImmediate(go);
                original.gameObject.SetActive(wasActive);
            }
            Status("SECOND_CHANCE_PREVIEWS_READY");
        }
    }
}
