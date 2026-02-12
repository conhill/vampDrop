using UnityEngine;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// LEVEL GENERATOR - Uses PlayerDataManager to create levels based on upgrades
    /// 
    /// BALL TYPES (Special Perks):
    /// - TypeID 0: Standard (1x points, no special effects)
    /// - TypeID 1: BonusPoints (2x-5x points multiplier)
    /// - TypeID 2: MultiplierBooster (increases gate multipliers by +1 or +2)
    /// - TypeID 3: Harmful (-points, reduces multipliers)
    /// - TypeID 4: Lucky (chance for extra rewards)
    /// 
    /// GATE TYPES (Dynamic Spawning):
    /// - 0: Standard gates (no multiplier)
    /// - 2, 3, 4, 5: Multiplier gates (probability based on upgrades)
    /// </summary>
    public class ProgressionSystem : MonoBehaviour
    {
        public static ProgressionSystem Instance { get; private set; }
        
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }
        
        /// <summary>
        /// Generate gate multipliers based on player's upgrades
        /// Returns array like [0, 2, 0, 3, 0] (standard, x2, standard, x3, standard)
        /// </summary>
        public int[] GenerateGateMultipliers(int gateCount)
        {
            if (playerData == null)
            {
                Debug.LogError("[Progression] PlayerDataManager not found! Using default gates.");
                return new int[gateCount]; // All 0s (standard gates)
            }
            
            int[] multipliers = new int[gateCount];
            
            // First, place guaranteed high multipliers
            if (playerData.DropPuzzle.guaranteedHighMultiplierGates > 0)
            {
                int stride = gateCount / (playerData.DropPuzzle.guaranteedHighMultiplierGates + 1);
                for (int i = 0; i < playerData.DropPuzzle.guaranteedHighMultiplierGates && i < gateCount; i++)
                {
                    int pos = stride * (i + 1);
                    if (pos < gateCount)
                    {
                        multipliers[pos] = 4; // x4 gate
                    }
                }
            }
            
            // Fill remaining slots based on spawn chances
            for (int i = 0; i < gateCount; i++)
            {
                if (multipliers[i] > 0) continue; // Skip guaranteed placements
                
                float roll = Random.Range(0f, 1f);
                float cumulative = 0f;
                
                // Check x4 chance
                cumulative += playerData.DropPuzzle.x4GateChance;
                if (roll < cumulative)
                {
                    multipliers[i] = 4;
                    continue;
                }
                
                // Check x3 chance
                cumulative += playerData.DropPuzzle.x3GateChance;
                if (roll < cumulative)
                {
                    multipliers[i] = 3;
                    continue;
                }
                
                // Check x2 chance
                cumulative += playerData.DropPuzzle.x2GateChance;
                if (roll < cumulative)
                {
                    multipliers[i] = 2;
                    continue;
                }
                
                // Default to standard gate
                multipliers[i] = 0;
            }
            
            Debug.Log($"[Progression] Generated {gateCount} gates: [{string.Join(", ", multipliers)}]");
            return multipliers;
        }
        
        /// <summary>
        /// Roll for special ball type based on player upgrades
        /// Called by DropperControllerECS when spawning balls
        /// </summary>
        public RiceBallType RollBallType()
        {
            if (playerData == null)
            {
                return GetStandardBall();
            }
            
            float roll = Random.Range(0f, 1f);
            float cumulative = 0f;
            
            // Check bonus points first
            cumulative += playerData.DropPuzzle.bonusPointBallChance;
            if (roll < cumulative)
            {
                return new RiceBallType
                {
                    TypeID = 1,
                    PointsMultiplier = Random.Range(2f, 5f), // 2x-5x points
                    MultiplierBoost = 0f,
                    IsHarmful = false
                };
            }
            
            // Check multiplier boost
            cumulative += playerData.DropPuzzle.multiplierBoostBallChance;
            if (roll < cumulative)
            {
                return new RiceBallType
                {
                    TypeID = 2,
                    PointsMultiplier = 1f,
                    MultiplierBoost = 1f, // +1 to any gate it hits
                    IsHarmful = false
                };
            }
            
            // Check lucky ball
            cumulative += playerData.DropPuzzle.luckyBallChance;
            if (roll < cumulative)
            {
                return new RiceBallType
                {
                    TypeID = 4,
                    PointsMultiplier = 1.5f,
                    MultiplierBoost = 0f,
                    IsHarmful = false
                };
            }
            
            // Standard ball
            return GetStandardBall();
        }
        
        private RiceBallType GetStandardBall()
        {
            return new RiceBallType
            {
                TypeID = 0,
                PointsMultiplier = 1f,
                MultiplierBoost = 0f,
                IsHarmful = false
            };
        }
        
        /// <summary>
        /// Get number of balls to spawn for this level
        /// </summary>
        public int GetStartingBallCount()
        {
            return playerData != null ? playerData.DropPuzzle.startingBalls : 20;
        }
        
        /// <summary>
        /// Get drop speed multiplier
        /// </summary>
        public float GetDropSpeedMultiplier()
        {
            return playerData != null ? playerData.DropPuzzle.dropSpeedMultiplier : 1.0f;
        }
    }
}
