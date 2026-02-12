using UnityEngine;
using System.Collections.Generic;

namespace Vampire
{
    /// <summary>
    /// Complete comic sequence configuration
    /// Create one of these for each comic story (Intro, AfterGoingOutside, etc.)
    /// Right-click in Project → Create → Vampire → Comic Sequence Config
    /// </summary>
    [CreateAssetMenu(fileName = "NewComicSequence", menuName = "Vampire/Comic Sequence Config", order = 1)]
    public class ComicSequenceConfig : ScriptableObject
    {
        [Header("Sequence Info")]
        [Tooltip("Identifier for this comic sequence (e.g., 'intro', 'after_going_outside')")]
        public string sequenceId = "new_sequence";
        
        [Tooltip("Display name for debugging")]
        public string sequenceName = "New Comic Sequence";
        
        [Header("Scenes")]
        [Tooltip("All panels/scenes in this comic sequence")]
        public List<ComicPanel> panels = new List<ComicPanel>();
        
        [Header("Transition")]
        [Tooltip("Scene to load after this comic completes")]
        public string nextSceneName = "FPS_Collect";
        
        [Header("Audio (Optional)")]
        [Tooltip("Background music for entire sequence (loops)")]
        public AudioClip sequenceMusic;
        
        [Tooltip("Volume for sequence music")]
        [Range(0f, 1f)]
        public float musicVolume = 0.5f;
    }
}
