using UnityEngine;
using UnityEngine.SceneManagement;

namespace Vampire
{
    /// <summary>
    /// Helper class for loading comic scenes from anywhere in the game
    /// </summary>
    public static class ComicSceneLoader
    {
        /// <summary>
        /// Load a comic sequence by passing the config
        /// </summary>
        /// <param name="comicConfig">The comic sequence config to play</param>
        public static void LoadComic(ComicSequenceConfig comicConfig)
        {
            if (comicConfig == null)
            {
                Debug.LogError("[ComicSceneLoader] Cannot load null comic config!");
                return;
            }
            
            ComicSceneManager.CurrentSequence = comicConfig;
            Debug.Log($"[ComicSceneLoader] Loading comic '{comicConfig.sequenceName}' → Will return to '{comicConfig.nextSceneName}'");
            SceneManager.LoadScene("ComicScene");
        }
    }
}
