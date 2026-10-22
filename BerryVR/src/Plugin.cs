using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace BerryVR
{
    [BepInPlugin("berryvr.plugin", "Berry VR", "1.0.0")]
    public class BerryVRPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            VRSettings.Bind(Config);
            Log.LogInfo("Berry VR 1.0.0 | Unity " + Application.unityVersion + " | " + Application.productName);
            Log.LogInfo("[VR] Loading gameplay patches.");
            VRPatches.Apply();
            gameObject.AddComponent<VRManager>();
            Log.LogInfo("[VR] Ready. Start SteamVR before launching for VR mode; flat mode stays available otherwise.");
        }
    }
}
