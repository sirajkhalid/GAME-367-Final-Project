using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Plays an FMOD event whenever the AudioSource on this GameObject plays, without changing the game code
    /// that drives the AudioSource (UnityEvents, Play On Awake, scripts...).
    ///
    /// - Starts the event when the source starts playing (or restarts, for one-shots played again).
    /// - Looping sources: the event is stopped when the source stops, its clip is swapped, or this object is disabled.
    /// - One-shots: the event plays to its own end, unless the source is stopped early.
    /// - The event follows this GameObject. Stops allow fade-out, so an AHDSR release is respected.
    ///
    /// Add mirrors in bulk with Kit Tools > Audio Backend > Add FMOD Mirrors.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FmodAudioSourceMirror : MonoBehaviour
    {
        [Tooltip("Played whenever this AudioSource starts playing. Leave empty until the event exists.")]
        public EventReference fmodEvent;

        // A one-shot whose last seen play position was this close to the end of its clip finished on its own.
        const float k_EndTolerance = 0.25f;

        AudioSource m_Source;
        EventInstance m_Instance;
        AudioClip m_LastClip;
        bool m_WasPlaying;
        float m_LastTime;

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
        }

        void OnEnable()
        {
            m_WasPlaying = false;
            m_LastTime = 0f;
            m_LastClip = m_Source.clip;
        }

        void OnDisable()
        {
            // Unity stops an AudioSource when its GameObject is disabled, so do the same.
            StopEvent();
        }

        // LateUpdate so Play/Stop calls made anywhere during this frame are already visible.
        void LateUpdate()
        {
            bool playing = m_Source.isPlaying;
            float time = m_Source.time;

            if (m_Source.clip != m_LastClip)
            {
                // Swapping the clip stops the Unity source (the Level1 switches do this when activated).
                StopEvent();
                m_LastClip = m_Source.clip;
                m_WasPlaying = false;
            }

            if (playing && !m_WasPlaying)
                StartEvent();
            else if (playing && !m_Source.loop && time < m_LastTime)
                StartEvent(); // Play() again while still playing restarts a one-shot
            else if (!playing && m_WasPlaying && StoppedEarly())
                StopEvent();

            m_WasPlaying = playing;
            m_LastTime = time;
        }

        bool StoppedEarly()
        {
            if (m_Source.loop || m_LastClip == null)
                return true;

            return m_LastTime < m_LastClip.length - k_EndTolerance;
        }

        void StartEvent()
        {
            // A restart cuts the previous one, as it does in Unity.
            StopEvent();

            m_Instance = FmodAudio.CreateAttached(fmodEvent, gameObject);
            FmodAudio.StartAndRelease(m_Instance);
        }

        void StopEvent()
        {
            // The handle stays valid until FMOD frees the finished event, so a finished one-shot is skipped here.
            if (m_Instance.isValid())
                m_Instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            m_Instance.clearHandle();
        }
    }
}
