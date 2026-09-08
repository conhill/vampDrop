using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Multiplier gate — spawns extra balls when a RiceBall passes through.
    ///
    /// FIXES APPLIED:
    ///   1. Removed OnTriggerStay — it was a "backup" that fired every physics frame
    ///      a ball remained inside the trigger, causing duplicate multiplications.
    ///      OnTriggerEnter fires exactly once per entry, which is correct.
    ///   2. Removed the unbounded HashSet<int> processedBalls. It grew forever
    ///      (never cleared) and was redundant — RiceBall.HasHitGate() already
    ///      tracks this per-ball. Deduplication is now solely the ball's responsibility.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MultiplierGate : MonoBehaviour
    {
        [Header("Multiplier Settings")]
        [Tooltip("Multiplier value (2 = x2, 4 = x4, etc.)")]
        public int Multiplier = 2;

        [Tooltip("Rice ball prefab to pull from pool")]
        public GameObject RiceBallPrefab;

        [Header("Visual Settings")]
        public Color GateColor = Color.yellow;

        [Header("Optional Text Display")]
        public TMPro.TextMeshPro MultiplierText;

        // ── Guarantee floor ───────────────────────────────────────────────────

        /// <summary>
        /// The multiplier this gate is PROMISED to pay, regardless of what the pre-drop slot
        /// machine rolls. PuzzleEnhancer stamps it on the gate it places to satisfy the
        /// tutorial / first-run "you will see a x2" promise.
        ///
        /// Runtime only — nothing serialises it, because the guarantee is a property of a
        /// particular run, not of the prefab. Zero means "no promise, the roll decides".
        ///
        /// GateRollController treats this as a FLOOR: a winning spin may raise a gate above
        /// it, but a losing spin may not tear a guaranteed gate down. Without that the
        /// enhancer's guarantee and the slot machine's ~96% no-match roll fight over the same
        /// object and the roll wins, which is how the promised tutorial x2 went missing.
        /// </summary>
        [System.NonSerialized] public int GuaranteedMultiplier;

        /// <summary>True when this gate must survive a losing pre-drop roll.</summary>
        public bool IsGuaranteed => GuaranteedMultiplier > 0;

        /// <summary>
        /// Retarget this gate, keeping every display in step. Never lowers a guaranteed gate
        /// below its promised floor. Use this instead of assigning <see cref="Multiplier"/>
        /// directly — the field alone leaves the label and the tint showing the old value.
        /// </summary>
        public void SetMultiplier(int value)
        {
            Multiplier = Mathf.Max(value, GuaranteedMultiplier);

            if (MultiplierText != null)
                MultiplierText.text = $"x{Multiplier}";

            var visual = GetComponent<MultiplierGateVisual>();
            if (visual != null) visual.SetMultiplier(Multiplier);
        }

        /// <summary>
        /// Raised when a ball actually multiplies at this gate. MultiplierGateVisual
        /// subscribes to flash the gate. Fired from RiceBallGateInteractionSystem (ECS
        /// path) and from OnTriggerEnter (legacy pool path).
        /// </summary>
        public event System.Action Hit;

        /// <summary>Fire the Hit event (safe to call every hit).</summary>
        public void RaiseHit() => Hit?.Invoke();

        private void Start()
        {
            var col = GetComponent<Collider>();
            if (col != null)
            {
                if (!col.isTrigger)
                {
                    col.isTrigger = true;
                    // Debug.LogWarning($"[MultiplierGate x{Multiplier}] Collider was not a trigger — fixed.");
                }
            }
            else
            {
                // Debug.LogError($"[MultiplierGate x{Multiplier}] No Collider on {name}!");
            }

            if (RiceBallPrefab == null)
                // Debug.LogError($"[MultiplierGate x{Multiplier}] No RiceBallPrefab assigned on {name}!");

            if (MultiplierText != null)
                MultiplierText.text = $"x{Multiplier}";
        }

        private void OnTriggerEnter(Collider other)
        {
            // Only process RiceBall MonoBehaviour balls (pool path)
            // ECS balls are handled by RiceBallGateInteractionSystem
            if (!other.CompareTag("RiceBall")) return;

            var riceBall = other.GetComponent<RiceBall>();
            if (riceBall == null) return;

            int gateId = GetInstanceID();

            // RiceBall tracks which gates it has already hit — single source of truth
            if (riceBall.HasHitGate(gateId)) return;
            riceBall.MarkGateHit(gateId);

            if (RiceBallPrefab == null)
            {
                // Debug.LogError($"[MultiplierGate x{Multiplier}] Cannot spawn — no prefab assigned!");
                return;
            }

            if (DropperController.Instance == null)
            {
                // Debug.LogError($"[MultiplierGate x{Multiplier}] DropperController.Instance is null!");
                return;
            }

            float ballRadius = 0.25f;
            var ballCol = other.GetComponent<Collider>();
            if (ballCol != null) ballRadius = ballCol.bounds.extents.y;

            int extra = Multiplier - 1;
            for (int i = 0; i < extra; i++)
            {
                float spread = Random.Range(-ballRadius * 3f, ballRadius * 3f);
                Vector3 spawnPos = other.transform.position + new Vector3(
                    spread,
                    ballRadius * 2.2f * (i + 1),
                    0f
                );

                GameObject newBall = DropperController.Instance.GetBallFromPool(spawnPos);
                if (newBall == null) break; // Pool exhausted — stop spawning

                var rb = newBall.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity  = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.useGravity      = true;
                    // Keep ContinuousSpeculative set by pool pre-warm
                }

                var newRiceBall = newBall.GetComponent<RiceBall>();
                if (newRiceBall != null)
                    newRiceBall.MarkGateHit(gateId); // Prevent immediate re-trigger
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = GateColor;
            var col = GetComponent<Collider>();
            if (col != null)
                Gizmos.DrawWireCube(transform.position, col.bounds.size);
        }
    }
}
