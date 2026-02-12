using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Controls the dropper that moves back and forth at the top
    /// Drops rice balls when player presses Space
    /// </summary>
    public class DropperController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("How fast the dropper moves horizontally")]
        public float MoveSpeed = 2f;
        
        [Tooltip("How far left/right the dropper moves from center")]
        public float MoveRange = 5f;
        
        [Header("Dropping")]
        [Tooltip("The rice ball prefab to drop (needs Rigidbody)")]
        public GameObject RiceBallPrefab;
        
        [Tooltip("Where balls spawn from")]
        public Transform DropPoint;
        
        [Tooltip("Force applied to dropped balls")]
        public float DropForce = 0f; // 0 = just gravity
        
        [Header("Settings")]
        [Tooltip("Drop all rice at once when space pressed")]
        public bool DropAllAtOnce = true;
        
        [Tooltip("Spacing between balls when dropping all (seconds)")]
        public float DropInterval = 0.1f; // Increased from 0.05 to reduce physics load
        
        private float moveDirection = 1f;
        private bool isDropping = false;
        private bool hasDropped = false;
        
        private void Start()
        {
            if (DropPoint == null)
            {
                DropPoint = transform;
                Debug.LogWarning("[DropperController] No DropPoint assigned, using this transform");
            }
            
            if (RiceBallPrefab == null)
            {
                Debug.LogError("[DropperController] ❌ No RiceBallPrefab assigned! Assign a prefab with Rigidbody");
            }
            else
            {
                // Validate prefab setup
                var riceBallComponent = RiceBallPrefab.GetComponent<RiceBall>();
                if (riceBallComponent == null)
                {
                    Debug.LogError($"[DropperController] ❌ RiceBall prefab '{RiceBallPrefab.name}' is MISSING RiceBall component! Add it to the prefab.");
                }
                
                var rb = RiceBallPrefab.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    Debug.LogWarning($"[DropperController] RiceBall prefab '{RiceBallPrefab.name}' missing Rigidbody (will be added at runtime)");
                }
                
                var col = RiceBallPrefab.GetComponent<Collider>();
                if (col == null)
                {
                    Debug.LogError($"[DropperController] ❌ RiceBall prefab '{RiceBallPrefab.name}' is MISSING Collider! Add a SphereCollider.");
                }
            }
            
            Debug.Log("[DropperController] Ready! Press SPACE to drop rice balls");
        }
        
        private void Update()
        {
            // Move dropper back and forth (only if not dropping)
            if (!isDropping)
            {
                MoveDropper();
            }
            
            // Check for drop input
            if (Input.GetKeyDown(KeyCode.Space) && !hasDropped)
            {
                if (DropAllAtOnce)
                {
                    StartCoroutine(DropAllBalls());
                }
                else
                {
                    TryDropBall();
                }
            }
        }
        
        private void MoveDropper()
        {
            // Move horizontally
            float newX = transform.position.x + (moveDirection * MoveSpeed * Time.deltaTime);
            
            // Clamp to range and reverse direction if needed
            if (newX > MoveRange)
            {
                newX = MoveRange;
                moveDirection = -1f;
            }
            else if (newX < -MoveRange)
            {
                newX = -MoveRange;
                moveDirection = 1f;
            }
            
            transform.position = new Vector3(newX, transform.position.y, transform.position.z);
        }
        
        private System.Collections.IEnumerator DropAllBalls()
        {
            isDropping = true;
            hasDropped = true;
            
            Debug.Log("[DropperController] Dropping all balls!");
            
            int ballsToDrop = DropPuzzleManager.Instance.RiceBallsAvailable;
            
            for (int i = 0; i < ballsToDrop; i++)
            {
                if (!DropPuzzleManager.Instance.TryDropBall())
                {
                    break;
                }
                
                // Spawn ball directly below drop point (no random offset to avoid collisions)
                Vector3 spawnPos = DropPoint.position + Vector3.down * 0.3f; // Spawn slightly below to avoid dropper collision
                
                GameObject ball = Instantiate(RiceBallPrefab, spawnPos, Quaternion.identity);
                
                // Configure Rigidbody for straight drop
                Rigidbody rb = ball.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero; // Start with no velocity
                    rb.angularVelocity = Vector3.zero; // No spin
                    rb.useGravity = true; // Ensure gravity is on
                    
                    // Apply drop force if configured
                    if (DropForce > 0)
                    {
                        rb.AddForce(Vector3.down * DropForce, ForceMode.Impulse);
                    }
                }
                
                yield return new WaitForSeconds(DropInterval);
            }
            
            Debug.Log($"[DropperController] Finished dropping {ballsToDrop} balls!");
        }
        
        private void TryDropBall()
        {
            // Check if we have balls available
            if (!DropPuzzleManager.Instance.TryDropBall())
            {
                return;
            }
            
            // Spawn ball directly below drop point (no offset to avoid dropper collision)
            Vector3 spawnPos = DropPoint.position + Vector3.down * 0.3f;
            GameObject ball = Instantiate(RiceBallPrefab, spawnPos, Quaternion.identity);
            
            // Configure Rigidbody for straight drop
            Rigidbody rb = ball.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero; // Start with no velocity
                rb.angularVelocity = Vector3.zero; // No spin
                rb.useGravity = true; // Ensure gravity is on
                
                // Apply drop force if configured
                if (DropForce > 0)
                {
                    rb.AddForce(Vector3.down * DropForce, ForceMode.Impulse);
                }
            }
        }
        
        private void OnDrawGizmos()
        {
            // Visualize movement range
            Gizmos.color = Color.cyan;
            Vector3 leftPos = new Vector3(-MoveRange, transform.position.y, transform.position.z);
            Vector3 rightPos = new Vector3(MoveRange, transform.position.y, transform.position.z);
            Gizmos.DrawLine(leftPos, rightPos);
            Gizmos.DrawWireSphere(leftPos, 0.2f);
            Gizmos.DrawWireSphere(rightPos, 0.2f);
        }
    }
}
