using System;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Created automatically by AudioBackend when play starts. Handles the A/B hotkey,
    /// applies muting to Unity and FMOD, and draws the on-screen indicator.
    /// </summary>
    public class AudioBackendRunner : MonoBehaviour
    {
        static AudioBackendRunner s_Instance;

        bool m_AppliedAfterBanksLoaded;
        bool m_LoggedFmodError;
        float m_IndicatorHideTime;
        GUIStyle m_GUIStyle;
        GUIStyle m_DebugStyle;

        void Awake()
        {
            s_Instance = this;
            m_GUIStyle = new GUIStyle { fontSize = 24, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            Apply();
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        void Update()
        {
            var settings = AudioBackendSettings.Instance;
            if (settings.enableHotkey && Input.GetKeyDown(settings.hotkey))
                AudioBackend.Toggle();

            // FMOD's master bus and global parameters only exist once the banks have loaded, so apply again then.
            if (!m_AppliedAfterBanksLoaded && FMODUnity.RuntimeManager.HaveAllBanksLoaded)
            {
                m_AppliedAfterBanksLoaded = true;
                Apply();
                MusicState.Resend();
                CombatTracker.Resend();
            }

            CombatTracker.Update();
            MusicState.Update();
        }

        void OnGUI()
        {
            var settings = AudioBackendSettings.Instance;

            if (settings.showMusicZone)
            {
                if (m_DebugStyle == null)
                    m_DebugStyle = new GUIStyle { fontSize = 18, normal = { textColor = Color.white } };
                string zone = MusicState.Current == MusicState.Zone ? "" : "  (zone " + MusicState.Zone + ")";
                GUI.Label(new Rect(10, Screen.height - 34, 900, 30),
                    "MusicState: " + MusicState.Current + zone + "   track: " + (SoundTrack.CurrentTrack ?? "-") +
                    "   CombatIntensity: " + CombatTracker.AwareCount, m_DebugStyle);
            }

            if (!settings.showIndicator || Time.unscaledTime > m_IndicatorHideTime)
                return;

            string text = AudioBackend.IsFMOD ? "Audio: FMOD" : "Audio: Unity";
            GUI.Label(new Rect(0, 10, Screen.width, 50), text, m_GUIStyle);
        }

        public static void ApplyMuting()
        {
            if (s_Instance != null)
                s_Instance.Apply();
        }

        public static void ShowIndicator()
        {
            if (s_Instance != null)
                s_Instance.m_IndicatorHideTime = Time.unscaledTime + AudioBackendSettings.Instance.indicatorDuration;
        }

        void Apply()
        {
            AudioListener.volume = AudioBackend.IsUnity ? 1f : 0f;

            try
            {
                FMODUnity.RuntimeManager.MuteAllEvents(!AudioBackend.IsFMOD);
            }
            catch (Exception e)
            {
                if (!m_LoggedFmodError)
                {
                    m_LoggedFmodError = true;
                    Debug.LogWarning("[AudioBackend] Could not mute/unmute FMOD. Is FMOD set up and are the banks built?\n" + e.Message);
                }
            }
        }
    }
}
