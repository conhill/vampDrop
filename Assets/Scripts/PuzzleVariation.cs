using UnityEngine;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Defines a puzzle layout variation (wall positions, multipliers, obstacles)
    /// Like different Cup Heroes levels
    /// </summary>
    [CreateAssetMenu(fileName = "PuzzleVariation", menuName = "Vampire/Puzzle Variation")]
    public class PuzzleVariation : ScriptableObject
    {
        [Header("Variation Info")]
        public string VariationName = "Variation 1";
        
        [Tooltip("Straight down, Funnel, Zigzag, etc.")]
        public LayoutType Layout = LayoutType.StraightDown;
        
        [Header("Wall Configuration")]
        public WallConfig[] Walls;
        
        [Header("Multiplier Gates")]
        public MultiplierConfig[] Multipliers;
        
        [Header("Goal Settings")]
        public Vector3 GoalPosition = new Vector3(0, -5, 0);
        public Vector3 GoalSize = new Vector3(2, 1, 0.5f);
    }
    
    public enum LayoutType
    {
        StraightDown,  // |  |
        Funnel,        // \  /
        DoubleFunnel,  // \/ \/
        Zigzag,        // Like stairs
        Custom
    }
    
    [System.Serializable]
    public class WallConfig
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale = Vector3.one;
        public WallType Type = WallType.Straight;
    }
    
    [System.Serializable]
    public class MultiplierConfig
    {
        public Vector3 Position;
        public int Multiplier = 2;
        public Vector3 Size = new Vector3(1, 0.5f, 0.5f);
    }
    
    public enum WallType
    {
        Straight,      // Vertical wall
        Diagonal,      // Angled wall
        Bumper,        // Physics bounce
        Platform       // Horizontal surface
    }
}
