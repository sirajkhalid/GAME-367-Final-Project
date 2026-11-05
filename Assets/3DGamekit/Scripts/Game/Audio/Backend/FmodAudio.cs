using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Small helpers for playing FMOD events from the kit's audio hooks.
    ///
    /// - An event reference that hasn't been assigned yet is silently skipped, so the game runs
    ///   fine before students have made their events.
    /// - An assigned event or parameter that can't be found logs one warning, not one per frame.
    /// - Events play in both modes; AudioBackend just mutes FMOD while in Unity mode.
    /// </summary>
    public static class FmodAudio
    {
        static readonly HashSet<string> s_Warned = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Warned.Clear();
        }

        /// <summary>Plays a one-shot event at a position. Returns false if nothing was played.</summary>
        public static bool PlayOneShot(EventReference eventRef, Vector3 position, params (string name, float value)[] parameters)
        {
            var instance = CreateInstance(eventRef);
            if (!instance.isValid())
                return false;

            instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
            StartAndRelease(instance, parameters);
            return true;
        }

        /// <summary>Plays a one-shot event that follows a GameObject. Returns false if nothing was played.</summary>
        public static bool PlayOneShotAttached(EventReference eventRef, GameObject gameObject, params (string name, float value)[] parameters)
        {
            var instance = CreateAttached(eventRef, gameObject);
            if (!instance.isValid())
                return false;

            StartAndRelease(instance, parameters);
            return true;
        }

        /// <summary>
        /// Creates an event instance you control yourself (for loops, or sounds you need to stop).
        /// Check instance.isValid() before using it; it is invalid if the event is unassigned or missing.
        /// </summary>
        public static EventInstance CreateInstance(EventReference eventRef)
        {
            if (eventRef.IsNull)
                return default(EventInstance);

            try
            {
                return RuntimeManager.CreateInstance(eventRef);
            }
            catch (EventNotFoundException)
            {
                Warn(eventRef.ToString(), "[FMOD] Event not found: " + eventRef + ". Has it been created in FMOD Studio, assigned to a bank, and have the banks been built?");
                return default(EventInstance);
            }
        }

        /// <summary>Creates an event instance that follows a GameObject.</summary>
        public static EventInstance CreateAttached(EventReference eventRef, GameObject gameObject)
        {
            var instance = CreateInstance(eventRef);
            if (instance.isValid())
                RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
            return instance;
        }

        /// <summary>Sets a local parameter. Warns once if the event doesn't have it.</summary>
        public static void SetParameter(EventInstance instance, string name, float value)
        {
            if (!instance.isValid())
                return;

            if (instance.setParameterByName(name, value) != FMOD.RESULT.OK)
            {
                string eventPath = GetPath(instance);
                Warn(eventPath + "|" + name, "[FMOD] Parameter '" + name + "' not found on " + eventPath + ".");
            }
        }

        /// <summary>True if the instance's event has a parameter with this name.</summary>
        public static bool HasParameter(EventInstance instance, string name)
        {
            EventDescription description;
            PARAMETER_DESCRIPTION parameter;
            return instance.isValid()
                && instance.getDescription(out description) == FMOD.RESULT.OK
                && description.getParameterDescriptionByName(name, out parameter) == FMOD.RESULT.OK;
        }

        /// <summary>Sets a labeled parameter by label name (e.g. Surface = "Grass"). Warns once if the parameter or label is missing.</summary>
        public static void SetParameterLabel(EventInstance instance, string name, string label)
        {
            if (!instance.isValid())
                return;

            if (instance.setParameterByNameWithLabel(name, label) != FMOD.RESULT.OK)
            {
                string eventPath = GetPath(instance);
                Warn(eventPath + "|" + name + "|" + label, "[FMOD] Couldn't set '" + name + "' to label '" + label + "' on " + eventPath + ". Does the event have a labeled parameter '" + name + "' with that label?");
            }
        }

        /// <summary>Sets a global parameter. Warns once if it doesn't exist.</summary>
        public static void SetGlobalParameter(string name, float value)
        {
            if (RuntimeManager.StudioSystem.setParameterByName(name, value) != FMOD.RESULT.OK)
                Warn("global|" + name, "[FMOD] Global parameter '" + name + "' not found.");
        }

        /// <summary>Sets a global labeled parameter by label name. Warns once if the parameter or label is missing.</summary>
        public static void SetGlobalParameterLabel(string name, string label)
        {
            if (RuntimeManager.StudioSystem.setParameterByNameWithLabel(name, label) != FMOD.RESULT.OK)
                Warn("global|" + name + "|" + label, "[FMOD] Couldn't set global parameter '" + name + "' to label '" + label + "'. Is there a global labeled parameter '" + name + "' with that label, and have the banks been built?");
        }

        /// <summary>Sets a VCA's volume (linear, 0-1). Warns once if the VCA doesn't exist.</summary>
        public static void SetVCAVolume(string path, float volume)
        {
            VCA vca;
            if (RuntimeManager.StudioSystem.getVCA(path, out vca) == FMOD.RESULT.OK)
                vca.setVolume(volume);
            else
                Warn("vca|" + path, "[FMOD] VCA not found: " + path + ". Create it in FMOD Studio's Mixer, assign buses to it, and build.");
        }

        /// <summary>Stops an instance (with fade-out by default) and releases it.</summary>
        public static void StopAndRelease(ref EventInstance instance, bool allowFadeout = true)
        {
            if (!instance.isValid())
                return;

            instance.stop(allowFadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
            instance.release();
            instance.clearHandle();
        }

        /// <summary>Starts a one-shot instance and releases it, so FMOD frees it when it finishes.</summary>
        public static void StartAndRelease(EventInstance instance)
        {
            if (!instance.isValid())
                return;

            instance.start();
            instance.release();
        }

        static void StartAndRelease(EventInstance instance, (string name, float value)[] parameters)
        {
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                    SetParameter(instance, parameters[i].name, parameters[i].value);
            }

            StartAndRelease(instance);
        }

        static string GetPath(EventInstance instance)
        {
            EventDescription description;
            string path;
            if (instance.getDescription(out description) == FMOD.RESULT.OK && description.getPath(out path) == FMOD.RESULT.OK)
                return path;
            return "(unknown event)";
        }

        static void Warn(string key, string message)
        {
            if (AudioBackendSettings.Instance.warnOnMissingEvents && s_Warned.Add(key))
                Debug.LogWarning(message);
        }
    }
}
