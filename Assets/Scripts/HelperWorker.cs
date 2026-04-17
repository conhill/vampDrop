using UnityEngine;
using UnityEngine.AI;
using Unity.Entities;
using Unity.Collections;
using System.Collections;
using Vampire.Rice;

namespace Vampire.Helpers
{
    using DropPuzzle;

    /// <summary>
    /// A purchased worker that roams a RiceSpawnPointAuthoring zone and collects rice.
    /// Spawned by SnerdShop. Calls AddRice(1) per collected entity.
    /// Assign a zone via AssignZone(); auto-unassigns when the zone is depleted.
    /// </summary>
    public class HelperWorker : MonoBehaviour
    {
        // ── Random name pool ────────────────────────────────────────────────
        private static readonly string[] NamePool =
        {
            "Grub", "Fizzle", "Sprocket", "Nubs", "Twitch", "Gloop",
            "Skree", "Bonk", "Murgle", "Pip", "Snark", "Widge",
            "Crud", "Blot", "Fenk", "Zorp", "Gibble", "Runt"
        };

        [Header("Configuration")]
        public int workerIndex = 0;
        public string WorkerName;       // Set at spawn time

        [Header("Detection")]
        public float collectionDistance = 2.5f;
        public float roamRadius = 30f;      // fallback when no zone assigned

        [Header("Visuals")]
        public Animator workerAnimator;
        public ParticleSystem collectEffect;

        // ── Zone assignment ──────────────────────────────────────────────────
        public RiceSpawnPointAuthoring AssignedZone { get; private set; }
        public bool IsPaused { get; private set; }

        /// <summary>Fired when the assigned zone runs out of rice.</summary>
        public event System.Action<HelperWorker> OnZoneDepleted;

        // ── Runtime ─────────────────────────────────────────────────────────
        private NavMeshAgent navAgent;
        private EntityManager entityManager;
        private EntityQuery riceQuery;
        private bool ecsReady = false;

        private enum WorkerState { Roaming, Seeking, Collecting }
        private WorkerState state = WorkerState.Roaming;

        private Vector3 spawnPos;
        private Vector3 targetRicePos;
        private Entity targetRiceEntity;
        private bool hasTarget = false;
        private float lastCollectTime = -999f;

        private PlayerDataManager PlayerData => PlayerDataManager.Instance;

        private float CollectionInterval => PlayerData != null
            ? 1f / Mathf.Max(0.1f, PlayerData.Helpers.ricePerSecond)
            : 1f;

        private void Awake()
        {
            navAgent = GetComponent<NavMeshAgent>();
            if (navAgent == null)
                navAgent = gameObject.AddComponent<NavMeshAgent>();

            navAgent.acceleration = 10f;
            navAgent.angularSpeed = 360f;
            navAgent.stoppingDistance = collectionDistance * 0.8f;
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(WorkerName))
                WorkerName = NamePool[Random.Range(0, NamePool.Length)];

            spawnPos = transform.position;
            ApplySpeedFromData();

            if (!navAgent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                {
                    navAgent.Warp(hit.position);
                    spawnPos = hit.position;
                }
                else
                {
                    Debug.LogWarning($"[HelperWorker] {WorkerName}: no NavMesh near spawn — worker inactive. Bake NavMesh in FPS_Collect.");
                }
            }

            InitECS();
            StartCoroutine(AILoop());
            StartCoroutine(ZoneDepletionCheckLoop());
        }

        // ── Public zone / pause API ──────────────────────────────────────────

        public void AssignZone(RiceSpawnPointAuthoring zone)
        {
            AssignedZone = zone;
            hasTarget = false;

            if (zone != null)
            {
                // Warp worker into the zone
                Vector3 center = zone.GetSpawnBounds().center;
                center.y = spawnPos.y;
                if (NavMesh.SamplePosition(center, out NavMeshHit hit, 20f, NavMesh.AllAreas))
                {
                    navAgent.Warp(hit.position);
                    spawnPos = hit.position;
                }
                Debug.Log($"[HelperWorker] {WorkerName} assigned to zone '{zone.ZoneName}'");
            }
            else
            {
                Debug.Log($"[HelperWorker] {WorkerName} unassigned from zone");
            }
        }

        public void UnassignZone() => AssignZone(null);

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            navAgent.isStopped = paused;
        }

        private void ApplySpeedFromData()
        {
            float baseSpeed = 3.5f;
            if (PlayerData != null)
                navAgent.speed = baseSpeed * PlayerData.Helpers.movementSpeed;
            else
                navAgent.speed = baseSpeed;
        }

        /// <summary>Called by SnerdShop after an upgrade purchase to update speed.</summary>
        public void RefreshStats()
        {
            ApplySpeedFromData();
        }

        private void InitECS()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            entityManager = world.EntityManager;
            riceQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Vampire.Rice.RiceEntity>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );
            ecsReady = true;
        }

        private IEnumerator AILoop()
        {
            while (true)
            {
                if (!IsPaused)
                {
                    switch (state)
                    {
                        case WorkerState.Roaming:   HandleRoaming();   break;
                        case WorkerState.Seeking:   HandleSeeking();   break;
                        case WorkerState.Collecting: HandleCollecting(); break;
                    }
                    UpdateAnimation();
                }
                yield return new WaitForSeconds(0.2f);
            }
        }

        private IEnumerator ZoneDepletionCheckLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(3f);

                if (AssignedZone == null || !ecsReady) continue;

                Bounds zoneBounds = AssignedZone.GetSpawnBounds();
                int riceInZone = CountRiceInBounds(zoneBounds);

                if (riceInZone == 0)
                {
                    Debug.Log($"[HelperWorker] {WorkerName}: zone '{AssignedZone.ZoneName}' depleted — unassigning");
                    var depletedZone = AssignedZone;
                    UnassignZone();
                    OnZoneDepleted?.Invoke(this);
                }
            }
        }

        private float _lastDiagnosticTime = 0f;

        private void HandleRoaming()
        {
            if (!navAgent.isOnNavMesh)
            {
                LogDiagnostic("not on NavMesh");
                return;
            }

            if (AssignedZone != null)
            {
                if (!ecsReady)
                {
                    LogDiagnostic("ECS not ready — retrying InitECS");
                    InitECS();
                    return;
                }

                Bounds zoneBounds = AssignedZone.GetSpawnBounds();
                int totalRice = riceQuery.IsEmpty ? 0 : -1; // -1 = unknown, query not empty

                if (riceQuery.IsEmpty)
                {
                    LogDiagnostic($"riceQuery is empty (no rice entities in world at all)");
                }
                else if (TryFindNearestRiceInBounds(zoneBounds, out Vector3 ricePos, out Entity riceEnt))
                {
                    targetRicePos = ricePos;
                    targetRiceEntity = riceEnt;
                    hasTarget = true;
                    navAgent.SetDestination(targetRicePos);
                    state = WorkerState.Seeking;
                    return;
                }
                else
                {
                    LogDiagnostic($"zone '{AssignedZone.ZoneName}' bounds {zoneBounds.center} size {zoneBounds.size} — no rice found in XZ range");
                }
            }

            // Wander within zone bounds (or fallback roam radius)
            if (!navAgent.hasPath || navAgent.remainingDistance < 1f)
            {
                Vector3 wanderTarget = GetWanderTarget();
                if (NavMesh.SamplePosition(wanderTarget, out NavMeshHit hit, roamRadius, NavMesh.AllAreas))
                    navAgent.SetDestination(hit.position);
            }
        }

        // Throttled diagnostic — logs once every 3 seconds so it's readable
        private void LogDiagnostic(string reason)
        {
            if (Time.time - _lastDiagnosticTime < 3f) return;
            _lastDiagnosticTime = Time.time;
            Debug.Log($"[HelperWorker] {WorkerName} idle — {reason}");
        }

        private Vector3 GetWanderTarget()
        {
            if (AssignedZone != null)
            {
                Bounds b = AssignedZone.GetSpawnBounds();
                return new Vector3(
                    Random.Range(b.min.x, b.max.x),
                    spawnPos.y,
                    Random.Range(b.min.z, b.max.z));
            }
            Vector3 r = Random.insideUnitSphere * roamRadius + spawnPos;
            r.y = spawnPos.y;
            return r;
        }

        private void HandleSeeking()
        {
            if (!hasTarget)
            {
                state = WorkerState.Roaming;
                return;
            }

            float dist = Vector3.Distance(transform.position, targetRicePos);
            if (dist <= collectionDistance)
            {
                navAgent.isStopped = true;
                state = WorkerState.Collecting;
            }
        }

        private void HandleCollecting()
        {
            float interval = CollectionInterval;
            if (Time.time - lastCollectTime >= interval)
            {
                TryCollect();
            }
        }

        private void TryCollect()
        {
            if (!ecsReady || !hasTarget) { ResetToRoam(); return; }

            // Verify entity still exists
            if (!entityManager.Exists(targetRiceEntity))
            {
                ResetToRoam();
                return;
            }

            // Verify still within range
            try
            {
                var lt = entityManager.GetComponentData<Unity.Transforms.LocalTransform>(targetRiceEntity);
                if (Vector3.Distance(transform.position, lt.Position) > collectionDistance + 1f)
                {
                    ResetToRoam();
                    return;
                }

                entityManager.DestroyEntity(targetRiceEntity);
                lastCollectTime = Time.time;

                if (PlayerData != null)
                    PlayerData.AddRice(1);

                if (collectEffect != null)
                    collectEffect.Play();
            }
            catch
            {
                // Entity may have been destroyed by another system
            }

            ResetToRoam();
        }

        // XZ-only check — GetSpawnBounds() is derived from floor renderers whose Y
        // extent is just the floor thickness. Rice entities sit on top of the floor
        // and would always fail a full 3D Bounds.Contains test.
        private static bool InZoneXZ(Bounds bounds, Vector3 pos)
        {
            return pos.x >= bounds.min.x && pos.x <= bounds.max.x &&
                   pos.z >= bounds.min.z && pos.z <= bounds.max.z;
        }

        private bool TryFindNearestRiceInBounds(Bounds bounds, out Vector3 bestPos, out Entity bestEnt)
        {
            bestPos = Vector3.zero;
            bestEnt = Entity.Null;

            try
            {
                var entities   = riceQuery.ToEntityArray(Allocator.TempJob);
                var transforms = riceQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(Allocator.TempJob);

                float nearestDist = float.MaxValue;
                bool found = false;

                for (int i = 0; i < entities.Length; i++)
                {
                    Vector3 pos = transforms[i].Position;
                    if (!InZoneXZ(bounds, pos)) continue;

                    float d = Vector3.Distance(transform.position, pos);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        bestPos = pos;
                        bestEnt = entities[i];
                        found = true;
                    }
                }

                entities.Dispose();
                transforms.Dispose();
                return found;
            }
            catch { return false; }
        }

        private int CountRiceInBounds(Bounds bounds)
        {
            try
            {
                var transforms = riceQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(Allocator.TempJob);
                int count = 0;
                foreach (var t in transforms)
                    if (InZoneXZ(bounds, t.Position)) count++;
                transforms.Dispose();
                return count;
            }
            catch { return 1; } // assume non-zero on error to avoid false depletion
        }

        private void ResetToRoam()
        {
            hasTarget = false;
            navAgent.isStopped = false;
            state = WorkerState.Roaming;
        }

        private void UpdateAnimation()
        {
            if (workerAnimator == null) return;
            workerAnimator.SetBool("IsWalking", navAgent.velocity.magnitude > 0.1f);
            workerAnimator.SetBool("IsCollecting", state == WorkerState.Collecting);
        }

        private void OnDrawGizmosSelected()
        {
            // Draw assigned zone bounds
            if (AssignedZone != null)
            {
                Gizmos.color = Color.yellow;
                Bounds b = AssignedZone.GetSpawnBounds();
                Gizmos.DrawWireCube(b.center, b.size);
            }
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, collectionDistance);
        }
    }
}
