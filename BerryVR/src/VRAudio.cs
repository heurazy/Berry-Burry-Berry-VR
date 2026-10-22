using HarmonyLib;
using UnityEngine;

namespace BerryVR
{
    /// <summary>
    /// Keeps the audio listener on the tracked head and logs the first
    /// positional sound effects so missing 3D audio can be diagnosed from
    /// BepInEx\LogOutput.log.
    /// </summary>
    public static class VRAudio
    {
        private static int _sfxLogs;
        private static AudioListener _listener;
        private static float _nextCheck;
        private static float _lastSfxVolume = -1f;
        private static float _lastDuck = -1f;
        private static float _lastGlobalVolume = -1f;
        private static int _repairs;

        /// <summary>
        /// One enabled listener, on the left eye camera. Doppler is disabled
        /// on every source: the rig moves the camera by code (recentering,
        /// turning, following the player), which reads as huge listener
        /// velocities and can pitch sounds out of the audible range.
        /// </summary>
        public static void SetupListener(Camera eye, string scene)
        {
            if (eye == null) return;

            var all = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _listener = eye.GetComponent<AudioListener>();
            if (_listener == null) _listener = eye.gameObject.AddComponent<AudioListener>();
            if (!_listener.enabled) _listener.enabled = true;

            int disabled = 0;
            foreach (var l in all)
            {
                if (l == null || l == _listener || !l.enabled) continue;
                l.enabled = false;
                disabled++;
            }

            AudioListener.pause = false;
            if (AudioListener.volume <= 0.001f) AudioListener.volume = 1f;

            int sources = 0;
            foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s == null) continue;
                s.dopplerLevel = 0f;
                sources++;
            }

            var cfg = AudioSettings.GetConfiguration();
            BerryVRPlugin.Log.LogInfo("[VR] Audio (" + scene + "): listener on '" + eye.name + "', " +
                all.Length + " listener(s) found, " + disabled + " extra disabled, " + sources +
                " sources without doppler | volume=" + AudioListener.volume.ToString("F2") +
                " speakers=" + cfg.speakerMode + " rate=" + cfg.sampleRate);
            _sfxLogs = 0;
        }

        /// <summary>
        /// Once a second: keep exactly one enabled listener on the eye, undo a
        /// global pause, and log every change of the game's effect volumes so
        /// a silent day can be traced in the log.
        /// </summary>
        public static void Tick(Camera eye)
        {
            if (eye == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;

            bool broken = _listener == null || !_listener.isActiveAndEnabled || _listener.gameObject != eye.gameObject;
            if (!broken)
            {
                foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                {
                    if (l != null && l != _listener && l.isActiveAndEnabled) { broken = true; break; }
                }
            }
            if (broken && _repairs < 20)
            {
                _repairs++;
                BerryVRPlugin.Log.LogWarning("[VR] Audio listener lost or duplicated, repairing (#" + _repairs + ").");
                int keep = _sfxLogs;
                SetupListener(eye, "repair");
                _sfxLogs = keep;
            }

            if (AudioListener.pause)
            {
                AudioListener.pause = false;
                BerryVRPlugin.Log.LogWarning("[VR] AudioListener.pause was set; cleared it.");
            }

            if (!Mathf.Approximately(AudioListener.volume, _lastGlobalVolume))
            {
                _lastGlobalVolume = AudioListener.volume;
                BerryVRPlugin.Log.LogInfo("[VR] Audio global volume = " + _lastGlobalVolume.ToString("F2"));
            }

            var am = AudioManager.Singleton;
            if (am == null) return;
            if (!Mathf.Approximately(am.sfxVolume, _lastSfxVolume) || !Mathf.Approximately(am.duckedVolumeMult, _lastDuck))
            {
                _lastSfxVolume = am.sfxVolume;
                _lastDuck = am.duckedVolumeMult;
                bool blips = GameManager.Singleton != null && GameManager.Singleton.audioduck_BlipsAndBloops;
                BerryVRPlugin.Log.LogInfo("[VR] Audio sfxVolume=" + _lastSfxVolume.ToString("F2") +
                    " ducked=" + _lastDuck.ToString("F2") + " blipDuck=" + blips);
            }
        }

        private static void LogSfx(AudioManager am, AudioClip clip, Vector3 pos, bool priority)
        {
            if (_sfxLogs >= 25 || am == null) return;
            _sfxLogs++;

            var list = priority ? am.prioritySources : am.unimportantSources;
            int count = list != null ? list.Count : 0;
            // The index already advanced past the source that just played.
            int idx = priority ? am.prioritySource_CurrIndex : am.unimportantSource_CurrIndex;
            if (count > 0) idx = (idx - 1 + count) % count;
            AudioSource src = count > 0 && idx >= 0 && idx < count ? list[idx] : null;

            Vector3 lp = _listener != null ? _listener.transform.position : Vector3.zero;
            string s = src == null ? "no source" :
                "blend=" + src.spatialBlend.ToString("F2") + " min=" + src.minDistance.ToString("F1") +
                " max=" + src.maxDistance.ToString("F1") + " vol=" + src.volume.ToString("F2") +
                " mute=" + src.mute + " enabled=" + src.isActiveAndEnabled +
                " mixer=" + (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "none") +
                " rolloff=" + src.rolloffMode;
            BerryVRPlugin.Log.LogInfo("[VR] SFX " + (clip != null ? clip.name : "null") +
                " dist=" + Vector3.Distance(lp, pos).ToString("F2") +
                " listener=" + (_listener != null && _listener.isActiveAndEnabled) +
                " pause=" + AudioListener.pause + " sfxVolume=" + am.sfxVolume.ToString("F2") + " | " + s);
        }

        [HarmonyPatch(typeof(AudioManager), "PlayPrioritySFX")]
        private static class PatchPriority
        {
            private static void Postfix(AudioManager __instance, AudioClip _clip, Vector3 _pos)
            {
                if (VRManager.Instance != null && VRManager.Instance.Active)
                    LogSfx(__instance, _clip, _pos, true);
            }
        }

        [HarmonyPatch(typeof(AudioManager), "PlayUnimportantSFX")]
        private static class PatchUnimportant
        {
            private static void Postfix(AudioManager __instance, AudioClip _clip, Vector3 _pos)
            {
                if (VRManager.Instance != null && VRManager.Instance.Active)
                    LogSfx(__instance, _clip, _pos, false);
            }
        }
    }
}
