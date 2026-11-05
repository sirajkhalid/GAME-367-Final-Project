using System;
using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using Random = UnityEngine.Random;


namespace Gamekit3D
{
    [RequireComponent(typeof(AudioSource))]
    public class RandomAudioPlayer : MonoBehaviour
    {
        public const string FmodSurfaceParameter = "Surface";

        [Serializable]
        public class MaterialAudioOverride
        {
            public Material[] materials;
            public SoundBank[] banks;
            [Tooltip("FMOD: label sent to the 'Surface' parameter when one of these materials is used.")]
            public string fmodSurface;
        }

        [Serializable]
        public class SoundBank
        {
            public string name;
            public AudioClip[] clips;
        }

        public bool randomizePitch = true;
        public float pitchRandomRange = 0.2f;
        public float playDelay = 0;
        public SoundBank defaultBank = new SoundBank();
        public MaterialAudioOverride[] overrides;

        [Header("FMOD")]
        [Tooltip("Played alongside the Unity clips every time this player plays. Leave empty until the event exists.")]
        public EventReference fmodEvent;
        [Tooltip("Extra events played at the same time as Fmod Event, e.g. a VO event alongside an SFX event. " +
                 "They get the same parameters, but only if they have them (no warning if they don't).")]
        public EventReference[] fmodAdditionalEvents;
        [Tooltip("Label sent to the 'Surface' parameter when no material override matches. Leave empty to not send Surface.")]
        public string fmodDefaultSurface;

        [HideInInspector]
        public bool playing;
        [HideInInspector]
        public bool canPlay;

        protected AudioSource m_Audiosource;
        protected Dictionary<Material, MaterialAudioOverride> m_Lookup = new Dictionary<Material, MaterialAudioOverride>();
        protected Dictionary<string, float> m_FmodParameters = new Dictionary<string, float>();
        protected List<EventInstance> m_LastFmodInstances = new List<EventInstance>();

        public AudioSource audioSource { get { return m_Audiosource; } }

        public AudioClip clip { get; private set; }

        void Awake()
        {
            m_Audiosource = GetComponent<AudioSource>();
            for (int i = 0; i < overrides.Length; i++)
            {
                foreach (var material in overrides[i].materials)
                    m_Lookup[material] = overrides[i];
            }
        }

        /// <summary>
        /// Sets an FMOD parameter value that is sent with every event this player plays from now on
        /// (e.g. Ellen's footsteps send "Speed"). Call it before PlayRandomClip.
        /// </summary>
        public void SetFmodParameter(string name, float value)
        {
            m_FmodParameters[name] = value;
        }

        /// <summary>
        /// Stops the Unity clip and the most recent FMOD event(s) from this player.
        /// FMOD is allowed to fade out, so an AHDSR release on the event is respected.
        /// </summary>
        public void Stop()
        {
            if (m_Audiosource != null)
                m_Audiosource.Stop();

            foreach (var instance in m_LastFmodInstances)
            {
                if (instance.isValid())
                    instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            }
        }

        /// <summary>
        /// Will pick a random clip to play in the assigned list. If you pass a material, it will try to find an
        /// override for that materials or play the default clip if none can ben found.
        /// </summary>
        /// <param name="overrideMaterial"></param>
        /// <returns> Return the choosen audio clip, null if none </returns>
        public AudioClip PlayRandomClip(Material overrideMaterial, int bankId = 0)
        {
#if UNITY_EDITOR
            //UnityEditor.EditorGUIUtility.PingObject(overrideMaterial);
#endif
            if (overrideMaterial == null) return null;
            return InternalPlayRandomClip(overrideMaterial, bankId);
        }

        /// <summary>
        /// Will pick a random clip to play in the assigned list.
        /// </summary>
        public void PlayRandomClip()
        {
            clip = InternalPlayRandomClip(null, bankId: 0);
        }

        AudioClip InternalPlayRandomClip(Material overrideMaterial, int bankId)
        {
            MaterialAudioOverride matched = null;
            var bank = defaultBank;
            if (overrideMaterial != null)
                if (m_Lookup.TryGetValue(overrideMaterial, out matched))
                    if (bankId < matched.banks.Length)
                        bank = matched.banks[bankId];

            PlayFmodEvent(matched);

            if (bank.clips == null || bank.clips.Length == 0)
                return null;
            var clip = bank.clips[Random.Range(0, bank.clips.Length)];

            if (clip == null)
                return null;

            m_Audiosource.pitch = randomizePitch ? Random.Range(1.0f - pitchRandomRange, 1.0f + pitchRandomRange) : 1.0f;
            m_Audiosource.clip = clip;
            m_Audiosource.PlayDelayed(playDelay);

            return clip;
        }

        // FMOD's version of the same play: the main event plus any additional events, with the surface and
        // any parameters set by the caller. Variation (which clip, pitch) is left to the FMOD events.
        void PlayFmodEvent(MaterialAudioOverride matched)
        {
            m_LastFmodInstances.Clear();
            string surface = matched != null ? matched.fmodSurface : fmodDefaultSurface;

            PlayOneFmodEvent(fmodEvent, surface, onlyIfPresent: false);

            if (fmodAdditionalEvents != null)
            {
                foreach (var additionalEvent in fmodAdditionalEvents)
                    PlayOneFmodEvent(additionalEvent, surface, onlyIfPresent: true);
            }
        }

        // onlyIfPresent: skip parameters the event doesn't have, instead of warning about them.
        void PlayOneFmodEvent(EventReference eventRef, string surface, bool onlyIfPresent)
        {
            EventInstance instance = FmodAudio.CreateAttached(eventRef, gameObject);
            if (!instance.isValid())
                return;

            if (!string.IsNullOrEmpty(surface) && (!onlyIfPresent || FmodAudio.HasParameter(instance, FmodSurfaceParameter)))
                FmodAudio.SetParameterLabel(instance, FmodSurfaceParameter, surface);

            foreach (var parameter in m_FmodParameters)
            {
                if (!onlyIfPresent || FmodAudio.HasParameter(instance, parameter.Key))
                    FmodAudio.SetParameter(instance, parameter.Key, parameter.Value);
            }

            // Kept so Stop() can cut it off. The handle stays valid until FMOD frees the finished event.
            m_LastFmodInstances.Add(instance);

            if (playDelay > 0 && isActiveAndEnabled)
                StartCoroutine(StartFmodEventDelayed(instance));
            else
                FmodAudio.StartAndRelease(instance);
        }

        IEnumerator StartFmodEventDelayed(EventInstance instance)
        {
            yield return new WaitForSeconds(playDelay);
            FmodAudio.StartAndRelease(instance);
        }

    }
}
