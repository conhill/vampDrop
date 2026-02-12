using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.UI
{
    public class RiceCounterUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI counterText;
        
        private EntityManager entityManager;
        private EntityQuery playerQuery;

        private void Start()
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            playerQuery = entityManager.CreateEntityQuery(typeof(Player.PlayerData));
        }

        private void Update()
        {
            if (playerQuery.IsEmpty)
                return;
            
            // Check if exactly one player exists (not multiple during scene transitions)
            var playerCount = playerQuery.CalculateEntityCount();
            if (playerCount != 1)
            {
                // 0 or multiple players - skip this frame
                return;
            }

            var playerEntity = playerQuery.GetSingletonEntity();
            var playerData = entityManager.GetComponentData<Player.PlayerData>(playerEntity);
            
            if (counterText != null)
            {
                counterText.text = $"Rice Collected: {playerData.RiceCollected}";
            }
        }

        private void OnDestroy()
        {
            playerQuery.Dispose();
        }
    }
}
