using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Manages audio for ball drop scene with 4 distinct states
    /// </summary>
    public class BallDropAudioManager : MonoBehaviour
    {
        [Header("Audio Clips")]
        [Tooltip("Music that loops while dropper moves (before space pressed)")]
        public AudioClip PreDropMusic;
        
        [Tooltip("Quick 1-second transition sound when space is pressed")]
        public AudioClip TransitionSound;
        
        [Tooltip("Music that loops while balls are falling")]
        public AudioClip FallingMusic;
        
        [Tooltip("Music that plays when drop is complete")]
        public AudioClip CompletionMusic;
        
        [Header("Audio Sources")]
        [Tooltip("Main music source (for looping tracks)")]
        public AudioSource musicSource;
        
        [Tooltip("SFX source (for one-shot sounds like transition)")]
        public AudioSource sfxSource;
        
        [Header("Settings")]
        [Tooltip("Fade duration when transitioning between tracks")]
        public float fadeDuration = 0.5f;
        
        public enum DropState
        {
            PreDrop,        // Dropper moving, waiting for space
            Transition,     // Space pressed, quick transition sound
            Falling,        // Balls actively falling
            Complete        // All done
        }
        
        private DropState currentState = DropState.PreDrop;
        private BallDropCompletionManager completionManager;
        private DropperControllerECS dropperController;
        
        private void Start()
        {
            // Find managers
            completionManager = FindObjectOfType<BallDropCompletionManager>();
            dropperController = FindObjectOfType<DropperControllerECS>();
            
            // Create audio sources if not assigned
            if (musicSource == null)
            {
                GameObject musicObj = new GameObject("MusicSource");
                musicObj.transform.SetParent(transform);
                musicSource = musicObj.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
            }
            
            if (sfxSource == null)
            {
                GameObject sfxObj = new GameObject("SFXSource");
                sfxObj.transform.SetParent(transform);
                sfxSource = sfxObj.AddComponent<AudioSource>();
                sfxSource.loop = false;
                sfxSource.playOnAwake = false;
            }
            
            // Subscribe to completion event
            if (completionManager != null)
            {
                completionManager.OnDropComplete += OnDropComplete;
            }
            
            // Wait one frame to ensure audio sources are fully initialized
            StartCoroutine(StartPreDropMusicDelayed());
            
            Debug.Log("[BallDropAudio] Initialized");
        }
        
        private System.Collections.IEnumerator StartPreDropMusicDelayed()
        {
            yield return null; // Wait one frame
            
            // Start pre-drop music directly (currentState is already PreDrop, so SetState would skip)
            Debug.Log("[BallDropAudio] Starting PreDrop music...");
            PlayMusic(PreDropMusic, loop: true);
        }
        
        private void OnDestroy()
        {
            if (completionManager != null)
            {
                completionManager.OnDropComplete -= OnDropComplete;
            }
        }
        
        private void Update()
        {
            // Monitor for transition sound completion
            if (currentState == DropState.Transition)
            {
                // Wait for transition sound to finish, then start falling music
                if (!sfxSource.isPlaying)
                {
                    SetState(DropState.Falling);
                }
            }
        }
        
        /// <summary>
        /// Called when dropping starts (space pressed)
        /// </summary>
        public void OnDropStarted()
        {
            SetState(DropState.Transition);
        }
        
        /// <summary>
        /// Change audio state
        /// </summary>
        public void SetState(DropState newState)
        {
            if (currentState == newState) return;
            
            Debug.Log($"[BallDropAudio] State: {currentState} → {newState}");
            currentState = newState;
            
            switch (newState)
            {
                case DropState.PreDrop:
                    PlayMusic(PreDropMusic, loop: true);
                    break;
                    
                case DropState.Transition:
                    // Play transition sound (one-shot)
                    if (TransitionSound != null && sfxSource != null)
                    {
                        sfxSource.PlayOneShot(TransitionSound);
                        Debug.Log("[BallDropAudio] Playing transition sound");
                    }
                    // Stop pre-drop music
                    if (musicSource != null && musicSource.isPlaying)
                    {
                        musicSource.Stop();
                    }
                    break;
                    
                case DropState.Falling:
                    PlayMusic(FallingMusic, loop: true);
                    break;
                    
                case DropState.Complete:
                    PlayMusic(CompletionMusic, loop: false);
                    break;
            }
        }
        
        /// <summary>
        /// Play music on main music source
        /// </summary>
        private void PlayMusic(AudioClip clip, bool loop)
        {
            if (clip == null)
            {
                Debug.LogWarning($"[BallDropAudio] Cannot play music - clip is null! Make sure to assign audio clips in Inspector.");
                return;
            }
            
            if (musicSource == null)
            {
                Debug.LogError($"[BallDropAudio] Cannot play music - musicSource is null!");
                return;
            }
            
            musicSource.clip = clip;
            musicSource.loop = loop;
            musicSource.Play();
            
            Debug.Log($"[BallDropAudio] Playing music: {clip.name} (loop={loop})");
        }
        
        /// <summary>
        /// Called when drop session completes
        /// </summary>
        private void OnDropComplete()
        {
            Debug.Log("[BallDropAudio] Drop complete - switching to completion music");
            SetState(DropState.Complete);
        }
        
        /// <summary>
        /// Stop all audio
        /// </summary>
        public void StopAll()
        {
            if (musicSource != null) musicSource.Stop();
            if (sfxSource != null) sfxSource.Stop();
        }
        
        /// <summary>
        /// Set music volume
        /// </summary>
        public void SetMusicVolume(float volume)
        {
            if (musicSource != null)
            {
                musicSource.volume = Mathf.Clamp01(volume);
            }
        }
        
        /// <summary>
        /// Set SFX volume
        /// </summary>
        public void SetSFXVolume(float volume)
        {
            if (sfxSource != null)
            {
                sfxSource.volume = Mathf.Clamp01(volume);
            }
        }
    }
}
