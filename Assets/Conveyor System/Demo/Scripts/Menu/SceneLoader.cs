using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Loads the demo scenes. Uses the Build Settings when the scene is listed there; in the Editor it also
    /// works without that, so the package scenes run straight after import.
    /// </summary>
    public static class SceneLoader
    {
        /// <summary>
        /// Name of the menu scene.
        /// </summary>
        public const string MenuScene = "Menu";

        /// <summary>
        /// Project path of the menu scene (used in the Editor when it is not in the Build Settings).
        /// </summary>
        public const string MenuScenePath = "Assets/Conveyor System/Menu.unity";

        /// <summary>
        /// Loads a scene by name, falling back to its asset path in the Editor.
        /// </summary>
        public static void Load(string sceneName, string scenePath)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SceneManager.LoadScene(sceneName);
                return;
            }

#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(scenePath))
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            Debug.LogWarning($"[Conveyor System] Scene '{sceneName}' is not in the Build Settings. Add it via File > Build Profiles to load it in a build.");
        }

        /// <summary>
        /// Returns to the menu scene.
        /// </summary>
        public static void LoadMenu() => Load(MenuScene, MenuScenePath);

        /// <summary>
        /// Quits the application (stops Play mode in the Editor).
        /// </summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
