using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Goal gate trigger - detects rice balls entering and awards currency
    /// Attach to a trigger collider at the bottom goal area
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GoalGate : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Tag to identify rice balls")]
        public string RiceBallTag = "RiceBall";
        
        [Header("Visual Feedback")]
        public Color GateColor = Color.green;
        
        private int ballsScored = 0;
        
        private void Start()
        {
            // Ensure this is a trigger
            Collider col = GetComponent<Collider>();
            if (!col.isTrigger)
            {
                col.isTrigger = true;
                Debug.LogWarning("[GoalGate] Collider was not set as trigger, fixed automatically");
            }
            
            Debug.Log("[GoalGate] Goal gate ready!");
        }
        
        private void OnTriggerEnter(Collider other)
        {
            // Check if it's a rice ball
            if (other.CompareTag(RiceBallTag))
            {
                ballsScored++;
                
                // Award currency (1 cent per ball)
                if (PlayerDataManager.Instance != null)
                {
                    PlayerDataManager.Instance.AddCurrency(1, "Goal scored");
                }
                else
                {
                    Debug.LogWarning("[GoalGate] PlayerDataManager not found!");
                }
                
                // Destroy the ball
                Destroy(other.gameObject);
            }
        }
        
        private void OnDrawGizmos()
        {
            // Visualize goal gate
            Gizmos.color = GateColor;
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                Gizmos.DrawWireCube(transform.position, col.bounds.size);
            }
        }
    }
}
