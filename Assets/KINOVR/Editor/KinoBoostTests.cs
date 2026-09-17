namespace KinoVR.Editor
{
    // Preserve batch entry points; the old timed BOOST rules have been replaced.
    public static class KinoBoostTests
    {
        public static void ValidateRules() => KinoBonusTests.ValidateRules();
        public static void Run(bool exitWhenDone = false) => KinoBonusTests.Run(exitWhenDone);
    }
}
