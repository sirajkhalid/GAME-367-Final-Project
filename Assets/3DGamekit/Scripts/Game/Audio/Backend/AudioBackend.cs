using System;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Switches what you hear between the original Unity audio and the FMOD implementation.
    ///
    /// Both systems keep running all the time; only one is audible. Unity mode mutes FMOD's
    /// master bus, FMOD mode sets AudioListener.volume to 0. Because both keep playing,
    /// you can flip back and forth mid-game (default hotkey F2) and loops stay in sync.
    /// </summary>
    public static class AudioBackend
    {
        static AudioBackendMode s_Current;

        /// <summary>Raised after the backend changes. Components can use this to enable/disable things.</summary>
        public static event Action<AudioBackendMode> Changed;

        public static AudioBackendMode Current { get { return s_Current; } }
        public static bool IsUnity { get { return s_Current == AudioBackendMode.Unity; } }
        public static bool IsFMOD { get { return s_Current == AudioBackendMode.FMOD; } }

        const string k_PrefsKey = "Gamekit3D.AudioBackend";

        /// <summary>
        /// The audio the game starts with. In the editor: the remembered choice if there is one, otherwise the
        /// settings asset's. In builds: always the asset's Build Startup Backend (every build with the same product
        /// name shares PlayerPrefs, so one build's F2 choice would otherwise carry over to the next).
        /// </summary>
        public static AudioBackendMode StartupMode
        {
            get
            {
                var settings = AudioBackendSettings.Instance;
#if UNITY_EDITOR
                if (settings.rememberLastBackend && PlayerPrefs.HasKey(k_PrefsKey))
                    return (AudioBackendMode)PlayerPrefs.GetInt(k_PrefsKey);
                return settings.startupBackend;
#else
                return settings.buildStartupBackend;
#endif
            }
        }

        /// <summary>Saves the choice so the next play session starts with it (if remembering is on; editor only).</summary>
        public static void Remember(AudioBackendMode mode)
        {
            if (!Application.isEditor || !AudioBackendSettings.Instance.rememberLastBackend)
                return;

            PlayerPrefs.SetInt(k_PrefsKey, (int)mode);
            PlayerPrefs.Save();
        }

        public static void Set(AudioBackendMode mode)
        {
            if (mode == s_Current)
                return;

            s_Current = mode;
            Remember(mode);
            AudioBackendRunner.ApplyMuting();
            AudioBackendRunner.ShowIndicator();

            if (Changed != null)
                Changed(mode);
        }

        public static void Toggle()
        {
            Set(IsUnity ? AudioBackendMode.FMOD : AudioBackendMode.Unity);
        }

        // Enter Play Mode Options skip domain reload in this project, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            s_Current = StartupMode;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateRunner()
        {
            var go = new GameObject("[AudioBackend]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AudioBackendRunner>();
        }
    }
}
