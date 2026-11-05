using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gamekit3D
{
    public class SoundTrack : MonoBehaviour
    {
        public float soundTrackVolume = 1;
        public float initialVolume = 1;
        public float volumeRampSpeed = 4;
        public bool playOnStart = true;
        public AudioSource[] audioSources;

        [Header("FMOD")]
        [Tooltip("One music event for the whole level. Plays alongside the Unity tracks and keeps playing. The zone is " +
                 "sent to the GLOBAL labeled parameter \"MusicState\" (see MusicState): the first Audio Source above is " +
                 "\"Main\", the rest are \"Zone01\", \"Zone02\"... in list order. \"Boss\" overrides the zone.")]
        public FMODUnity.EventReference fmodMusicEvent;

        /// <summary>The Unity track currently playing, for the debug display.</summary>
        public static string CurrentTrack { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CurrentTrack = null;
        }

        AudioSource activeAudio, fadeAudio;
        FMOD.Studio.EventInstance m_FmodMusic;
        float volumeVelocity, fadeVelocity;
        float volume;
        Stack<string> trackStack = new Stack<string>();

        public void PushTrack(string name)
        {
            trackStack.Push(name);
            Enqueue(name);
        }

        public void PopTrack()
        {
            if (trackStack.Count > 1)
                trackStack.Pop();
            Enqueue(trackStack.Peek());
        }

        public void Enqueue(string name)
        {
            foreach (var i in audioSources)
            {
                if (i.name == name)
                {
                    fadeAudio = activeAudio;
                    activeAudio = i;
                    if (!activeAudio.isPlaying) activeAudio.Play();
                    SetFmodZone();
                    break;
                }
            }
        }

        public void Play()
        {
            if (activeAudio != null)
                activeAudio.Play();

            if (!m_FmodMusic.isValid())
            {
                m_FmodMusic = FmodAudio.CreateInstance(fmodMusicEvent);
                SetFmodZone();
                FmodAudio.StartAndRelease(m_FmodMusic);
            }
        }

        public void Stop()
        {
            foreach (var i in audioSources) i.Stop();
            FmodAudio.StopAndRelease(ref m_FmodMusic);
        }

        void OnEnable()
        {
            trackStack.Clear();
            if (audioSources.Length > 0)
            {
                activeAudio = audioSources[0];
                foreach (var i in audioSources) i.volume = 0;
                trackStack.Push(audioSources[0].name);
                SetFmodZone();
                if (playOnStart) Play();
            }
            volume = initialVolume;
        }

        void OnDisable()
        {
            FmodAudio.StopAndRelease(ref m_FmodMusic);
        }

        // "Main" for the first track, "Zone01", "Zone02"... for the rest, in the order of audioSources.
        void SetFmodZone()
        {
            int index = System.Array.IndexOf(audioSources, activeAudio);
            if (index < 0)
                return;

            CurrentTrack = activeAudio.name;
            MusicState.SetZone(index == 0 ? MusicState.Main : "Zone" + index.ToString("00"));
        }

        void Reset()
        {
            audioSources = GetComponentsInChildren<AudioSource>();
        }

        public void SetVolume(float volume)
        {
            this.volume = volume;
        }

        void Update()
        {
            if (activeAudio != null)
                activeAudio.volume = Mathf.SmoothDamp(activeAudio.volume, volume * soundTrackVolume, ref volumeVelocity, volumeRampSpeed, 1);

            if (fadeAudio != null)
            {
                fadeAudio.volume = Mathf.SmoothDamp(fadeAudio.volume, 0, ref fadeVelocity, volumeRampSpeed, 1);
                if (Mathf.Approximately(fadeAudio.volume, 0))
                {
                    fadeAudio.Stop();
                    fadeAudio = null;
                }
            }
        }
    }
}
