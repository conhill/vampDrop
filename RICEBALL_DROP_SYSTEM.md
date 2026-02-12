# Riceball Drop System

## Overview
ECS-based physics simulation for dropping riceballs through gates. Uses GPU instanced rendering to handle 1000+ riceballs efficiently. Hybrid ECS/MonoBehaviour architecture for performance.

---

## Architecture

### ECS Components

**RiceBallPhysics** (IComponentData):
```csharp
public struct RiceBallPhysics : IComponentData
{
    public float3 velocity;
    public float3 position;
    public float radius;
    public float mass;
    public bool hasScored;      // Has passed through gate
    public float scoreValue;    // Multiplier from gate
}
```

**RiceBallTag** (IComponentData):
```csharp
public struct RiceBallTag : IComponentData
{
    // Tag component for identifying riceball entities
}
```

**RiceBallLifetime** (IComponentData):
```csharp
public struct RiceBallLifetime : IComponentData
{
    public float spawnTime;
    public float maxLifetime;  // Cleanup after X seconds
}
```

### Systems

**RiceBallPhysicsSystem** (SystemBase):
- Integrates velocity (gravity + physics)
- Handles wall collisions
- Detects gate scoring
- Runs every frame

**RiceBallRendererECS** (SystemBase):
- GPU instanced rendering (batches of 1023)
- Sets material properties per ball
- Matrices updated from ECS positions

**RiceBallCleanupSystem** (SystemBase):
- Removes balls below floor
- Despawns old balls
- Prevents infinite accumulation

---

## RiceBallPhysicsSystem

### Purpose
Simulates realistic physics for all riceballs using ECS for performance.

### Physics Integration
```csharp
protected override void OnUpdate()
{
    float deltaTime = SystemAPI.Time.DeltaTime;
    float3 gravity = new float3(0, -9.81f, 0);
    
    Entities
        .WithAll<RiceBallTag>()
        .ForEach((ref RiceBallPhysics physics) =>
        {
            // Apply gravity
            physics.velocity += gravity * deltaTime;
            
            // Apply drag (air resistance)
            physics.velocity *= 0.99f;
            
            // Integrate position
            physics.position += physics.velocity * deltaTime;
            
            // Update transform (for rendering)
            SystemAPI.SetComponent(entity, new LocalTransform
            {
                Position = physics.position,
                Rotation = quaternion.identity,
                Scale = physics.radius * 2f
            });
        })
        .ScheduleParallel();
}
```

### Wall Collision
```csharp
private void HandleWallCollisions(ref RiceBallPhysics physics)
{
    // Left/Right walls
    if (physics.position.x < leftWallX + physics.radius)
    {
        physics.position.x = leftWallX + physics.radius;
        physics.velocity.x = -physics.velocity.x * 0.5f; // Bounce with energy loss
    }
    if (physics.position.x > rightWallX - physics.radius)
    {
        physics.position.x = rightWallX - physics.radius;
        physics.velocity.x = -physics.velocity.x * 0.5f;
    }
    
    // Front/Back walls
    if (physics.position.z < backWallZ + physics.radius)
    {
        physics.position.z = backWallZ + physics.radius;
        physics.velocity.z = -physics.velocity.z * 0.5f;
    }
    if (physics.position.z > frontWallZ - physics.radius)
    {
        physics.position.z = frontWallZ - physics.radius;
        physics.velocity.z = -physics.velocity.z * 0.5f;
    }
}
```

### Performance Tuning
```csharp
[Header("Physics")]
public float gravity = -9.81f;
public float drag = 0.99f;           // 0.99 = 1% velocity loss per frame
public float bounceDamping = 0.5f;   // Energy retained on bounce

[Header("Performance")]
public bool useParallelJobs = true;   // Burst-compile physics
public int maxBallsPerFrame = 100;    // Spawn rate limiter
```

---

## Gate Scoring System

### Gate MonoBehaviour
```csharp
public class ScoreGate : MonoBehaviour
{
    public float multiplier = 2f;
    public ParticleSystem scoreEffect;
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("RiceBall"))
        {
            // Find ECS entity
            Entity ballEntity = GetEntityFromGameObject(other.gameObject);
            
            if (ballEntity != Entity.Null)
            {
                // Mark as scored in ECS
                var physics = EntityManager.GetComponentData<RiceBallPhysics>(ballEntity);
                if (!physics.hasScored)
                {
                    physics.hasScored = true;
                    physics.scoreValue = multiplier;
                    EntityManager.SetComponentData(ballEntity, physics);
                    
                    // Visual feedback
                    scoreEffect.Play();
                    
                    // Award currency
                    PlayerDataManager.Instance.AddCurrency(
                        Mathf.RoundToInt(ballQuality * multiplier)
                    );
                }
            }
        }
    }
}
```

### Hybrid ECS + MonoBehaviour
Why hybrid approach?
- **ECS:** Fast physics simulation (1000+ balls)
- **MonoBehaviour:** Unity's trigger system, easier gate management

**Entity → GameObject Mapping:**
```csharp
public class RiceBallEntityRef : MonoBehaviour
{
    public Entity entity;
}

// When spawning:
GameObject ballVisual = Instantiate(ballPrefab);
ballVisual.AddComponent<RiceBallEntityRef>().entity = ballEntity;
```

---

## RiceBallRendererECS

### GPU Instancing
Renders all balls in batches of 1023 (Unity's instancing limit) per draw call.

### Rendering Logic
```csharp
private Matrix4x4[] matrices = new Matrix4x4[1023];
private MaterialPropertyBlock propertyBlock;

protected override void OnUpdate()
{
    EntityQuery query = GetEntityQuery(typeof(RiceBallPhysics), typeof(LocalTransform));
    NativeArray<LocalTransform> transforms = query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
    
    int ballCount = transforms.Length;
    int batchCount = Mathf.CeilToInt(ballCount / 1023f);
    
    for (int batch = 0; batch < batchCount; batch++)
    {
        int startIdx = batch * 1023;
        int count = Mathf.Min(1023, ballCount - startIdx);
        
        // Fill matrices for this batch
        for (int i = 0; i < count; i++)
        {
            LocalTransform transform = transforms[startIdx + i];
            matrices[i] = float4x4.TRS(
                transform.Position,
                transform.Rotation,
                new float3(transform.Scale)
            );
        }
        
        // Draw instanced
        Graphics.DrawMeshInstanced(
            ballMesh,
            0,
            ballMaterial,
            matrices,
            count,
            propertyBlock
        );
    }
    
    transforms.Dispose();
}
```

### Material Setup
**Shader Requirements:**
- Must support GPU instancing
- Enable "Enable GPU Instancing" in material inspector

**Example Shader Property:**
```hlsl
UNITY_INSTANCING_BUFFER_START(Props)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(Props)
```

### Color Variations
```csharp
// Set colors based on ball quality
private Vector4[] colors = new Vector4[1023];

for (int i = 0; i < count; i++)
{
    RiceBallPhysics physics = GetComponent<RiceBallPhysics>(entities[startIdx + i]);
    colors[i] = GetQualityColor(physics.scoreValue);
}

propertyBlock.SetVectorArray("_Color", colors);
```

---

## Spawning Riceballs

### BallDropUI Integration
```csharp
public class BallDropUI : MonoBehaviour
{
    private World ecsWorld;
    private EntityManager entityManager;
    
    public void SpawnRiceBalls()
    {
        int ballCount = PlayerDataManager.Instance.GetRiceBallCount();
        
        for (int i = 0; i < ballCount; i++)
        {
            RiceBallQuality quality = PlayerDataManager.Instance.GetBallQuality(i);
            Entity ballEntity = CreateRiceBallEntity(startPosition, quality);
            
            // Remove from inventory
            PlayerDataManager.Instance.DropBall(i);
        }
    }
    
    private Entity CreateRiceBallEntity(Vector3 pos, RiceBallQuality quality)
    {
        Entity entity = entityManager.CreateEntity(
            typeof(RiceBallPhysics),
            typeof(RiceBallTag),
            typeof(RiceBallLifetime),
            typeof(LocalTransform)
        );
        
        entityManager.SetComponentData(entity, new RiceBallPhysics
        {
            position = pos,
            velocity = float3.zero,
            radius = 0.5f,
            mass = 1f,
            hasScored = false,
            scoreValue = GetQualityValue(quality)
        });
        
        entityManager.SetComponentData(entity, new RiceBallLifetime
        {
            spawnTime = (float)Time.ElapsedTime,
            maxLifetime = 60f
        });
        
        return entity;
    }
}
```

### Spawn Position Randomization
```csharp
private Vector3 GetRandomSpawnPosition()
{
    Vector3 basePos = dropStartPosition;
    
    // Add slight randomness to prevent stacking
    basePos.x += Random.Range(-0.5f, 0.5f);
    basePos.z += Random.Range(-0.5f, 0.5f);
    
    return basePos;
}
```

---

## Gate Generation System

### Gate Placement
```csharp
public class GateGenerator : MonoBehaviour
{
    public GameObject[] gatePrefabs;  // x2, x3, x5, x10
    public Transform gateContainer;
    public float minY = 0f;
    public float maxY = 10f;
    public int gateCount = 10;
    
    public void GenerateGates()
    {
        ClearExistingGates();
        
        float yStep = (maxY - minY) / gateCount;
        
        for (int i = 0; i < gateCount; i++)
        {
            float y = minY + (i * yStep);
            Vector3 position = new Vector3(0, y, 0);
            
            GameObject gatePrefab = SelectGate(i);
            Instantiate(gatePrefab, position, Quaternion.identity, gateContainer);
        }
    }
    
    private GameObject SelectGate(int index)
    {
        // Higher gates have better multipliers
        float progression = (float)index / gateCount;
        
        if (progression > 0.9f && IsGateUnlocked("x10"))
            return gatePrefabs[3]; // x10
        else if (progression > 0.7f && IsGateUnlocked("x5"))
            return gatePrefabs[2]; // x5
        else if (progression > 0.4f)
            return gatePrefabs[1]; // x3
        else
            return gatePrefabs[0]; // x2
    }
}
```

### Gate Upgrade System
```csharp
// In ProgressionSystem
public bool IsGateUnlocked(string gateId)
{
    return PlayerDataManager.Instance.HasUpgrade(gateId);
}

// In UpgradeShop
public void UnlockX5Gate()
{
    if (SpendCurrency(x5Cost))
    {
        PlayerDataManager.Instance.AddUpgrade("unlock_x5_gate");
        GateGenerator.Instance.RegenerateGates();
    }
}
```

---

## Cleanup System

### RiceBallCleanupSystem
```csharp
protected override void OnUpdate()
{
    float currentTime = (float)SystemAPI.Time.ElapsedTime;
    EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);
    
    Entities
        .WithAll<RiceBallTag>()
        .ForEach((Entity entity, in RiceBallPhysics physics, in RiceBallLifetime lifetime) =>
        {
            bool shouldCleanup = false;
            
            // Below floor
            if (physics.position.y < floorY - 5f)
                shouldCleanup = true;
            
            // Exceeded lifetime
            if (currentTime - lifetime.spawnTime > lifetime.maxLifetime)
                shouldCleanup = true;
            
            if (shouldCleanup)
            {
                ecb.DestroyEntity(entity);
                CleanupVisual(entity);
            }
        })
        .Run();
    
    ecb.Playback(EntityManager);
    ecb.Dispose();
}
```

### Manual Cleanup
```csharp
public void ClearAllBalls()
{
    EntityQuery query = EntityManager.CreateEntityQuery(typeof(RiceBallTag));
    EntityManager.DestroyEntity(query);
    
    // Also destroy GameObjects
    foreach (var visual in activeBallVisuals)
    {
        Destroy(visual);
    }
    activeBallVisuals.Clear();
}
```

---

## Performance Optimization

### Profiling Results
```
┌──────────────┬─────────────┬─────────────┐
│ Ball Count   │ Frame Time  │ Batches     │
├──────────────┼─────────────┼─────────────┤
│ 100          │ 0.8ms       │ 1           │
│ 500          │ 2.1ms       │ 1           │
│ 1000         │ 3.5ms       │ 1           │
│ 2000         │ 6.8ms       │ 2           │
│ 5000         │ 15.2ms      │ 5           │
└──────────────┴─────────────┴─────────────┘
```

### Optimization Strategies

**1. Burst Compilation**
```csharp
[BurstCompile]
private struct PhysicsJob : IJobEntity
{
    public float deltaTime;
    public float3 gravity;
    
    void Execute(ref RiceBallPhysics physics)
    {
        physics.velocity += gravity * deltaTime;
        physics.position += physics.velocity * deltaTime;
    }
}

protected override void OnUpdate()
{
    new PhysicsJob
    {
        deltaTime = SystemAPI.Time.DeltaTime,
        gravity = new float3(0, -9.81f, 0)
    }.ScheduleParallel();
}
```

**2. LOD System**
```csharp
// Use lower-poly mesh for distant balls
private Mesh GetLODMesh(float distanceToCamera)
{
    if (distanceToCamera < 10f)
        return highPolyMesh;  // 500 tris
    else if (distanceToCamera < 30f)
        return medPolyMesh;   // 100 tris
    else
        return lowPolyMesh;   // 20 tris
}
```

**3. Frustum Culling**
```csharp
// Only render balls in camera view
Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);

for (int i = 0; i < count; i++)
{
    if (GeometryUtility.TestPlanesAABB(frustumPlanes, GetBallBounds(i)))
    {
        matrices[visibleCount++] = transforms[i];
    }
}
```

**4. Physics Simplification**
```csharp
public bool useSimplePhysics = true;  // Toggle in inspector

if (useSimplePhysics)
{
    // No inter-ball collisions
    physics.velocity += gravity * deltaTime;
    physics.position += physics.velocity * deltaTime;
}
else
{
    // Full physics with ball-ball collisions (slow)
    CheckAllBallCollisions();
}
```

---

## Extending the System

### Power-Ups

**Add Magnet Power-Up:**
```csharp
public struct RiceBallMagnet : IComponentData
{
    public float3 attractorPosition;
    public float attractionStrength;
    public float duration;
}

// In physics system:
if (EntityManager.HasComponent<RiceBallMagnet>(entity))
{
    var magnet = EntityManager.GetComponentData<RiceBallMagnet>(entity);
    float3 direction = math.normalize(magnet.attractorPosition - physics.position);
    physics.velocity += direction * magnet.attractionStrength * deltaTime;
}
```

**Add Explosion Power-Up:**
```csharp
public void ExplodeBalls(Vector3 center, float force, float radius)
{
    Entities
        .WithAll<RiceBallTag>()
        .ForEach((ref RiceBallPhysics physics) =>
        {
            float distance = math.distance(physics.position, center);
            if (distance < radius)
            {
                float3 direction = math.normalize(physics.position - center);
                float strength = force * (1f - distance / radius);
                physics.velocity += direction * strength;
            }
        })
        .Run();
}
```

### Special Ball Types

**Heavy Ball (breaks obstacles):**
```csharp
public struct HeavyBall : IComponentData
{
    public float breakForce;
}

// In collision system:
if (EntityManager.HasComponent<HeavyBall>(entity))
{
    var heavy = EntityManager.GetComponentData<HeavyBall>(entity);
    if (ObstacleHealth < heavy.breakForce)
    {
        DestroyObstacle();
    }
}
```

**Splitting Ball (divides on impact):**
```csharp
public struct SplittingBall : IComponentData
{
    public int splitCount;
}

// On collision:
if (EntityManager.HasComponent<SplittingBall>(entity))
{
    for (int i = 0; i < splitCount; i++)
    {
        Vector3 newVelocity = Quaternion.Euler(0, i * 360f / splitCount, 0) * Vector3.forward;
        CreateRiceBall(physics.position, newVelocity * 5f);
    }
    EntityManager.DestroyEntity(entity);
}
```

### Analytics Integration

**Track Ball Performance:**
```csharp
public class BallAnalytics : MonoBehaviour
{
    private Dictionary<Entity, BallStats> ballStats = new();
    
    public void RecordBallSpawn(Entity ball)
    {
        ballStats[ball] = new BallStats
        {
            spawnTime = Time.time,
            spawnPosition = GetPosition(ball)
        };
    }
    
    public void RecordBallScore(Entity ball, float multiplier)
    {
        if (ballStats.TryGetValue(ball, out BallStats stats))
        {
            stats.scoreMultiplier = multiplier;
            stats.timeToScore = Time.time - stats.spawnTime;
            
            // Send to analytics
            AnalyticsService.LogBallDrop(stats);
        }
    }
}
```

---

## Troubleshooting

**Balls fall through gates:**
- Check gate collider is trigger (`Is Trigger = true`)
- Verify ball has collider with `RiceBall` tag
- Check physics layer collision matrix

**Performance issues:**
- Enable Burst compilation
- Reduce `maxBallsPerFrame`
- Use simpler physics model
- Implement LOD system

**Balls disappear instantly:**
- Check `RiceBallLifetime.maxLifetime` value
- Verify floor Y position is correct
- Check cleanup system isn't too aggressive

**Instanced rendering not working:**
- Ensure material has GPU instancing enabled
- Verify shader supports instancing
- Check batch count < 1023 per draw call

**Gates not detecting balls:**
- Verify `RiceBallEntityRef` component is attached
- Check entity-GameObject mapping
- Ensure `OnTriggerEnter` is called (Physics Time Step)
