# Rice Spawning System

## Overview
ECS-based rice grain spawning system for the FPS collection scene. Spawns rice periodically in random positions, handles despawning, and integrates with the collection system.

---

## Architecture

### ECS Components

**RiceEntity** (IComponentData):
```csharp
public struct RiceEntity : IComponentData
{
    public float spawnTime;      // When this rice was spawned
    public bool isCollected;     // Flag for collection (despawn next frame)
}
```

**RiceHidden** (IComponentData):
```csharp
public struct RiceHidden : IComponentData
{
    // Tag component - rice exists but is hidden
    // Used when transitioning scenes
}
```

### Systems

**RiceSpawnerECS** (SystemBase):
- Spawns rice entities at intervals
- Manages spawn positions and randomization
- Tracks active rice count

**RiceDespawnSystem** (SystemBase):
- Removes old uncollected rice
- Cleans up collected rice
- Prevents excessive entity buildup

---

## RiceSpawnerECS

### Inspector Properties
```csharp
[Header("Spawning")]
public GameObject ricePrefab;           // Visual prefab to instantiate
public float spawnInterval = 2f;        // Seconds between spawns
public int maxRiceCount = 50;           // Max active rice entities
public float riceLifetime = 60f;        // Seconds before despawn

[Header("Spawn Area")]
public Vector3 spawnAreaCenter;         // Center of spawn zone
public Vector3 spawnAreaSize;           // Size of spawn box
public LayerMask groundLayer;           // Raycast to find ground
```

### Spawning Logic
```csharp
protected override void OnUpdate()
{
    if (Time.time < nextSpawn Time) return;
    
    // Count active rice (not collected, not hidden)
    int activeCount = CountActiveRice();
    if (activeCount >= maxRiceCount) return;
    
    // Spawn new rice
    Vector3 spawnPos = GetRandomSpawnPosition();
    Entity riceEntity = CreateRiceEntity(spawnPos);
    GameObject visual = InstantiateRiceVisual(spawnPos);
    
    nextSpawnTime = Time.time + spawnInterval;
}
```

### Random Position Generation
```csharp
private Vector3 GetRandomSpawnPosition()
{
    // Random point within spawn box
    Vector3 randomOffset = new Vector3(
        Random.Range(-spawnAreaSize.x / 2f, spawnAreaSize.x / 2f),
        spawnAreaSize.y,
        Random.Range(-spawnAreaSize.z / 2f, spawnAreaSize.z / 2f)
    );
    
    Vector3 spawnPoint = spawnAreaCenter + randomOffset;
    
    // Raycast down to find ground
    if (Physics.Raycast(spawnPoint, Vector3.down, out RaycastHit hit, 100f, groundLayer))
    {
        return hit.point + Vector3.up * 0.5f; // Slightly above ground
    }
    
    return spawnPoint;
}
```

### Entity Creation
```csharp
private Entity CreateRiceEntity(Vector3 position)
{
    Entity riceEntity = EntityManager.CreateEntity(
        typeof(RiceEntity),
        typeof(LocalTransform)
    );
    
    EntityManager.SetComponentData(riceEntity, new RiceEntity
    {
        spawnTime = (float)Time.ElapsedTime,
        isCollected = false
    });
    
    EntityManager.SetComponentData(riceEntity, new LocalTransform
    {
        Position = position,
        Rotation = quaternion.identity,
        Scale = 1f
    });
    
    return riceEntity;
}
```

---

## RiceDespawnSystem

### Purpose
Automatically removes rice that is:
1. Older than `riceLifetime`
2. Marked as collected (`isCollected = true`)

### Implementation
```csharp
protected override void OnUpdate()
{
    float currentTime = (float)SystemAPI.Time.ElapsedTime;
    
    EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);
    
    // Despawn old or collected rice
    Entities
        .WithAll<RiceEntity>()
        .ForEach((Entity entity, in RiceEntity rice) =>
        {
            bool shouldDespawn = rice.isCollected || 
                                 (currentTime - rice.spawnTime > riceLifetime);
            
            if (shouldDespawn)
            {
                ecb.DestroyEntity(entity);
                DestroyVisual(entity); // Destroy GameObject counterpart
            }
        })
        .Run();
    
    ecb.Playback(EntityManager);
    ecb.Dispose();
}
```

---

## RiceCollectorFPS Integration

### Pickup Detection
RiceCollectorFPS (MonoBehaviour) detects rice via OnTriggerEnter:

```csharp
private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("RiceGrain"))
    {
        // Find corresponding ECS entity
        Entity riceEntity = FindRiceEntity(other.gameObject);
        
        if (riceEntity != Entity.Null)
        {
            // Mark for collection
            EntityManager.SetComponentData(riceEntity, new RiceEntity
            {
                spawnTime = rice.spawnTime,
                isCollected = true  // Despawn system will handle cleanup
            });
            
            // Update inventory
            PlayerDataManager.Instance.AddRice(1);
            
            // Visual feedback
            PlayCollectionEffect(other.transform.position);
        }
    }
}
```

### Entity-GameObject Mapping
To link GameObjects to ECS entities:

**Option 1: EntityAuthoring Component**
```csharp
public class RiceEntityRef : MonoBehaviour
{
    public Entity entity;
}

// In spawner:
visual.AddComponent<RiceEntityRef>().entity = riceEntity;
```

**Option 2: Dictionary Lookup**
```csharp
private Dictionary<GameObject, Entity> riceMap = new Dictionary<GameObject, Entity>();

// When spawning:
riceMap[visual] = riceEntity;

// When collecting:
Entity entity = riceMap[other.gameObject];
```

---

## Scene Transition Handling

### Problem
Rice entities persist even when switching scenes (ECS data survives scene loads).

### Solution: Hide/Unhide Rice

**When Leaving FPS Scene:**
```csharp
public void HideRiceEntities()
{
    EntityQuery query = EntityManager.CreateEntityQuery(typeof(RiceEntity));
    EntityManager.AddComponent<RiceHidden>(query);
    
    // Hide GameObjects
    foreach (var visual in activeRiceVisuals)
    {
        visual.SetActive(false);
    }
}
```

**When Returning to FPS Scene:**
```csharp
public void UnhideRiceEntities()
{
    EntityQuery query = EntityManager.CreateEntityQuery(
        typeof(RiceEntity), 
        typeof(RiceHidden)
    );
    
    EntityManager.RemoveComponent<RiceHidden>(query);
    
    // Show GameObjects
    foreach (var visual in activeRiceVisuals)
    {
        visual.SetActive(true);
    }
}
```

**Triggered From:**
- BallDropUI.Start() - Hide rice when entering drop scene
- BallDropUI.GoBackToFPS() - Unhide rice when returning

---

## Spawn Area Visualization

### Gizmos
```csharp
private void OnDrawGizmos()
{
    Gizmos.color = Color.green;
    Gizmos.DrawWireCube(spawnAreaCenter, spawnAreaSize);
}
```

### Setup in Editor
1. Select RiceSpawner GameObject
2. Adjust Spawn Area Center (drag in Scene view)
3. Adjust Spawn Area Size to cover walkable areas
4. Set Ground Layer to terrain/floor layers

---

## Performance Tuning

### Spawn Rate vs Max Count
```
┌─────────────┬──────────────┬─────────────┐
│ Spawn       │ Max Count    │ Use Case    │
│ Interval    │              │             │
├─────────────┼──────────────┼─────────────┤
│ 1s          │ 30           │ Easy mode   │
│ 2s          │ 50           │ Normal      │
│ 3s          │ 70           │ Hard mode   │
│ 5s          │ 100          │ Scarce rice │
└─────────────┴──────────────┴─────────────┘
```

### Lifetime Balance
- **Short (30s):** Encourages active collection
- **Medium (60s):** Balanced spawning/despawning
- **Long (120s):** Map fills up, performance hit

### ECS Query Optimization
```csharp
// Cached query (faster)
private EntityQuery riceQuery;

protected override void OnCreate()
{
    riceQuery = GetEntityQuery(typeof(RiceEntity));
}

protected override void OnUpdate()
{
    int count = riceQuery.CalculateEntityCount();
}
```

---

## Extending the System

### Custom Spawn Patterns

**Clustered Spawning:**
```csharp
private Vector3 GetClusteredSpawnPosition(Vector3 clusterCenter)
{
    float clusterRadius = 5f;
    Vector3 offset = Random.insideUnitSphere * clusterRadius;
    offset.y = 0; // Keep on ground plane
    return clusterCenter + offset;
}
```

**Path-Based Spawning:**
```csharp
public Transform[] spawnPath;
private int currentPathIndex = 0;

private Vector3 GetPathSpawnPosition()
{
    Vector3 pos = spawnPath[currentPathIndex].position;
    currentPathIndex = (currentPathIndex + 1) % spawnPath.Length;
    return pos;
}
```

### Rice Variants

**Add Quality Types:**
```csharp
public enum RiceType
{
    Normal,      // 1 rice
    Golden,      // 3 rice
    Rainbow      // 5 rice + quality bonus
}

public struct RiceEntity : IComponentData
{
    public RiceType type;
    public float spawnTime;
    public bool isCollected;
}
```

**Visual Differentiation:**
```csharp
private GameObject InstantiateRiceVisual(Vector3 pos, RiceType type)
{
    GameObject prefab = type switch
    {
        RiceType.Golden => goldenRicePrefab,
        RiceType.Rainbow => rainbowRicePrefab,
        _ => normalRicePrefab
    };
    
    return Instantiate(prefab, pos, Quaternion.identity);
}
```

### Spawn Events

**Add Event System:**
```csharp
public class RiceSpawnEvents
{
    public event Action<Entity, Vector3> OnRiceSpawned;
    public event Action<Entity> OnRiceDespawned;
    public event Action<Entity> OnRiceCollected;
}

// In spawner:
RiceSpawnEvents.Instance.OnRiceSpawned?.Invoke(entity, position);

// In collector:
RiceSpawnEvents.Instance.OnRiceCollected?.Invoke(entity);
```

**Use Cases:**
- Analytics tracking
- Audio cues
- Particle effects
- Tutorial hints

---

## Troubleshooting

**Rice not spawning:**
- Check `maxRiceCount` not reached
- Verify spawn area overlaps walkable ground
- Check `groundLayer` mask includes terrain
- Ensure `spawnInterval` > 0

**Rice spawns in air/underground:**
- Verify `groundLayer` is correct
- Check raycast max distance (100f default)
- Adjust spawn height offset

**Rice persists after scene change:**
- Call `HideRiceEntities()` when leaving scene
- Check ECS World didn't get destroyed
- Verify RiceHidden component is added

**Performance issues:**
- Reduce `maxRiceCount`
- Increase `riceLifetime` to reduce churn
- Cache EntityQuery instances
- Use Burst-compiled systems if needed
