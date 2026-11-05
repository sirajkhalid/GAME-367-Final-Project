using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Gamekit3D
{
    /// <summary>
    /// The last opened scene is remembered in Library/UserSettings, which aren't in student zips or git.
    /// On a fresh copy Unity opens an empty "Untitled" scene, so open Level1 (where the audio work happens) instead.
    /// Only does this when the editor starts with an unsaved, unchanged scene, so it never replaces a scene someone chose.
    /// </summary>
    [InitializeOnLoad]
    static class OpenLevel1OnFirstLoad
    {
        const string k_ScenePath = "Assets/3DGamekit/Scenes/GamePlay/Level1.unity";
        const string k_SessionKey = "Gamekit3D.OpenLevel1OnFirstLoad.Checked";

        static OpenLevel1OnFirstLoad()
        {
            EditorApplication.delayCall += OpenLevel1IfUntitled;
        }

        static void OpenLevel1IfUntitled()
        {
            // Once per editor session; later domain reloads (script changes) don't check again.
            if (SessionState.GetBool(k_SessionKey, false))
                return;
            SessionState.SetBool(k_SessionKey, true);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene scene = SceneManager.GetActiveScene();
            bool isUntitled = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(scene.path) && !scene.isDirty;
            if (isUntitled && AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) != null)
                EditorSceneManager.OpenScene(k_ScenePath);
        }
    }
}
