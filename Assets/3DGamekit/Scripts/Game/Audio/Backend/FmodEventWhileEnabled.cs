using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Plays an FMOD event while this GameObject is enabled, and stops it (with fade-out) when the object is
    /// disabled or destroyed. For sounds the original kit doesn't have, tied to an object that is switched off
    /// by gameplay — e.g. the weapon pedestal's idle hum on its Staff object, which is disabled when Ellen takes it.
    /// </summary>
    public class FmodEventWhileEnabled : MonoBehaviour
    {
        [Tooltip("Loop event played while this object is enabled. Leave empty until the event exists.")]
        public EventReference fmodEvent;

        EventInstance m_Instance;

        void OnEnable()
        {
            m_Instance = FmodAudio.CreateAttached(fmodEvent, gameObject);
            FmodAudio.StartAndRelease(m_Instance);
        }

        void OnDisable()
        {
            if (m_Instance.isValid())
                m_Instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            m_Instance.clearHandle();
        }
    }
}
