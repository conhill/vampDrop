using UnityEngine;
using Vampire.DropPuzzle;

/// <summary>
/// Helper to create pre-configured puzzle variations in code
/// Use this to quickly set up common layouts
/// </summary>
public static class PuzzleVariationTemplates
{
    /// <summary>
    /// Simple straight down layout |  |
    /// Just vertical walls on left and right
    /// </summary>
    public static void ConfigureStraightDown(PuzzleVariation variation)
    {
        variation.VariationName = "Straight Down";
        variation.Layout = LayoutType.StraightDown;
        
        // Two vertical walls
        variation.Walls = new WallConfig[]
        {
            // Left wall
            new WallConfig
            {
                Position = new Vector3(-2, 0, 0),
                Rotation = Vector3.zero,
                Scale = new Vector3(0.2f, 10, 1),
                Type = WallType.Straight
            },
            // Right wall
            new WallConfig
            {
                Position = new Vector3(2, 0, 0),
                Rotation = Vector3.zero,
                Scale = new Vector3(0.2f, 10, 1),
                Type = WallType.Straight
            }
        };
        
        // Simple multipliers in the middle
        variation.Multipliers = new MultiplierConfig[]
        {
            new MultiplierConfig
            {
                Position = new Vector3(-0.5f, 2, 0),
                Multiplier = 2,
                Size = new Vector3(0.8f, 0.3f, 0.5f)
            },
            new MultiplierConfig
            {
                Position = new Vector3(0.5f, 0, 0),
                Multiplier = 3,
                Size = new Vector3(0.8f, 0.3f, 0.5f)
            }
        };
        
        variation.GoalPosition = new Vector3(0, -4, 0);
        variation.GoalSize = new Vector3(3, 1, 0.5f);
    }
    
    /// <summary>
    /// Funnel layout - starts wide, narrows down
    ///  \    /
    ///   \  /
    ///    \/
    /// </summary>
    public static void ConfigureFunnel(PuzzleVariation variation)
    {
        variation.VariationName = "Funnel";
        variation.Layout = LayoutType.Funnel;
        
        // Angled walls that funnel inward
        variation.Walls = new WallConfig[]
        {
            // Left angled wall (top)
            new WallConfig
            {
                Position = new Vector3(-3, 3, 0),
                Rotation = new Vector3(0, 0, -30), // Angle inward
                Scale = new Vector3(0.2f, 4, 1),
                Type = WallType.Diagonal
            },
            // Right angled wall (top)
            new WallConfig
            {
                Position = new Vector3(3, 3, 0),
                Rotation = new Vector3(0, 0, 30), // Angle inward
                Scale = new Vector3(0.2f, 4, 1),
                Type = WallType.Diagonal
            },
            // Left bottom wall (straight)
            new WallConfig
            {
                Position = new Vector3(-1, -1, 0),
                Rotation = Vector3.zero,
                Scale = new Vector3(0.2f, 3, 1),
                Type = WallType.Straight
            },
            // Right bottom wall (straight)
            new WallConfig
            {
                Position = new Vector3(1, -1, 0),
                Rotation = Vector3.zero,
                Scale = new Vector3(0.2f, 3, 1),
                Type = WallType.Straight
            }
        };
        
        // Multipliers at different heights
        variation.Multipliers = new MultiplierConfig[]
        {
            new MultiplierConfig
            {
                Position = new Vector3(-1.5f, 4, 0),
                Multiplier = 2,
                Size = new Vector3(1, 0.3f, 0.5f)
            },
            new MultiplierConfig
            {
                Position = new Vector3(1.5f, 3, 0),
                Multiplier = 4,
                Size = new Vector3(1, 0.3f, 0.5f)
            },
            new MultiplierConfig
            {
                Position = new Vector3(0, 1, 0),
                Multiplier = 5,
                Size = new Vector3(1.2f, 0.3f, 0.5f)
            }
        };
        
        variation.GoalPosition = new Vector3(0, -3, 0);
        variation.GoalSize = new Vector3(2, 1, 0.5f);
    }
    
    /// <summary>
    /// Complex layout with multiple paths and multipliers
    /// Like the Cup Heroes image
    /// </summary>
    public static void ConfigureCupHeroesStyle(PuzzleVariation variation)
    {
        variation.VariationName = "Cup Heroes Style";
        variation.Layout = LayoutType.Custom;
        
        // Multiple platforms and dividers
        variation.Walls = new WallConfig[]
        {
            // Outer walls
            new WallConfig { Position = new Vector3(-4, 0, 0), Rotation = Vector3.zero, Scale = new Vector3(0.2f, 12, 1), Type = WallType.Straight },
            new WallConfig { Position = new Vector3(4, 0, 0), Rotation = Vector3.zero, Scale = new Vector3(0.2f, 12, 1), Type = WallType.Straight },
            
            // Divider platforms
            new WallConfig { Position = new Vector3(-1, 3, 0), Rotation = Vector3.zero, Scale = new Vector3(2, 0.2f, 1), Type = WallType.Platform },
            new WallConfig { Position = new Vector3(1, 1, 0), Rotation = Vector3.zero, Scale = new Vector3(2, 0.2f, 1), Type = WallType.Platform },
            new WallConfig { Position = new Vector3(-1.5f, -1, 0), Rotation = Vector3.zero, Scale = new Vector3(2, 0.2f, 1), Type = WallType.Platform },
        };
        
        // Multiple multiplier zones like Cup Heroes
        variation.Multipliers = new MultiplierConfig[]
        {
            new MultiplierConfig { Position = new Vector3(-2.5f, 2, 0), Multiplier = 2, Size = new Vector3(1, 0.3f, 0.5f) },
            new MultiplierConfig { Position = new Vector3(2.5f, 3, 0), Multiplier = 4, Size = new Vector3(1, 0.3f, 0.5f) },
            new MultiplierConfig { Position = new Vector3(-2, 0, 0), Multiplier = 5, Size = new Vector3(1.2f, 0.3f, 0.5f) },
            new MultiplierConfig { Position = new Vector3(1.5f, -1, 0), Multiplier = 6, Size = new Vector3(1.5f, 0.3f, 0.5f) },
        };
        
        variation.GoalPosition = new Vector3(0, -4, 0);
        variation.GoalSize = new Vector3(6, 1, 0.5f);
    }
}
