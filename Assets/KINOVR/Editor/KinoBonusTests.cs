using UnityEditor;

namespace KinoVR.Editor
{
    // Keep existing menu/batch callers on the full normal/bonus/Second Chance sequence.
    public static class KinoBonusTests
    {
        [MenuItem("Tools/KINO VR/KINO Bonus/2 - Test full round sequence")]
        public static void RunMenu() => Run();
        public static void ValidateRules() => KinoSecondChanceTests.ValidateRules();
        public static void ValidateRulesAndAssets() => KinoSecondChanceTests.ValidateRulesAndAssets();
        public static void Run(bool exitWhenDone = false) => KinoSecondChanceTests.Run(exitWhenDone);
    }
}
