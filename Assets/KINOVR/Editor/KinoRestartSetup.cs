using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KinoVR.Editor
{
    public static partial class KinoGameplaySetup
    {
        [MenuItem("Tools/KINO VR/Restart/1 - Set up end-of-round button")]
        public static void ApplyRestart()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            LoadMaterials();
            const string path = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";
            var gameplay = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureRestart(gameplay.GetComponent<KinoRoundController>());
                PrefabUtility.SaveAsPrefabAsset(gameplay, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(gameplay); }
            AssetDatabase.SaveAssets();
            Status("RESTART_READY");
        }

        static void ConfigureRestart(KinoRoundController round)
        {
            if (round.restartButton) return;
            var canvas = CanvasAt("Restart button", round.transform, new Vector2(360, 144));
            canvas.transform.localScale = Vector3.one * .001f;
            var restart = canvas.gameObject.AddComponent<KinoRestartButton>();
            restart.round = round;
            round.restartButton = restart;

            Graphic("Gold border", canvas.transform, 0, 0, 360, 144, null).color = new Color(1, .77f, .2f);
            var face = Graphic("Blue face", canvas.transform, 4, 4, 352, 136, null);
            face.color = new Color(.015f, .065f, .15f);
            Text("Restart label", canvas.transform, 14, 25, 332, 62, "RESTART", 47, Color.white, true);
            Text("Restart hint", canvas.transform, 14, 92, 332, 28, "PLAY AGAIN", 20, new Color(1, .82f, .36f), true);
            restart.button = canvas.gameObject.AddComponent<Button>();
            restart.button.targetGraphic = face;
            var colors = restart.button.colors;
            colors.disabledColor = new Color(.7f, .7f, .7f, 1);
            restart.button.colors = colors;
            restart.button.navigation = new Navigation { mode = Navigation.Mode.None };
            restart.pressArea = canvas.gameObject.AddComponent<BoxCollider>();
            restart.pressArea.size = new Vector3(360, 144, 65);
            restart.pressArea.isTrigger = true;
            var body = canvas.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            restart.Hide();
            EditorUtility.SetDirty(round);
        }
    }
}
