using UnityEngine;
using System.Collections;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// RICE CRAFTING SYSTEM
    /// Handles conversion of rice grains → riceballs with quality rolling
    /// Press a button to craft, plays animations, updates inventory
    /// </summary>
    public class RiceCraftingSystem : MonoBehaviour
    {
        public static RiceCraftingSystem Instance { get; private set; }
        
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        
        [Header("Crafting Settings")]
        public KeyCode craftKey = KeyCode.E; // Press E to craft
        public float baseTimePerBall = 0.3f; // Seconds per riceball
        public bool autoShowResults = true;
        
        [Header("Audio")]
        [Tooltip("Looping sound while crafting is in progress (crafting.mp3)")]
        public AudioClip craftingSound;
        [Tooltip("One-shot chime when all balls are done (complete.wav)")]
        public AudioClip completionSound;

        private AudioSource _audioSource;

        [Header("UI")]
        [Tooltip("Uncheck to hide the legacy OnGUI crafting panel (use when UIController HUD is active)")]
        public bool showLegacyUI = false;
        
        private bool isCrafting = false;
        private int lastCraftedCount = 0;
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake  = false;
            _audioSource.spatialBlend = 0f; // 2D — player's own action
            _audioSource.loop         = false;
        }
        
        private void Update()
        {
            // Player crafts riceballs themselves — but only once Snerd has given them the
            // crafting device (HasCraftingDevice). Reverts the "craft at Snerd" behaviour.
            if (Input.GetKeyDown(craftKey)
                && playerData != null
                && playerData.HasCraftingDevice
                && !isCrafting)
            {
                CraftRiceBalls();
            }
        }
        
        /// <summary>
        /// Craft riceballs with animated progression
        /// </summary>
        private IEnumerator CraftRiceBallsCoroutine()
        {
            isCrafting = true;
            
            int ballsToCraft = playerData.RiceGrains / 5;
            float timePerBall = baseTimePerBall / playerData.Crafting.craftingSpeedMultiplier;
            
            Debug.Log($"[Crafting] Starting craft: {ballsToCraft} riceballs | {timePerBall:F2}s each");
            
            // Store initial counts to track what was crafted
            int initialFine = playerData.Inventory.FineBalls;
            int initialGood = playerData.Inventory.GoodBalls;
            int initialGreat = playerData.Inventory.GreatBalls;
            int initialExcellent = playerData.Inventory.ExcellentBalls;
            
            // Do the actual crafting
            lastCraftedCount = playerData.ConvertRiceToRiceBalls();
            
            // Start looping crafting sound for the whole crafting duration
            if (craftingSound != null && _audioSource != null)
            {
                _audioSource.clip   = craftingSound;
                _audioSource.loop   = true;
                _audioSource.volume = SFXVolume();
                _audioSource.Play();
            }

            // Animate the crafting process
            for (int i = 0; i < lastCraftedCount; i++)
            {
                Debug.Log($"[Crafting] Crafting ball {i + 1}/{lastCraftedCount}...");
                yield return new WaitForSeconds(timePerBall);
            }
            
            // Calculate what was crafted
            int craftedFine = playerData.Inventory.FineBalls - initialFine;
            int craftedGood = playerData.Inventory.GoodBalls - initialGood;
            int craftedGreat = playerData.Inventory.GreatBalls - initialGreat;
            int craftedExcellent = playerData.Inventory.ExcellentBalls - initialExcellent;
            
            Debug.Log($"[Crafting] ✅ Complete! Crafted: {craftedFine} Fine, {craftedGood} Good, {craftedGreat} Great, {craftedExcellent} Excellent");
            Debug.Log($"[Crafting] Total inventory: {playerData.Inventory.GetTotalBalls()} balls (Value: {playerData.Inventory.GetTotalValue()}x)");

            // Stop crafting loop and play completion chime
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.loop = false;
                if (completionSound != null)
                {
                    _audioSource.volume = SFXVolume();
                    _audioSource.PlayOneShot(completionSound);
                }
            }

            // Notify tutorial manager
            if (DropPuzzle.TutorialManager.Instance != null)
            {
                DropPuzzle.TutorialManager.Instance.NotifyRiceBallsCrafted(lastCraftedCount);
            }
            
            isCrafting = false;
        }
        
        /// <summary>
        /// Manual craft trigger (call from UI button)
        /// </summary>
        public void CraftRiceBalls()
        {
            if (isCrafting)
            {
                Debug.LogWarning("[Crafting] Already crafting!");
                return;
            }
            
            if (playerData.RiceGrains >= 5)
            {
                StartCoroutine(CraftRiceBallsCoroutine());
            }
            else
            {
                Debug.LogWarning($"[Crafting] Not enough rice! Have {playerData.RiceGrains}, need 5");
            }
        }
        
        /// <summary>
        /// Get crafting progress (for UI display)
        /// </summary>
        public bool IsCrafting() => isCrafting;
        
        /// <summary>
        /// Get number of riceballs we can craft
        /// </summary>
        public int GetCraftableCount()
        {
            return playerData != null ? playerData.RiceGrains / 5 : 0;
        }

        private float SFXVolume()
        {
            var a = Player.FPSAudioManager.Instance;
            return a != null ? a.sfxVolume * a.masterVolume : 1f;
        }
        
        private void OnGUI()
        {
            if (!showLegacyUI) return;
            if (playerData == null) return;
            
            // Show crafting UI in top-right
            float x = Screen.width - 310;
            float y = 10;
            float lineHeight = 25;
            
            GUI.Box(new Rect(x - 5, y - 5, 300, 130), "");
            
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 16;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = Color.yellow;
            
            GUIStyle textStyle = new GUIStyle(GUI.skin.label);
            textStyle.fontSize = 14;
            textStyle.normal.textColor = Color.white;
            
            GUI.Label(new Rect(x, y, 300, lineHeight), "RICE CRAFTING", headerStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 300, lineHeight), $"Rice: {playerData.RiceGrains} (Need 5 per ball)", textStyle);
            y += lineHeight;
            
            int craftable = GetCraftableCount();
            GUI.Label(new Rect(x, y, 300, lineHeight), $"Can craft: {craftable} riceballs", textStyle);
            y += lineHeight;
            
            if (isCrafting)
            {
                GUIStyle craftingStyle = new GUIStyle(textStyle);
                craftingStyle.normal.textColor = Color.green;
                GUI.Label(new Rect(x, y, 300, lineHeight), "⚙️ CRAFTING...", craftingStyle);
            }
            else if (craftable > 0)
            {
                if (GUI.Button(new Rect(x, y, 290, 30), $"[E] Craft {craftable} RiceBalls"))
                {
                    CraftRiceBalls();
                }
            }
        }
    }
}
