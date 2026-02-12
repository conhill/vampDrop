# 🎮 VAMP4 - Roguelite Drop Puzzle System

## 📚 DEVELOPER DOCUMENTATION

**New to this project?** Start with the comprehensive developer guides below:

### 🏗️ [GAME_STRUCTURE.md](GAME_STRUCTURE.md)
**Game Architecture & Flow**
- Core game loop (FPS → Crafting → Drop → Shop)
- Scene architecture breakdown
- Persistent systems (DontDestroyOnLoad)
- Scene transitions & data persistence
- Build settings, tags, layers
- Testing workflows

### ⚙️ [GAMEPLAY_SYSTEMS.md](GAMEPLAY_SYSTEMS.md)
**Core Gameplay Mechanics**
- Riceball crafting system (5 rice → 1 ball)
- Quality tiers & probability tables
- Upgrade shop (all upgrades, pricing formulas)
- Currency & progression systems
- Inventory management
- Adding new gameplay systems

### 🌾 [RICE_SPAWNING.md](RICE_SPAWNING.md)
**ECS Rice Spawning System**
- ECS architecture (components, systems)
- Spawn rate & distribution logic
- Collection mechanics
- Scene transition handling
- Performance tuning
- Extending spawn patterns

### 🎯 [RICEBALL_DROP_SYSTEM.md](RICEBALL_DROP_SYSTEM.md)
**ECS Ball Physics & Rendering**
- ECS physics simulation (1000+ balls)
- GPU instanced rendering
- Gate interaction & scoring
- Wall collision system
- Performance optimization (Burst, jobs)
- Adding power-ups & special balls

### 🎬 [COMIC_SYSTEM.md](COMIC_SYSTEM.md)
**Narrative Comic System**
- Horizontal scrolling comic architecture
- Creating comic sequences (ScriptableObjects)
- Panel sizing & element positioning
- Animation system (12 types)
- Loading comics from code
- Extending with new animations

---

## 🎮 PROJECT OVERVIEW

A **roguelite progression system** for a Plinko-style drop puzzle game with FPS collection mechanics.

### The Core Loop:
```
🏃 FPS Scene: Collect rice grains by walking over them
    ↓
⚙️ Crafting: Convert 5 rice → 1 riceball (random quality roll)
    ↓
🎯 Drop Puzzle: Drop riceballs through gates
    ↓
💰 Score: Ball quality × gate multiplier = currency earned
    ↓
🛒 Shop: Purchase permanent upgrades
    ↓
🔁 REPEAT: Better quality chances, more gates, faster collection
```

### Key Systems:
- **4-Tier Quality System:** Fine (70%), Good (20%), Great (8%), Excellent (2%)
- **ECS Physics:** 1000+ riceballs simulated with GPU instancing
- **Persistent Upgrades:** 15+ upgrades across crafting/collection/gates
- **Dynamic Gates:** x2, x3, x5, x10 multipliers (unlockable)
- **Horizontal Comics:** ScriptableObject-based cutscene system
- **DontDestroyOnLoad:** Persistent GameSystems maintains data across scenes

---

## 🎮 KEYBOARD CONTROLS

### Debug Keys (Works in ANY scene):
| Key | Action | Purpose |
|-----|--------|---------|
| **C** | Add $100 | Test shop purchases |
| **G** | Add 50 rice | Test crafting |
| **E** | Craft riceballs | Convert rice to balls |
| **1-5** | Buy upgrades | Test progression |
| **R** | Reset progress | ⚠️ Clear all data |

### Gameplay Keys:
| Key | Action | Scene |
|-----|--------|-------|
| **SPACE** | Drop balls | Drop Puzzle |
| **WASD** | Move | FPS Collect |
| **ENTER** | Continue | Comic Scene |

---

## 🔧 SCENE STRUCTURE

### Build Settings Order:
1. **ComicScene** - Horizontal scrolling comics
2. **FPS_Collect** - Rice grain collection (first-person)
3. **DropPuzzle** - Riceball drop physics puzzle

### Required GameObjects:

**Both FPS_Collect & DropPuzzle:**
- `GameSystems` (DontDestroyOnLoad)
  - PlayerDataManager
  - UpgradeShop
  - ProgressionSystem
  - DayNightCycleManager
  - DebugUpgradeUI

**DropPuzzle Only:**
- `BallMeshSetup` - Cylinder mesh generator
- `RiceBallRendererECS` - GPU instanced rendering
- `Dropper` - Ball spawner (Use Inventory System ✓)

**FPS_Collect Only:**
- `Player` with RiceCollectorFPS component
- Rice grain prefabs (Tag: "RiceGrain", Trigger ✓)

---

## 📊 QUALITY & UPGRADE SYSTEM

### Quality Progression:
| Quality | Multiplier | Default % | Max % (Upgraded) | Color |
|---------|-----------|-----------|------------------|-------|
| Fine | 1x | 70% | 49% | White |
| Good | 2x | 20% | 30% | Yellow |
| Great | 4x | 8% | 15% | Blue |
| Excellent | 8x | 2% | 6% | Purple |

### Upgrade Categories:

**Crafting Upgrades:**
- `good_chance` - Increase Good quality % (Base: $100, ×1.5 scaling)
- `great_chance` - Increase Great quality % (Base: $250, ×1.5 scaling)
- `excellent_chance` - Increase Excellent quality % (Base: $500, ×1.5 scaling)

**Gate Upgrades:**
- `unlock_x5_gate` - Unlock x5 multiplier gates ($1000 flat)
- `unlock_x10_gate` - Unlock x10 multiplier gates ($5000 flat)

**Inventory Upgrades:**
- `max_riceballs` - +1 inventory capacity (Base: $50, ×1.2 scaling)

**FPS Upgrades:**
- `pickup_radius` - Increase collection radius (Base: $30, ×1.3 scaling)

---

## 🎯 QUICK START

### 30-Second Test:
```
1. Open DropPuzzle.unity
2. Verify GameSystems exists with 5 components
3. Press Play
4. Press [G] → [E] → [SPACE]
5. Balls drop! ✅
```

### Full Test Sequence:
```
Test Crafting:    [G] → [E] → Console shows "Crafted 10 riceballs"
Test Inventory:   Check DebugUpgradeUI shows ball counts
Test Dropping:    [SPACE] → Balls drop from spawn point
Test Upgrades:    [C] × 3 → [5] → [E] → Some Good quality balls appear
Test Persistence: Switch scenes → Inventory persists
```

---

## 🐛 TROUBLESHOOTING

| Issue | Fix |
|-------|-----|
| "PlayerDataManager not found" | Create GameSystems GameObject with required components |
| No balls drop | Press [G] then [E] to craft balls first |
| Balls invisible | Assign material to BallMeshSetup & RiceBallRendererECS |
| Can't pick rice in FPS | Tag rice as "RiceGrain", enable Is Trigger |
| Infinite ball drop | Enable "Use Inventory System" on Dropper |

**More issues?** See detailed troubleshooting in [GAMEPLAY_SYSTEMS.md](GAMEPLAY_SYSTEMS.md)

---

## 📁 KEY SCRIPT LOCATIONS

```
Assets/Scripts/
├── PlayerDataManager.cs         # Master data, inventory, currency
├── UpgradeShop.cs               # Shop system, upgrade purchases
├── ProgressionSystem.cs         # Gate generation, quality logic
├── RiceCraftingSystem.cs        # 5 rice → 1 ball conversion UI
├── DebugUpgradeUI.cs            # Testing interface (on-screen)
├── RiceCollectorFPS.cs          # FPS rice pickup
├── DropperControllerECS.cs      # Ball spawner (inventory mode)
├── RiceBallPhysicsECS.cs        # ECS physics simulation
├── RiceBallRendererECS.cs       # GPU instanced rendering
└── ComicScene/
    ├── ComicSceneManager.cs     # Main comic controller
    ├── ComicSequenceConfig.cs   # ScriptableObject configs
    ├── ComicPanel.cs            # Panel/element data structures
    └── ComicSceneLoader.cs      # Static loading helper
```

---

## 🏆 PROJECT STATUS

### ✅ Complete:
- Roguelite progression architecture
- 4-tier quality crafting system
- ECS ball physics (1000+ balls)
- GPU instanced rendering
- Persistent upgrade system
- Debug UI for testing
- Horizontal comic system (12 animation types)
- Scene persistence (DontDestroyOnLoad)

### 🔄 In Progress:
- Shop UI (currently debug keys)
- Save/load system (JSON serialization)
- Visual ball differentiation (quality colors)

### ⏳ Planned:
- More upgrade types (speed, special abilities)
- Achievement system
- Stats tracking & analytics
- Polish & particle effects

---

## 📝 CONTRIBUTING

When adding new systems:
1. Read [GAMEPLAY_SYSTEMS.md](GAMEPLAY_SYSTEMS.md) - "Adding New Systems" section
2. Follow the architecture patterns (singleton managers, DontDestroyOnLoad)
3. Add debug hotkeys for testing
4. Update relevant documentation

---

## 🚀 GETTING STARTED

**New Developer?**
1. Read [GAME_STRUCTURE.md](GAME_STRUCTURE.md) - Understand the game loop
2. Read [GAMEPLAY_SYSTEMS.md](GAMEPLAY_SYSTEMS.md) - Learn core mechanics
3. Run the 30-second test above
4. Explore other READMEs as needed

**Ready to extend the game?** Pick a system README and dive into the "Extending" section!

---

**Last Updated:** Jan 2025  
**Unity Version:** 2022.3 LTS  
**ECS Version:** com.unity.entities 1.0+
