# Game Structure & Flow

## Overview
VAMP4 is a roguelite FPS/drop-puzzle hybrid where players collect rice, craft riceballs, and drop them through a pachinko-style puzzle for rewards.

## Core Game Loop
```
┌─────────────────────────────────────┐
│  FPS_Collect Scene (Collection)     │
│  - Collect rice grains               │
│  - Craft riceballs (5 rice → 1 ball)│
│  - Quality rolls: Fine/Good/Great/Ex │
│  - Enter shop to buy upgrades        │
└───────────────┬─────────────────────┘
                ↓
       [Press E at Door]
                ↓
┌─────────────────────────────────────┐
│  DropPuzzle Scene (Ball Drop)       │
│  - Drop crafted riceballs            │
│  - Navigate physics maze             │
│  - Hit multiplier gates x2/x3/x5     │
│  - Score = Ball Quality × Multiplier │
└───────────────┬─────────────────────┘
                ↓
     [Complete/Go Back to FPS]
                ↓
          [Repeat Loop]
```

## Scene Architecture

### FPS_Collect Scene
**Purpose:** Collection and preparation phase

**Key GameObjects:**
- `Player` - FirstPersonController with RiceCollectorFPS
- `RiceSpawner` - Manages rice grain spawning (ECS)
- `GameSystems` (DontDestroyOnLoad) - Persistent managers
- `ShopZone` - BuyZoneInteraction, opens shop UI
- `ExitTrigger` - Transitions to DropPuzzle scene

**Components:**
- `RiceSpawnerECS` - Spawns rice grains using ECS
- `RiceCollectorFPS` - Handles rice pickup and particle effects
- `BuyZoneInteraction` - Shop trigger zone
- `DayNightCycleManager` - Time pressure system

### DropPuzzle Scene
**Purpose:** Drop puzzle gameplay phase

**Key GameObjects:**
- `BallRenderer` - RiceBallRendererECS (GPU instanced rendering)
- `Dropper` - DropperControllerECS (spawns balls)
- `GateInteraction` - RiceBallGateInteractionSystem
- `WallCollision` - RiceBallWallCollisionSystem
- `GridPuzzleLoader` - Loads level layouts
- `BallDropUI` - Completion screen, return to FPS

**Components:**
- `DropperControllerECS` - Spawns ECS balls into puzzle
- `RiceBallRendererECS` - Renders all balls in GPU-instanced batches
- `RiceBallGateInteractionSystem` - Detects gate hits
- `RiceBallWallCollisionSystem` - Wall physics
- `BallDropUI` - UI and scene transitions

### ComicScene (Optional)
**Purpose:** Story/narrative moments

**Key GameObjects:**
- `ComicSceneManager` - Controls comic sequence
- `ComicCanvas` - Auto-generated UI canvas
- `ScrollingContainer` - Horizontal panel container

**When Used:**
- Game intro (before first quest)
- After going outside first time
- After coming inside first time
- Story milestones
- Endings

## Persistent Systems (DontDestroyOnLoad)

### GameSystems GameObject
Lives across all scenes, never destroyed.

**Components:**
1. **PlayerDataManager**
   - Inventory (rice count, riceball storage)
   - Currency (game money)
   - Stats (total balls dropped, score)
   - Save/load data

2. **UpgradeShop**
   - Available upgrades (crafting quality, max riceballs, gate unlocks)
   - Purchase logic
   - Price calculations

3. **ProgressionSystem**
   - Generates gates based on upgrades
   - Calculates ball quality chances
   - Unlocks content

4. **DayNightCycleManager**
   - Timer countdown (FPS scene only)
   - Pause/resume for UI
   - Quest deadline pressure

5. **DebugUpgradeUI** (optional)
   - [G] key to open debug shop
   - Grant currency/upgrades for testing

## Scene Transitions

### FPS → DropPuzzle
```csharp
// In ExitTrigger or door interaction:
SceneManager.LoadScene("DropPuzzle");

// GameSystems persists automatically (DontDestroyOnLoad)
```

### DropPuzzle → FPS
```csharp
// In BallDropUI.GoBackToFPS():
SceneManager.LoadScene("FPS_Collect");
```

### Loading Comics
```csharp
// Set config, load scene:
ComicSceneManager.CurrentSequence = myComicConfig;
SceneManager.LoadScene("ComicScene");

// Comic automatically returns to specified scene
```

## Build Settings Scene Order
Correct order for build:
1. ComicScene (optional - if using intro comic)
2. FPS_Collect
3. DropPuzzle

## Data Persistence

### Between Scenes
- `PlayerDataManager.Instance` - persists via DontDestroyOnLoad
- All inventory, currency, stats maintained
- Upgrades remain active

### Between Sessions
Currently runtime-only. To add save/load:
```csharp
// In PlayerDataManager:
public void SaveToFile()
{
    string json = JsonUtility.ToJson(this);
    File.WriteAllText(savePath, json);
}

public void LoadFromFile()
{
    string json = File.ReadAllText(savePath);
    JsonUtility.FromJsonOverwrite(json, this);
}
```

## Key Script Locations

**Core Systems:**
- `Assets/Scripts/PlayerDataManager.cs`
- `Assets/Scripts/UpgradeShop.cs`
- `Assets/Scripts/ProgressionSystem.cs`
- `Assets/Scripts/DayNightCycleManager.cs`

**FPS Collection:**
- `Assets/Scripts/RiceCollectorFPS.cs`
- `Assets/Scripts/BuyZoneInteraction.cs`

**Ball Drop:**
- `Assets/Scripts/DropperControllerECS.cs`
- `Assets/Scripts/RiceBallRendererECS.cs`
- `Assets/Scripts/BallDropUI.cs`

**Comic System:**
- `Assets/Scripts/ComicSceneManager.cs`
- `Assets/Scripts/ComicSequenceConfig.cs`
- `Assets/Scripts/ComicSceneLoader.cs`

## Tags & Layers

**Required Tags:**
- `Wall` - For drop puzzle walls
- `RiceGrain` - For rice pickup colliders
- `Player` - For player GameObject
- `Gate` - For multiplier/goal gates

**Required Layers:**
- Default (0) - General objects
- UI (5) - UI elements
- (Custom layers as needed)

## Common Setup Issues

**Problem:** GameSystems doesn't persist
**Solution:** Check DontDestroyOnLoad is called in Awake(), before scene loads

**Problem:** Rice/riceballs reset when changing scenes
**Solution:** Check PlayerDataManager singleton instance is working

**Problem:** Upgrades don't apply
**Solution:** Verify ProgressionSystem.UpdateProgression() is called after purchase

**Problem:** Comic doesn't load next scene
**Solution:** Ensure ComicSequenceConfig has Next Scene Name set correctly

## Testing Workflow

### Quick Test (30 seconds):
1. Open FPS_Collect scene
2. Press [G] for debug shop
3. Grant 1000 currency [E]
4. Press [ESC], go to shop zone
5. Buy upgrades
6. Collect rice, craft balls
7. Go outside, drop balls

### Full Test (5 minutes):
1. Start from ComicScene or FPS_Collect
2. Collect rice manually
3. Craft riceballs naturally
4. Observe quality distribution
5. Drop balls, check scoring
6. Buy upgrades, verify effects
7. Return to FPS, repeat

### Performance Test:
1. Open DropPuzzle scene
2. Set dropper interval to 0.05s
3. Spawn 1000+ balls
4. Check FPS stays above 60
5. Verify GPU instancing working (batches of 1023)
