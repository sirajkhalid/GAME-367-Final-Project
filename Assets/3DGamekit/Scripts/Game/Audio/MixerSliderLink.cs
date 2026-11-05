using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;

namespace Gamekit3D
{
    [RequireComponent(typeof(Slider))]
    public class MixerSliderLink : MonoBehaviour
    {
        public AudioMixer mixer;
        public string mixerParameter;

        [Header("FMOD")]
        [Tooltip("FMOD VCA this slider also controls. Leave empty to use the mixer parameter's name without \"Volume\" " +
                 "(MasterVolume -> vca:/Master, MusicVolume -> vca:/Music, SFXVolume -> vca:/SFX).")]
        public string fmodVCA;

        protected Slider m_Slider;
        
        void Awake ()
        {
            m_Slider = GetComponent<Slider>();

            float value;
            mixer.GetFloat(mixerParameter, out value);

            m_Slider.value = AudioDBHelper.DecibelsToLinear(value);

            m_Slider.onValueChanged.AddListener(SliderValueChange);
        }


        void SliderValueChange(float value)
        {
            mixer.SetFloat(mixerParameter, AudioDBHelper.LinearToDecibels(value));

            // VCA volume is linear, like the slider. Only set when the slider moves, so opening the menu
            // doesn't change what you hear.
            FmodAudio.SetVCAVolume(FmodVCAPath, value);
        }

        string FmodVCAPath
        {
            get
            {
                if (!string.IsNullOrEmpty(fmodVCA))
                    return fmodVCA;
                return "vca:/" + mixerParameter.Replace("Volume", "");
            }
        }
    }
}