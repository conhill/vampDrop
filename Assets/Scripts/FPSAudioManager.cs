using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Vampire.Player
{
    /// <summary>
    /// Manages all audio for the FPS scene including background music,
    /// footsteps, and rice collection sounds
    /// </summary>
    public class FPSAudioManager : MonoBehaviour
    {
        [Header("Background Music")]
        [Tooltip("Background music that loops during FPS gameplay")]
        public AudioClip backgroundMusic;
        
        [Header("Footstep Sounds")]
        [Tooltip("Looping walking sound on regular surfaces (6 seconds)")]
        public AudioClip walkingSound;
        
        [Tooltip("Looping walking sound when stepping on rice (16 seconds)")]
        public AudioClip walkingOnRiceSound;
        
        [Header("Jump Sounds")]
        [Tooltip("Sound when player starts a jump")]
        public AudioClip jumpSound;
        
        [Tooltip("Sound when player lands after jumping")]
        public AudioClip landSound;
        
        [Header("Rice Collection Sounds")]
        [Tooltip("Sound when picking up a single rice grain")]
        public AudioClip ricePickupSound;
        
        [Tooltip("Sound for multi-pickup (when collecting multiple rice at once)")]
        public AudioClip multiPickupSound;
        
        [Header("Audio Sources")]
        [Tooltip("Music source for background music")]
        public AudioSource musicSource;
        
        [Tooltip("Footstep audio source")]
        public AudioSource footstepSource;
        
        [Tooltip("SFX source for rice pickup sounds")]
        public AudioSource sfxSource;
        
        [Header("Volume Controls")]
        [Tooltip("Master volume (affects all audio)")]
        [Range(0f, 1f)]
        public float masterVolume = 1.0f;
        
        [Tooltip("Background music volume")]
        [Range(0f, 1f)]
        public float musicVolume = 0.5f;
        
        [Tooltip("Footstep sounds volume")]
        [Range(0f, 1f)]
        public float footstepVolume = 0.7f;
        
        [Tooltip("Rice pickup and SFX volume")]
        [Range(0f, 1f)]
        public float sfxVolume = 0.8f;
        
        [Header("Surface Detection")]
        [Tooltip("Check radius for detecting rice beneath player")]
        public float riceDetectionRadius = 1.0f;
        
        [Tooltip("Layer mask for rice objects (set to specific rice layer, NOT -1)")]
        public LayerMask riceLayerMask = (1 << 8); // Layer 8 by default - change this in Inspector!
        
        [Tooltip("Fallback: also check for objects with this tag if layer detection fails")]
        public string riceTag = "Rice";
        
        [Tooltip("Minimum rice objects needed to count as 'walking on rice'")]
        public int minRiceCountForWalking = 5;
        
        [Tooltip("Only check directly below player (not around)")]
        public bool onlyCheckDirectlyBelow = true;
        
        [Header("Footstep Fade")]
        [Tooltip("Seconds to fade footsteps in when starting to walk")]
        [Range(0.05f, 1f)]
        public float footstepFadeInTime  = 0.1f;
        [Tooltip("Seconds to fade footsteps out when stopping")]
        [Range(0.05f, 1f)]
        public float footstepFadeOutTime = 0.2f;
        [Tooltip("How long you must hold W before footsteps reach full volume. Short taps stay quiet.")]
        [Range(0.1f, 2f)]
        public float walkFullVolumeTime = 0.75f;

        [Header("Performance Settings")]
        [Tooltip("Enable performance profiling logs")]
        public bool enablePerformanceProfiling = true;
        
        [Tooltip("How often to check for rice (in frames). Higher = better performance")]
        public int riceCheckInterval = 10;
        
        [Tooltip("Disable expensive ECS rice detection for performance")]
        public bool disableECSRiceDetection = false;
        
        [Tooltip("DEBUGGING: Completely disable rice audio (always use ground sound)")]
        public bool forceGroundAudio = false;
        [Tooltip("Volume multiplier for regular ground walking (since it's very low)")]
        [Range(0f, 3f)]
        public float groundWalkingVolumeBoost = 1.0f;
        
        [Tooltip("Volume multiplier for rice walking (since it's a little loud)")]
        [Range(0f, 3f)]
        public float riceWalkingVolumeBoost = 0.7f;
        

        // Private variables
        private bool isWalkingOnRice;
        private bool isCurrentlyWalking = false;
        private bool wasWalkingLastFrame = false;
        private bool wasGroundedLastFrame = true;
        private float lastMasterVolume;
        private float lastMusicVolume;
        private float lastFootstepVolume;
        private float lastSFXVolume;

        // Footstep fade state
        private float _currentFootstepVolume = 0f;
        private float _targetFootstepVolume  = 0f;
        private float _walkingTargetVolume   = 0f;
        private float _walkHoldTime          = 0f; // accumulates while W held; drives volume ramp
        
        // Performance optimization
        private int lastRiceCheckFrame = 0;
        private Unity.Entities.EntityQuery _riceQuery;
        private bool _riceQueryCreated = false;
        
        // Singleton instance for easy access
        public static FPSAudioManager Instance { get; private set; }

        /// <summary>
        /// When false, FPS background music does NOT auto-start on scene load — used during the
        /// opening tutorial (quest 1) before Snerd introduces the boombox. Set true to resume.
        /// </summary>
        public static bool AutoPlayMusicOnStart = true;
        
        private void Awake()
        {
            // Singleton setup
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                // Debug.LogWarning("[FPSAudioManager] Multiple instances detected! Destroying duplicate.");
                Destroy(gameObject);
                return;
            }
        }
        
        private void Start()
        {
            // Create audio sources if not assigned
            if (musicSource == null)
            {
                GameObject musicObj = new GameObject("MusicSource");
                musicObj.transform.SetParent(transform);
                musicSource = musicObj.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
                
                // Configure for 2D background music
                musicSource.spatialBlend = 0f; // 2D audio
                musicSource.priority = 64; // Medium priority
            }
            
            if (footstepSource == null)
            {
                GameObject footstepObj = new GameObject("FootstepSource");
                footstepObj.transform.SetParent(transform);
                footstepSource = footstepObj.AddComponent<AudioSource>();
                footstepSource.loop = false;
                footstepSource.playOnAwake = false;
                
                // Configure for 2D audio (player's own footsteps)
                footstepSource.spatialBlend = 0f; // 0 = 2D, 1 = 3D
                footstepSource.rolloffMode = AudioRolloffMode.Linear;
                footstepSource.minDistance = 1f;
                footstepSource.maxDistance = 10f;
            }
            
            if (sfxSource == null)
            {
                GameObject sfxObj = new GameObject("SFXSource");
                sfxObj.transform.SetParent(transform);
                sfxSource = sfxObj.AddComponent<AudioSource>();
                sfxSource.loop = false;
                sfxSource.playOnAwake = false;
                
                // Configure for 2D pickup sounds (player actions)
                sfxSource.spatialBlend = 0f; // 2D audio
                sfxSource.priority = 128; // High priority for pickup sounds
            }
            
            // Apply volume settings
            UpdateAudioVolumes();
            
            // Start background music (unless suppressed — e.g. tutorial quest 1 before the boombox)
            if (AutoPlayMusicOnStart)
                PlayBackgroundMusic();
        }
        
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_riceQueryCreated)
                _riceQuery.Dispose();
        }
        
        private void Update()
        {
            if (HasVolumeChanged())
                UpdateAudioVolumes();

            // Accumulate hold time while walking; reset when stopped
            if (isCurrentlyWalking)
            {
                _walkHoldTime = Mathf.Min(_walkHoldTime + Time.deltaTime, walkFullVolumeTime);
                // SmoothStep gives a natural ease-in curve: quiet at first, full volume after walkFullVolumeTime
                float holdScale = Mathf.SmoothStep(0f, 1f, _walkHoldTime / Mathf.Max(walkFullVolumeTime, 0.01f));
                _targetFootstepVolume = _walkingTargetVolume * holdScale;
            }
            // else _targetFootstepVolume stays 0 (set by StopWalkingSound)

            // Smooth footstep volume fade toward current target
            if (footstepSource != null)
            {
                float fadeRate = _targetFootstepVolume >= _currentFootstepVolume
                    ? Time.deltaTime / Mathf.Max(footstepFadeInTime,  0.01f)
                    : Time.deltaTime / Mathf.Max(footstepFadeOutTime, 0.01f);

                _currentFootstepVolume = Mathf.MoveTowards(
                    _currentFootstepVolume, _targetFootstepVolume, fadeRate);

                if (footstepSource.isPlaying)
                    footstepSource.volume = _currentFootstepVolume;

                if (_currentFootstepVolume <= 0f && _targetFootstepVolume <= 0f && footstepSource.isPlaying)
                    footstepSource.Stop();
            }
        }
        
        /// <summary>
        /// Check if any volume settings have changed
        /// </summary>
        private bool HasVolumeChanged()
        {
            return lastMasterVolume != masterVolume ||
                   lastMusicVolume != musicVolume ||
                   lastFootstepVolume != footstepVolume ||
                   lastSFXVolume != sfxVolume;
        }
        
        /// <summary>
        /// Update all audio source volumes based on current settings
        /// </summary>
        private void UpdateAudioVolumes()
        {
            if (musicSource != null)
            {
                musicSource.volume = musicVolume * masterVolume;
            }
            
            if (footstepSource != null)
            {
                // Recalculate max volume — Update() applies hold-time scaling on top of this
                float volumeMultiplier = isWalkingOnRice ? riceWalkingVolumeBoost : groundWalkingVolumeBoost;
                _walkingTargetVolume = footstepVolume * sfxVolume * masterVolume * volumeMultiplier;
                // Do NOT set _targetFootstepVolume here — Update() drives it via hold-time scaling
            }
            
            if (sfxSource != null)
            {
                sfxSource.volume = sfxVolume * masterVolume;
            }
            
            // Update last known values
            lastMasterVolume = masterVolume;
            lastMusicVolume = musicVolume;
            lastFootstepVolume = footstepVolume;
            lastSFXVolume = sfxVolume;
        }
        
        /// <summary>
        /// Start playing background music
        /// </summary>
        public void PlayBackgroundMusic()
        {
            if (backgroundMusic != null && musicSource != null)
            {
                musicSource.clip = backgroundMusic;
                musicSource.Play();
            }
        }
        
        /// <summary>
        /// Stop background music
        /// </summary>
        public void StopBackgroundMusic()
        {
            if (musicSource != null)
            {
                musicSource.Stop();
            }
        }
        
        /// <summary>
        /// Called by FPSController when player is moving
        /// </summary>
        /// <param name="playerPosition">Current player position</param>
        /// <param name="isRunning">Is the player running?</param>
        /// <param name="isCrouching">Is the player crouching?</param>
        /// <param name="isGrounded">Is the player on the ground?</param>
        public void OnPlayerMoving(Vector3 playerPosition, bool isRunning, bool isCrouching, bool isGrounded)
        {
            // Handle jump/land audio
            HandleJumpLandAudio(isGrounded);
            
            // Reduce log frequency to avoid spam
            bool shouldLog = Time.frameCount % 120 == 0;
            
            
            if (!isGrounded) 
            {
                StopWalkingSound();
                return;
            }
            
            // Player is moving and grounded
            isCurrentlyWalking = true;
            
            // Check what surface we're walking on
            CheckForRiceBeneathPlayer(playerPosition);
            
            // Start or update walking sound
            StartWalkingSound();
        }
        
        /// <summary>
        /// Handle jump and land audio based on grounded state changes
        /// </summary>
        private void HandleJumpLandAudio(bool isGrounded)
        {
            // Detect landing (just became grounded)
            if (isGrounded && !wasGroundedLastFrame)
            {
                PlayLandSound();
            }
            
            wasGroundedLastFrame = isGrounded;
        }
        
        /// <summary>
        /// Called by FPSController when player jumps
        /// </summary>
        public void OnPlayerJump()
        {
            PlayJumpSound();
        }
        
        /// <summary>
        /// Play jump sound effect
        /// </summary>
        private void PlayJumpSound()
        {
            if (sfxSource == null || jumpSound == null) return;
            
            sfxSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f); // Slight pitch variation
            sfxSource.PlayOneShot(jumpSound);
        }
        
        /// <summary>
        /// Play landing sound effect
        /// </summary>
        private void PlayLandSound()
        {
            if (sfxSource == null || landSound == null) return;
            
            sfxSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f); // More pitch variation for lands
            sfxSource.PlayOneShot(landSound);
        }
        
        /// <summary>
        /// Called when player stops moving
        /// </summary>
        public void OnPlayerStopped()
        {
            // Only process if we were actually walking to avoid spam
            if (isCurrentlyWalking || wasWalkingLastFrame)
            {
                isCurrentlyWalking = false;
                StopWalkingSound();
            }
            
            wasWalkingLastFrame = isCurrentlyWalking;
        }
        
        /// <summary>
        /// Start playing the appropriate walking sound
        /// </summary>
        private void StartWalkingSound()
        {
            if (footstepSource == null) return;

            AudioClip soundToPlay = isWalkingOnRice ? walkingOnRiceSound : walkingSound;
            if (soundToPlay == null) return;

            // Update the max volume ceiling — Update() scales this by hold time each frame
            float volumeMultiplier = isWalkingOnRice ? riceWalkingVolumeBoost : groundWalkingVolumeBoost;
            _walkingTargetVolume = footstepVolume * sfxVolume * masterVolume * volumeMultiplier;

            if (footstepSource.clip != soundToPlay || !footstepSource.isPlaying)
            {
                footstepSource.clip   = soundToPlay;
                footstepSource.loop   = true;
                footstepSource.volume = _currentFootstepVolume;
                footstepSource.Play();
            }
        }

        private void StopWalkingSound()
        {
            _targetFootstepVolume = 0f;
            _walkHoldTime         = 0f; // reset so the next tap starts from quiet again
        }
        
        /// <summary>
        /// Check if there are rice objects beneath the player
        /// </summary>
        private void CheckForRiceBeneathPlayer(Vector3 playerPosition)
        {
            // Performance optimization - only check rice every N frames
            bool shouldCheckRice = (Time.frameCount - lastRiceCheckFrame) >= riceCheckInterval;
            if (!shouldCheckRice)
            {
                return; // Skip expensive rice detection this frame
            }
            
            var startTime = enablePerformanceProfiling ? System.DateTime.Now : System.DateTime.MinValue;
            
            Vector3 checkPosition;
            Collider[] riceObjects = new Collider[0]; // Initialize as empty
            int ecsRiceCount = 0;
            
            // Only check traditional Unity colliders if layer mask is properly configured
            if (riceLayerMask.value > 0 && riceLayerMask.value != -1)
            {
                if (onlyCheckDirectlyBelow)
                {
                    // Cast straight down to check for rice objects beneath feet
                    checkPosition = playerPosition;
                    riceObjects = Physics.OverlapCapsule(
                        checkPosition + Vector3.up * 0.1f, 
                        checkPosition - Vector3.up * 0.2f, 
                        riceDetectionRadius, 
                        riceLayerMask
                    );
                }
                else
                {
                    // Original sphere check
                    checkPosition = playerPosition + Vector3.up * 0.5f;
                    riceObjects = Physics.OverlapSphere(checkPosition, riceDetectionRadius, riceLayerMask);
                }
                
                // Filter out non-rice objects (double-check with tag if layer mask isn't set correctly)
                riceObjects = System.Array.FindAll(riceObjects, obj => 
                    obj != null && (obj.gameObject.layer == GetFirstLayerFromMask(riceLayerMask) || 
                                   (!string.IsNullOrEmpty(riceTag) && obj.CompareTag(riceTag))));
            }
            else
            {
                checkPosition = playerPosition;
                // If layer mask is wrong, skip Unity collider detection entirely
                riceObjects = new Collider[0];
                if (Time.frameCount % 300 == 0)
                {
                    // Debug.LogWarning("[FPSAudioManager] Skipping Unity collider detection - fix riceLayerMask in Inspector!");
                }
            }
            
            // Check ECS rice entities (expensive operation - can be disabled)
            if (!disableECSRiceDetection)
            {
                try
                {
                    var entityManager = World.DefaultGameObjectInjectionWorld?.EntityManager;
                    if (entityManager != null)
                    {
                        ecsRiceCount = CountECSRiceNearPlayer(entityManager.Value, playerPosition);
                    }
                }
                catch (System.Exception e)
                {
                    // Silently handle ECS not being available
                    if (Time.frameCount % 300 == 0) // Log occasionally
                    {
                        // Debug.LogWarning($"[FPSAudioManager] ECS rice detection failed: {e.Message}");
                    }
                }
            }
            
            bool wasWalkingOnRice = isWalkingOnRice;
            
            // Calculate total rice count for both scenarios
            int totalRiceCount = riceObjects.Length + ecsRiceCount;
            
            // Check for debugging override
            if (forceGroundAudio)
            {
                isWalkingOnRice = false;
                
                if (Time.frameCount % 300 == 0)
                {
                    // Debug.Log("[FPSAudioManager] forceGroundAudio enabled - using ground sound only");
                }
            }
            else
            {
                // Combine traditional colliders and ECS rice count
                isWalkingOnRice = totalRiceCount >= minRiceCountForWalking;
                
                // Extra validation: if we detect rice but layer mask is wrong, ignore it
                if (isWalkingOnRice && riceLayerMask.value == -1)
                {
                    // Debug.LogWarning("[FPSAudioManager] Ignoring rice detection due to invalid layer mask (-1)");
                    isWalkingOnRice = false;
                }
            }
            
            // Performance logging
            if (enablePerformanceProfiling)
            {
                var endTime = System.DateTime.Now;
                var duration = (endTime - startTime).TotalMilliseconds;
            }
            
            // Debug rice detection with detailed object info
            if (Time.frameCount % 120 == 0)
            {
                if (riceObjects.Length > 0)
                {
                    string riceInfo = string.Join(", ", System.Array.ConvertAll(riceObjects, obj => 
                        $"{obj.name}(L{obj.gameObject.layer})"));
                }
                
                // Warning if layer mask looks wrong
                if (riceLayerMask.value == -1)
                {
                    // Debug.LogError("[FPSAudioManager] ⚠️ riceLayerMask is -1 (ALL LAYERS)! Set it to a specific rice layer in Inspector!");
                }
            }
            
            // If surface type changed, restart the walking sound
            if (wasWalkingOnRice != isWalkingOnRice && isCurrentlyWalking)
            {
                StartWalkingSound();
            }
            
            lastRiceCheckFrame = Time.frameCount;
        }
        
        /// <summary>
        /// Count ECS rice entities near the player position
        /// </summary>
        private int CountECSRiceNearPlayer(EntityManager entityManager, Vector3 playerPosition)
        {
            // Build query once and reuse — CreateEntityQuery every call was a sync point
            if (!_riceQueryCreated)
            {
                _riceQuery = entityManager.CreateEntityQuery(
                    Unity.Entities.ComponentType.ReadOnly<LocalTransform>(),
                    Unity.Entities.ComponentType.ReadOnly<Vampire.Rice.RiceEntity>()
                );
                _riceQueryCreated = true;
            }

            if (_riceQuery.IsEmpty) return 0;

            int count = 0;
            float radiusSquared = riceDetectionRadius * riceDetectionRadius;
            float3 playerPos = new float3(playerPosition.x, playerPosition.y, playerPosition.z);

            var transforms = _riceQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            int maxEntitiesToCheck = math.min(transforms.Length, 2000);
            for (int i = 0; i < maxEntitiesToCheck; i++)
            {
                float3 ricePos = transforms[i].Position;
                float distanceSquared = math.distancesq(playerPos, ricePos);

                if (distanceSquared <= radiusSquared)
                {
                    if (onlyCheckDirectlyBelow)
                    {
                        if (math.abs(ricePos.y - playerPos.y) <= 0.5f)
                            count++;
                    }
                    else
                    {
                        count++;
                    }
                }
            }

            transforms.Dispose();
            return count;
        }
        
        /// <summary>
        /// Helper method to get the first layer number from a LayerMask
        /// </summary>
        private int GetFirstLayerFromMask(LayerMask layerMask)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((layerMask.value & (1 << i)) != 0)
                {
                    return i;
                }
            }
            return 0; // Default layer if none found
        }
        
        /// <summary>
        /// Play rice pickup sound
        /// </summary>
        /// <param name="isMultiPickup">True if picking up multiple rice at once</param>
        public void PlayRicePickupSound(bool isMultiPickup = false)
        {
            if (sfxSource == null) return;
            
            AudioClip soundToPlay = isMultiPickup ? multiPickupSound : ricePickupSound;
            
            if (soundToPlay != null)
            {
                sfxSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f); // Slight pitch variation
                sfxSource.PlayOneShot(soundToPlay);
            }
        }
        
        /// <summary>
        /// Set master volume (0.0 to 1.0) - affects all audio
        /// </summary>
        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            UpdateAudioVolumes();
        }
        
        /// <summary>
        /// Set music volume (0.0 to 1.0)
        /// </summary>
        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            UpdateAudioVolumes();
        }
        
        /// <summary>
        /// Set footstep volume (0.0 to 1.0)
        /// </summary>
        public void SetFootstepVolume(float volume)
        {
            footstepVolume = Mathf.Clamp01(volume);
            UpdateAudioVolumes();
        }
        
        /// <summary>
        /// Set SFX volume (0.0 to 1.0)
        /// </summary>
        public void SetSFXVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            UpdateAudioVolumes();
        }
        
        /// <summary>
        /// Mute or unmute all audio
        /// </summary>
        public void SetMuted(bool muted)
        {
            if (musicSource != null) musicSource.mute = muted;
            if (footstepSource != null) footstepSource.mute = muted;
            if (sfxSource != null) sfxSource.mute = muted;
        }
        
        /// <summary>
        /// Get current effective volume for music (includes master volume)
        /// </summary>
        public float GetEffectiveMusicVolume()
        {
            return musicVolume * masterVolume;
        }
        
        /// <summary>
        /// Get current effective volume for footsteps (includes master volume)
        /// </summary>
        public float GetEffectiveFootstepVolume()
        {
            return footstepVolume * masterVolume;
        }
        
        /// <summary>
        /// Get current effective volume for SFX (includes master volume)
        /// </summary>
        public float GetEffectiveSFXVolume()
        {
            return sfxVolume * masterVolume;
        }
        
        private void OnDrawGizmosSelected()
        {
            // Draw rice detection radius
            if (isWalkingOnRice)
                Gizmos.color = Color.green;
            else
                Gizmos.color = Color.yellow;
                
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, riceDetectionRadius);
        }
        
        /// <summary>
        /// Debug method to validate audio setup - call this in Inspector or code
        /// </summary>
        public void ValidateAudioSetup()
        {
            // Debug.Log("===== FPS Audio Manager Validation =====");
            // Debug.Log($"Walking Sound: {(walkingSound != null ? $"✓ {walkingSound.name}" : "✗ Missing")}");
            // Debug.Log($"Rice Walking Sound: {(walkingOnRiceSound != null ? $"✓ {walkingOnRiceSound.name}" : "✗ Missing")}");
            // Debug.Log($"Jump Sound: {(jumpSound != null ? $"✓ {jumpSound.name}" : "✗ Missing")}");
            // Debug.Log($"Land Sound: {(landSound != null ? $"✓ {landSound.name}" : "✗ Missing")}");
            // Debug.Log($"Background Music: {(backgroundMusic != null ? $"✓ {backgroundMusic.name}" : "✗ Missing")}");
            // Debug.Log($"Rice Pickup Sound: {(ricePickupSound != null ? $"✓ {ricePickupSound.name}" : "✗ Missing")}");
            
            // Debug.Log($"Music Source: {(musicSource != null ? $"✓ Volume: {musicSource.volume:F2}" : "✗ Missing")}");
            // Debug.Log($"Footstep Source: {(footstepSource != null ? $"✓ Volume: {footstepSource.volume:F2}" : "✗ Missing")}");
            // Debug.Log($"SFX Source: {(sfxSource != null ? $"✓ Volume: {sfxSource.volume:F2}" : "✗ Missing")}");
            
            // Debug.Log($"Master Volume: {masterVolume:F2}");
            // Debug.Log($"Footstep Volume: {footstepVolume:F2}");
            // Debug.Log($"Ground Boost: {groundWalkingVolumeBoost:F2}");
            // Debug.Log($"Rice Boost: {riceWalkingVolumeBoost:F2}");
            // Debug.Log($"Rice Detection - Radius: {riceDetectionRadius:F2}, Min Count: {minRiceCountForWalking}, Direct Below: {onlyCheckDirectlyBelow}");
            // Debug.Log($"Layer Mask: {riceLayerMask.value} (Layer {GetFirstLayerFromMask(riceLayerMask)}), Rice Tag: '{riceTag}'");
            
            // Critical layer mask validation
            if (riceLayerMask.value == -1)
            {
                // Debug.LogError("❌ CRITICAL: riceLayerMask is -1 (ALL LAYERS)! This will detect everything as rice!");
                // Debug.LogError("💡 FIX: Set riceLayerMask to a specific layer in Inspector (e.g., Layer 8 for Rice objects)");
            }
            else if (riceLayerMask.value == 0)
            {
                // Debug.LogWarning("⚠️ riceLayerMask is 0 (nothing). Make sure rice objects are on a specific layer.");
            }
            else
            {
                // Debug.Log($"✅ Layer mask correctly set to layer {GetFirstLayerFromMask(riceLayerMask)}");
            }
            
            // Debug.Log($"Force Ground Audio: {(forceGroundAudio ? "✅ ENABLED (rice audio disabled)" : "❌ Disabled")}");
            // Debug.Log($"Performance - Rice Check Interval: {riceCheckInterval} frames, ECS Detection: {!disableECSRiceDetection}");
            
            // Quick troubleshooting guide
            if (riceLayerMask.value == -1)
            {
                // Debug.LogError("🚨 QUICK FIX: Set 'Force Ground Audio' to true to disable rice detection entirely!");
            }
            // Debug.Log("=====================================");
        }
        
        /// <summary>
        /// Debug method to test performance impact of different systems
        /// </summary>
        public void DebugPerformance()
        {
            // Debug.Log("===== Performance Debug =====");
            // Debug.Log($"Current FPS: {1.0f / Time.deltaTime:F1}");
            // Debug.Log($"Frame time: {Time.deltaTime * 1000:F1}ms");
            
            // Test ECS rice counting performance
            if (!disableECSRiceDetection)
            {
                var startTime = System.DateTime.Now;
                var entityManager = World.DefaultGameObjectInjectionWorld?.EntityManager;
                if (entityManager != null)
                {
                    int riceCount = CountECSRiceNearPlayer(entityManager.Value, transform.position);
                    var duration = (System.DateTime.Now - startTime).TotalMilliseconds;
                    // Debug.Log($"ECS Rice Count: {riceCount} (took {duration:F2}ms)");
                }
            }
            
            // Debug.LogWarning("If frame time is >16ms (60fps) or >33ms (30fps), performance is poor!");
            // Debug.Log("Try: Increase riceCheckInterval or disable ECS rice detection");
            
            // Additional audio detection warnings
            if (riceLayerMask.value == -1)
            {
                // Debug.LogError("❌ AUDIO BUG: riceLayerMask=-1 will cause false rice detection!");
            }
            
            // Debug.Log("============================");
        }
    }
}