# Gameplay Systems

## Overview
Core roguelite mechanics: crafting, quality tiers, shop, upgrades, progression, and economy.

---

## Riceball Crafting System

### How It Works
Players convert rice grains into riceballs with variable quality tiers.

**Formula:**
- 5 Rice Grains → 1 Riceball
- Quality determined by random roll + upgrades

### Quality Tiers
```
┌─────────────┬────────┬──────────────┐
│ Tier        │ Value  │ Base Chance  │
├─────────────┼────────┼──────────────┤
│ Fine        │ 1x     │ 70%          │
│ Good        │ 2x     │ 20%          │
│ Great       │ 3x     │ 8%           │
│ Excellent   │ 5x     │ 2%           │
└─────────────┴────────┴──────────────┘
```

### Implementation

**In `RiceCraftingSystem.cs`:**
```csharp
public RiceBallQuality RollQuality()
{
    // Get current quality chances from ProgressionSystem
    var chances = ProgressionSystem.Instance.GetQualityChances();
    
    float roll = Random.value; // 0.0 to 1.0
    
    if (roll < chances.excellentChance) return RiceBallQuality.Excellent;
    if (roll < chances.greatChance) return RiceBallQuality.Great;
    if (roll < chances.goodChance) return RiceBallQuality.Good;
    return RiceBallQuality.Fine;
}
```

**Quality System Modifications:**
Located in `ProgressionSystem.cs`:
```csharp
public QualityChances GetQualityChances()
{
    // Base chances
    float excellent = 0.02f;
    float great = 0.10f;
    float good = 0.30f;
    
    // Apply upgrades
    excellent += shopInstance.GetUpgradeLevel("excellent_chance") * 0.01f;
    great += shopInstance.GetUpgradeLevel("great_chance") * 0.02f;
    good += shopInstance.GetUpgradeLevel("good_chance") * 0.05f;
    
    return new QualityChances(excellent, great, good);
}
```

### Crafting UI
- Shows rice count, riceball count
- "Craft" button (requires 5 rice)
- Visual feedback on quality rolled
- Inventory updates automatically via PlayerDataManager

---

## Upgrade Shop System

### Shop Architecture

**UpgradeShop.cs** - Manages purchases and price scaling

**Available Upgrades:**

| ID | Name | Effect | Base Price | Scaling |
|----|------|--------|-----------|---------|
| `excellent_chance` | Excellent Chance | +1% excellent quality per level | 100 | ×1.5 |
| `great_chance` | Great Chance | +2% great quality per level | 50 | ×1.4 |
| `good_chance` | Good Chance | +5% good quality per level | 20 | ×1.3 |
| `max_riceballs` | Max Riceballs | +1 max riceball capacity | 30 | ×1.2 |
| `unlock_x2_gate` | x2 Multiplier Gate | Unlock x2 gates | 200 | N/A |
| `unlock_x3_gate` | x3 Multiplier Gate | Unlock x3 gates (requires x2) | 500 | N/A |
| `unlock_x5_gate` | x5 Multiplier Gate | Unlock x5 gates (requires x3) | 1000 | N/A |

### Purchase Logic
```csharp
public bool TryPurchaseUpgrade(string upgradeId)
{
    int currentLevel = GetUpgradeLevel(upgradeId);
    int cost = GetUpgradeCost(upgradeId, currentLevel);
    
    if (PlayerDataManager.Instance.SpendCurrency(cost))
    {
        SetUpgradeLevel(upgradeId, currentLevel + 1);
        ProgressionSystem.Instance.UpdateProgression();
        return true;
    }
    return false;
}
```

### Adding New Upgrades

**Step 1:** Add to UpgradeShop dictionary
```csharp
private void InitializeUpgrades()
{
    upgrades.Add("new_upgrade_id", new UpgradeData
    {
        id = "new_upgrade_id",
        displayName = "New Upgrade",
        basePrice = 100,
        priceScaling = 1.3f,
        maxLevel = 10
    });
}
```

**Step 2:** Apply effect in ProgressionSystem
```csharp
public void UpdateProgression()
{
    int newUpgradeLevel = shopInstance.GetUpgradeLevel("new_upgrade_id");
    // Apply effect (modify chances, spawn rates, etc.)
}
```

**Step 3:** Add UI (optional)
- Create button in shop UI
- Wire up purchase event
- Display level/cost

---

## Currency System

### Earning Currency
```csharp
// In BallDropUI.cs or gate detection:
int ballValue = GetBallQualityValue(quality); // 1, 2, 3, or 5
int gateMultiplier = GetGateMultiplier(gate); // x2, x3, x5
int earned = ballValue * gateMultiplier;

PlayerDataManager.Instance.AddCurrency(earned);
```

### Spending Currency
```csharp
// In shop:
if (PlayerDataManager.Instance.SpendCurrency(cost))
{
    // Purchase succeeded
}
```

### Currency Sources
- **Primary:** Dropping riceballs through goals
- **Multipliers:** Gates multiply ball quality value
- **Bonus:** (Future) Daily quests, achievements

---

## Progression System

### Purpose
Bridges shop upgrades → gameplay effects

### Key Responsibilities
1. Calculate quality chances from upgrades
2. Generate gates based on unlocked multipliers
3. Determine max riceball capacity
4. Apply buffs/modifiers

### Gate Generation
```csharp
public List<GateConfig> GenerateGates()
{
    List<GateConfig> gates = new List<GateConfig>();
    
    // Always spawn goal (x1)
    gates.Add(new GateConfig { type = GateType.Goal, multiplier = 1 });
    
    // Spawn multiplier gates if unlocked
    if (shopInstance.GetUpgradeLevel("unlock_x2_gate") > 0)
    {
        gates.Add(new GateConfig { type = GateType.Multiplier, multiplier = 2 });
    }
    
    if (shopInstance.GetUpgradeLevel("unlock_x3_gate") > 0)
    {
        gates.Add(new GateConfig { type = GateType.Multiplier, multiplier = 3 });
    }
    
    if (shopInstance.GetUpgradeLevel("unlock_x5_gate") > 0)
    {
        gates.Add(new GateConfig { type = GateType.Multiplier, multiplier = 5 });
    }
    
    return gates;
}
```

### Integration Points
- Called after every purchase (UpgradeShop)
- Called when loading DropPuzzle scene (GridPuzzleLoader)
- Called at game start (Awake/Start)

---

## Inventory System

### PlayerDataManager Storage
```csharp
public class PlayerDataManager : MonoBehaviour
{
    // Inventory
    public int RiceGrains { get; private set; }
    public List<RiceBallData> RiceBalls { get; private set; }
    public int MaxRiceBalls { get; private set; } = 5;
    
    // Currency
    public int Currency { get; private set; }
    
    // Stats
    public int TotalRiceBallsDropped { get; private set; }
    public int TotalScore { get; private set; }
}
```

### Operations
```csharp
// Add rice
PlayerDataManager.Instance.AddRice(1);

// Craft riceball
var quality = RollQuality();
PlayerDataManager.Instance.AddRiceBall(quality);
PlayerDataManager.Instance.RemoveRice(5);

// Drop riceball
PlayerDataManager.Instance.RemoveRiceBall(ballData);
PlayerDataManager.Instance.IncrementTotalDropped();
```

### Capacity Management
Max riceballs dynamically set from upgrades:
```csharp
void UpdateMaxRiceBalls()
{
    int baseCapacity = 5;
    int upgradeBonus = UpgradeShop.Instance.GetUpgradeLevel("max_riceballs");
    PlayerDataManager.Instance.SetMaxRiceBalls(baseCapacity + upgradeBonus);
}
```

---

## Roguelite Meta-Progression

### Current Implementation
- **Per-Run:** Rice, riceballs, score reset on scene restart
- **Persistent:** Upgrades, currency remain between runs
- **Scaling:** Prices increase per level purchased

### Future Expansion Ideas

**Run-Based Modifiers:**
```csharp
// Start each run with random modifier
public enum RunModifier
{
    DoubleRice,      // 2x rice spawn rate
    LuckyDrops,      // +10% all quality chances
    ExpensiveShop,   // 2x shop prices, 3x currency earned
    TimeAttack       // Half time, bonus currency if complete
}
```

**Unlock System:**
```csharp
public class UnlockManager
{
    Dictionary<string, bool> unlockedContent;
    
    public void UnlockContent(string id)
    {
        unlockedContent[id] = true;
        // Unlocks: new drop zones, special riceball types, etc.
    }
}
```

**Prestige/Reset System:**
```csharp
public void PrestigeReset()
{
    // Reset all upgrades
    // Grant prestige points
    // Unlock prestige-only upgrades
    // Keep high scores/achievements
}
```

---

## Day/Night Cycle System

### Purpose
Time pressure mechanic for collection phase

### Implementation
**DayNightCycleManager.cs:**
```csharp
public float timeRemaining = 300f; // 5 minutes
public bool isPaused = false;

void Update()
{
    if (isPaused) return;
    
    timeRemaining -= Time.deltaTime;
    
    if (timeRemaining <= 0)
    {
        OnTimerExpired();
    }
}
```

### Pause/Resume
Pauses during:
- Shop browsing (BuyZoneInteraction.OpenShop)
- Ball drop completion (BallDropUI.ShowCompletionScreen)
- Comic scenes (auto-paused)

### Timer Effects
```csharp
void OnTimerExpired()
{
    // Force player to drop balls
    // Add penalty (reduce currency, quality chances)
    // Auto-transition to drop puzzle
}
```

---

## Adding New Systems

### Example: Achievement System

**Step 1: Create Manager**
```csharp
public class AchievementManager : MonoBehaviour
{
    public static AchievementManager Instance;
    
    private HashSet<string> unlockedAchievements = new HashSet<string>();
    
    public void UnlockAchievement(string id)
    {
        if (unlockedAchievements.Add(id))
        {
            OnAchievementUnlocked(id);
        }
    }
}
```

**Step 2: Add to GameSystems**
- Attach to GameSystems GameObject
- Add DontDestroyOnLoad logic

**Step 3: Trigger from Gameplay**
```csharp
// In RiceBallGateInteractionSystem:
if (gateType == GateType.Goal && ballQuality == RiceBallQuality.Excellent)
{
    AchievementManager.Instance.UnlockAchievement("excellent_goal");
}
```

**Step 4: Reward (Optional)**
```csharp
void OnAchievementUnlocked(string id)
{
    int reward = GetAchievementReward(id);
    PlayerDataManager.Instance.AddCurrency(reward);
    ShowAchievementPopup(id);
}
```
