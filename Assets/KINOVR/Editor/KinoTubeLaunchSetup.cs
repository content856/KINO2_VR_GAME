using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static class KinoTubeLaunchSetup
    {
        const string AirPath = "Assets/KINOVR/Prefabs/KinoAirBalls.prefab";
        const string GamePath = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";
        public static readonly int[] LaunchBays = { 26, 2 };

        [MenuItem("Tools/KINO VR/8 - Select flight difficulty")]
        public static void SelectDifficulty()
        {
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            if (round && round.launcher) Selection.activeGameObject = round.launcher.gameObject;
        }

        public static void ApplyFlightTuning()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Apply flight tuning in Edit mode.");
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            if (!round || !round.launcher) throw new InvalidOperationException("Open the gameplay scene first.");
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ".utmp/TubeFlightOrientationBefore.unity", true);
            var prefab = PrefabUtility.LoadPrefabContents(GamePath);
            try
            {
                var launcher = prefab.GetComponentInChildren<BallLauncher>(true);
                FlightDefaults(launcher);
                PrefabUtility.SaveAsPrefabAsset(prefab, GamePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            FlightDefaults(round.launcher);
            EditorUtility.SetDirty(round.launcher);
            PrefabUtility.RecordPrefabInstancePropertyModifications(round.launcher);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            SelectDifficulty();
        }

        static void FlightDefaults(BallLauncher launcher)
        {
            launcher.flightTime = 2.6f;
            launcher.exitAcceleration = 28;
            launcher.turbulenceFrequency = 1;
            launcher.wobbleAngle = 3;
        }

        [MenuItem("Tools/KINO VR/6 - Tube airflow and gameplay pool")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Apply in Edit mode after lighting has finished.");
            var root = GameObject.Find("KINO air balls");
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            if (!root || !round) throw new InvalidOperationException("Open the Rotunda gameplay scene first.");
            ConfigureAirRoot(root);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, AirPath, InteractionMode.AutomatedAction);
            foreach (var chamber in root.GetComponentsInChildren<KinoAirChamber>())
            {
                chamber.round = round;
                chamber.playerView = round.playerView;
                PrefabUtility.RecordPrefabInstancePropertyModifications(chamber);
            }
            ConfigureLauncher(round.launcher);
            ConfigurePrefabLauncher(round.launcher);
            ConfigureLauncher(round.launcher);
            PrefabUtility.RecordPrefabInstancePropertyModifications(round.launcher);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Validate();
        }

        public static void ConfigureAirRoot(GameObject root)
        {
            foreach (int bay in LaunchBays)
            {
                var tube = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith($"Tube {bay:00} -", StringComparison.Ordinal));
                if (!tube)
                {
                    tube = new GameObject($"Tube {bay:00} - gameplay air").transform;
                    tube.SetParent(root.transform, false);
                    var glass = Glass(bay);
                    tube.position = new Vector3(glass.bounds.center.x, 0, glass.bounds.center.z);
                }
                foreach (var batch in tube.GetComponentsInChildren<KinoNumberBatch>(true)) Object.DestroyImmediate(batch.gameObject);
                foreach (var ball in tube.GetComponentsInChildren<KinoBallNumber>(true)) Object.DestroyImmediate(ball.gameObject);
                var chamber = tube.GetComponent<KinoAirChamber>();
                if (chamber) Object.DestroyImmediate(chamber);
                tube.name = $"Tube {bay:00} - gameplay air";
                var spawn = tube.Find("SpawnPosition");
                if (!spawn) { spawn = new GameObject("SpawnPosition").transform; spawn.SetParent(tube, false); }
                var exit = tube.Find("ExitPosition");
                if (!exit)
                {
                    exit = new GameObject("ExitPosition").transform;
                    exit.SetParent(tube, false);
                    var glass = Glass(bay);
                    // Clear the upper collar before wind bends the entire oval inward.
                    exit.position = new Vector3(spawn.position.x, glass.bounds.max.y + .4f, spawn.position.z);
                }
            }
        }

        static MeshRenderer Glass(int bay) => Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Single(r => r.name == $"Tube_{bay:00}__TubeGlass");

        public static void ConfigureLauncher(BallLauncher launcher)
        {
            var root = GameObject.Find("KINO air balls");
            if (!root) throw new InvalidOperationException("Set up the air balls before tube launching.");
            ConfigureAirRoot(root);
            Settings(launcher);
            launcher.spawnPoints = new Transform[2];
            launcher.exitPoints = new Transform[2];
            for (int i = 0; i < LaunchBays.Length; i++)
            {
                var tube = root.transform.Find($"Tube {LaunchBays[i]:00} - gameplay air");
                launcher.spawnPoints[i] = tube.Find("SpawnPosition");
                launcher.exitPoints[i] = tube.Find("ExitPosition");
            }
            EditorUtility.SetDirty(launcher);
        }

        static void Settings(BallLauncher launcher)
        {
            launcher.poolCapacity = 16;
            launcher.ballLifetime = 6;
            launcher.tubeRiseTime = 1.8f;
            FlightDefaults(launcher);
            launcher.speedVariance = .12f;
            launcher.turbulence = .35f;
        }

        public static void ConfigurePrefabLauncher(BallLauncher sceneLauncher)
        {
            var positions = sceneLauncher.spawnPoints.Select(t => t.position).ToArray();
            var exits = sceneLauncher.exitPoints.Select(t => t.position).ToArray();
            var prefab = PrefabUtility.LoadPrefabContents(GamePath);
            try
            {
                var launcher = prefab.GetComponentInChildren<BallLauncher>(true);
                Settings(launcher);
                foreach (Transform child in launcher.transform.Cast<Transform>().ToArray())
                    if (child.name.StartsWith("Launch point ", StringComparison.Ordinal) || child.name.StartsWith("Tube route ", StringComparison.Ordinal))
                        Object.DestroyImmediate(child.gameObject);
                launcher.spawnPoints = new Transform[2]; launcher.exitPoints = new Transform[2];
                for (int i = 0; i < 2; i++)
                {
                    var route = new GameObject($"Tube route {LaunchBays[i]:00}").transform;
                    route.SetParent(launcher.transform, false);
                    var spawn = new GameObject("SpawnPosition").transform; spawn.SetParent(route, false);
                    var exit = new GameObject("ExitPosition").transform; exit.SetParent(route, false);
                    spawn.position = positions[i]; exit.position = exits[i];
                    launcher.spawnPoints[i] = spawn; launcher.exitPoints[i] = exit;
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, GamePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        public static void Validate()
        {
            var launcher = Object.FindFirstObjectByType<KinoRoundController>().launcher;
            if (launcher.poolCapacity != 16 || launcher.spawnPoints.Length != 2 || launcher.exitPoints.Length != 2)
                throw new InvalidOperationException("Expected two tube routes and a 16-ball pool.");
            for (int i = 0; i < 2; i++)
            {
                var spawn = launcher.spawnPoints[i]; var exit = launcher.exitPoints[i];
                if (!spawn || spawn.name != "SpawnPosition" || !exit || exit.position.y < Glass(LaunchBays[i]).bounds.max.y + .3f ||
                    spawn.parent.GetComponentsInChildren<KinoBallNumber>(true).Length != 0 || spawn.parent.GetComponent<KinoAirChamber>())
                    throw new InvalidOperationException("Invalid or occupied launch tube " + LaunchBays[i]);
            }
            Debug.Log("KINO_TUBE_ROUTES_VALID: Tube 26 + Tube 02, exact SpawnPosition markers, clear exits, 16 pooled gameplay balls.");
        }
    }
}
