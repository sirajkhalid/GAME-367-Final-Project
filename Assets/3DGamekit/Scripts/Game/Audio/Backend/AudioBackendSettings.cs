using UnityEngine;

namespace Gamekit3D
{
    public enum AudioBackendMode
    {
        Unity,
        FMOD
    }

    /// <summary>
    /// Project-wide settings for switching between the original Unity audio and FMOD.
    /// Lives in a Resources folder so it can be loaded at runtime. Select it from
    /// Kit Tools > Audio Backend > Select Settings Asset.
    /// </summary>
    public class AudioBackendSettings : ScriptableObject
    {
        public const string ResourcePath = "AudioBackendSettings";

        [Tooltip("Which audio you hear when the game starts, unless a remembered choice exists (see below).")]
        public AudioBackendMode startupBackend = AudioBackendMode.Unity;

        [Tooltip("Start with the audio last chosen (hotkey or menu) on this computer. Saved in PlayerPrefs, not in the project.")]
        public bool rememberLastBackend = true;

        [Tooltip("Which audio a built game (.exe / .app) starts with. Builds always use this and ignore the remembered choice.")]
        public AudioBackendMode buildStartupBackend = AudioBackendMode.FMOD;

        [Tooltip("Allow switching between Unity and FMOD audio while playing.")]
        public bool enableHotkey = true;
        public KeyCode hotkey = KeyCode.F2;

        [Tooltip("Briefly show which audio is active after switching.")]
        public bool showIndicator = true;
        public float indicatorDuration = 2f;

        [Tooltip("Show MusicState (zone / Boss), the Unity music track and CombatIntensity in the corner while playing.")]
        public bool showMusicZone = true;

        [Tooltip("Log a warning (once each) when an assigned FMOD event or parameter can't be found.")]
        public bool warnOnMissingEvents = true;

        static AudioBackendSettings s_Instance;
        static AudioBackendSettings s_Defaults;

        public static AudioBackendSettings Instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = Resources.Load<AudioBackendSettings>(ResourcePath);

                // Asset missing: use defaults, but keep looking for the asset next time.
                if (s_Instance == null)
                {
                    if (s_Defaults == null)
                        s_Defaults = CreateInstance<AudioBackendSettings>();
                    return s_Defaults;
                }
                return s_Instance;
            }
        }
    }
}
