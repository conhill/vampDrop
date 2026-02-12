using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// FPS RICE COLLECTOR - Pickup rice grains in FPS mode
    /// Attach to player, use raycasts/triggers to collect rice
    /// </summary>
    public class RiceCollectorFPS : MonoBehaviour
    {
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        
        [Header("Pickup Settings")]
        public float pickupRadius = 1.5f; // Overridden by upgrades
        public int maxSimultaneousPickups = 1; // Overridden by upgrades
        public LayerMask riceLayer;
        
        [Header("Audio (Optional)")]
        public AudioClip pickupSound;
        
        private int ricePickedThisFrame = 0;
        
        private void Start()
        {
            // Load pickup settings from upgrades
            if (playerData != null)
            {
                pickupRadius = playerData.FPSCollector.pickupRadius;
                maxSimultaneousPickups = playerData.FPSCollector.maxSimultaneousPickups;
                Debug.Log($"[RiceCollector] Loaded upgrades: Radius={pickupRadius} MaxPickups={maxSimultaneousPickups}");
            }
        }
        
        private void Update()
        {
            ricePickedThisFrame = 0;
            
            // Find nearby rice grains
            Collider[] nearbyRice = Physics.OverlapSphere(transform.position, pickupRadius, riceLayer);
            
            if (nearbyRice.Length > 0)
            {
                // Pick up to maxSimultaneousPickups rice per frame
                int pickupCount = Mathf.Min(nearbyRice.Length, maxSimultaneousPickups);
                
                for (int i = 0; i < pickupCount; i++)
                {
                    PickupRice(nearbyRice[i].gameObject);
                    ricePickedThisFrame++;
                }
            }
        }
        
        /// <summary>
        /// Pick up a single rice grain
        /// </summary>
        private void PickupRice(GameObject riceObject)
        {
            if (playerData != null)
            {
                playerData.AddRice(1);
            }
            
            // TODO: Play pickup animation/particle effect
            if (pickupSound != null)
            {
                // AudioSource.PlayClipAtPoint(pickupSound, transform.position);
            }
            
            // Destroy the rice object
            Destroy(riceObject);
        }
        
        /// <summary>
        /// Manual trigger-based pickup (alternative to sphere overlap)
        /// Attach trigger collider to player, tag rice objects as "Rice"
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Rice") && ricePickedThisFrame < maxSimultaneousPickups)
            {
                PickupRice(other.gameObject);
                ricePickedThisFrame++;
            }
        }
        
        private void OnDrawGizmos()
        {
            // Visualize pickup radius
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
        
        // For debug UI display
        private void OnGUI()
        {
            if (playerData == null) return;
            
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;
            
            // Top-left rice counter
            GUI.Label(new Rect(10, 10, 300, 30), $"Rice: {playerData.RiceGrains}", style);
            
            int craftable = playerData.RiceGrains / 5;
            if (craftable > 0)
            {
                style.normal.textColor = Color.yellow;
                GUI.Label(new Rect(10, 40, 300, 25), $"Can craft {craftable} riceballs!", style);
            }
        }
    }
}
