using BepInEx.Configuration;

namespace BerryVR
{
    public enum TurnMode
    {
        Smooth,
        Snap
    }

    /// <summary>
    /// Player-facing VR options, stored in BepInEx\config\berryvr.plugin.cfg.
    /// </summary>
    public static class VRSettings
    {
        public static ConfigEntry<TurnMode> Turn;
        public static ConfigEntry<float> SmoothTurnSpeed;
        public static ConfigEntry<float> SnapTurnAngle;
        public static ConfigEntry<bool> ComfortVignette;
        public static ConfigEntry<float> VignetteStrength;
        public static ConfigEntry<bool> Haptics;
        public static ConfigEntry<bool> CartoonGloves;
        public static ConfigEntry<float> GloveScale;
        public static ConfigEntry<float> HeightOffset;
        public static ConfigEntry<float> GunOffsetRight;
        public static ConfigEntry<float> GunOffsetUp;
        public static ConfigEntry<float> GunOffsetForward;
        public static ConfigEntry<bool> DebugCaptures;
        public static ConfigEntry<bool> FingerTouchUI;

        public static void Bind(ConfigFile cfg)
        {
            Turn = cfg.Bind("Comfort", "TurnMode", TurnMode.Smooth,
                "Right stick turning: Smooth (continuous) or Snap (fixed steps).");
            SmoothTurnSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 140f,
                new ConfigDescription("Smooth turn speed in degrees per second at full stick.",
                    new AcceptableValueRange<float>(30f, 360f)));
            SnapTurnAngle = cfg.Bind("Comfort", "SnapTurnAngle", 45f,
                new ConfigDescription("Snap turn step in degrees.", new AcceptableValueRange<float>(10f, 90f)));
            ComfortVignette = cfg.Bind("Comfort", "Vignette", false,
                "Darken the edges of the view while moving or turning with the sticks (reduces motion sickness).");
            VignetteStrength = cfg.Bind("Comfort", "VignetteStrength", 0.65f,
                new ConfigDescription("How much of the periphery the vignette covers at full speed (0-1).",
                    new AcceptableValueRange<float>(0f, 1f)));
            Haptics = cfg.Bind("Feel", "Haptics", true,
                "Controller vibrations when grabbing, releasing and pointing at usable objects.");
            CartoonGloves = cfg.Bind("Visuals", "CartoonGloves", true,
                "Show cartoon gloves driven by SteamVR finger tracking instead of plain controller blocks.");
            HeightOffset = cfg.Bind("Comfort", "HeightOffset", 0f,
                new ConfigDescription("Raise (+) or lower (-) your in-game eye height in meters. Useful when playing seated.",
                    new AcceptableValueRange<float>(-0.6f, 0.8f)));
            FingerTouchUI = cfg.Bind("Feel", "FingerTouchUI", true,
                "Press in-world screens (the PC, ...) by touching them with your index finger.");
            var cm = new AcceptableValueRange<float>(-20f, 20f);
            GunOffsetRight = cfg.Bind("Visuals", "GunOffsetRight", -2f,
                new ConfigDescription("Pop gun position in the right hand, in cm: + moves it right, - left.", cm));
            GunOffsetUp = cfg.Bind("Visuals", "GunOffsetUp", -1.5f,
                new ConfigDescription("Pop gun position in the right hand, in cm: + moves it up, - down.", cm));
            GunOffsetForward = cfg.Bind("Visuals", "GunOffsetForward", 0f,
                new ConfigDescription("Pop gun position in the right hand, in cm: + moves it forward, - back.", cm));
            DebugCaptures = cfg.Bind("Debug", "GunCaptures", false,
                "Save a picture of the left eye (BepInEx/BerryVR_gunN.png) the first 3 times the pop gun is shown.");
            GloveScale = cfg.Bind("Visuals", "GloveScale", 1f,
                new ConfigDescription("Glove finger and palm thickness multiplier.",
                    new AcceptableValueRange<float>(0.6f, 1.6f)));
        }
    }
}
