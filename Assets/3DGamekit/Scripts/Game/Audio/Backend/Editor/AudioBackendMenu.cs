using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Kit Tools > Audio Backend menu: choose Unity or FMOD audio, and find the settings asset.
    /// Also makes sure the settings asset exists and restores Unity's volume after play mode.
    /// </summary>
    [InitializeOnLoad]
    static class AudioBackendMenu
    {
        const string k_Menu = "Kit Tools/Audio Backend/";
        const string k_AssetFolder = "Assets/3DGamekit/Scripts/Game/Audio/Backend/Resources";
        const string k_AssetPath = k_AssetFolder + "/" + AudioBackendSettings.ResourcePath + ".asset";

        static AudioBackendMenu()
        {
            EditorApplication.delayCall += EnsureSettingsAsset;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem(k_Menu + "Unity Audio", priority = 200)]
        static void SelectUnity()
        {
            SetBackend(AudioBackendMode.Unity);
        }

        [MenuItem(k_Menu + "Unity Audio", true)]
        static bool SelectUnityValidate()
        {
            Menu.SetChecked(k_Menu + "Unity Audio", CurrentMode() == AudioBackendMode.Unity);
            return true;
        }

        [MenuItem(k_Menu + "FMOD", priority = 201)]
        static void SelectFmod()
        {
            SetBackend(AudioBackendMode.FMOD);
        }

        [MenuItem(k_Menu + "FMOD", true)]
        static bool SelectFmodValidate()
        {
            Menu.SetChecked(k_Menu + "FMOD", CurrentMode() == AudioBackendMode.FMOD);
            return true;
        }

        [MenuItem(k_Menu + "Select Settings Asset", priority = 220)]
        static void SelectSettings()
        {
            EnsureSettingsAsset();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<AudioBackendSettings>(k_AssetPath);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        static AudioBackendMode CurrentMode()
        {
            return Application.isPlaying ? AudioBackend.Current : AudioBackend.StartupMode;
        }

        // The menu sets what the game starts with; during play it also switches immediately.
        // When remembering is on, the choice is saved on this computer and the settings asset is left alone.
        static void SetBackend(AudioBackendMode mode)
        {
            if (Application.isPlaying)
            {
                AudioBackend.Set(mode);
                return;
            }

            if (AudioBackendSettings.Instance.rememberLastBackend)
            {
                AudioBackend.Remember(mode);
                return;
            }

            EnsureSettingsAsset();
            var settings = AssetDatabase.LoadAssetAtPath<AudioBackendSettings>(k_AssetPath);
            Undo.RecordObject(settings, "Set Audio Backend");
            settings.startupBackend = mode;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        static void EnsureSettingsAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioBackendSettings>(k_AssetPath) != null)
                return;

            Directory.CreateDirectory(k_AssetFolder);
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AudioBackendSettings>(), k_AssetPath);
            AssetDatabase.SaveAssets();
        }

        // AudioListener.volume isn't reset when play mode ends, so leaving play in FMOD mode
        // would keep Unity audio silent in the editor.
        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                AudioListener.volume = 1f;
        }
    }
}
