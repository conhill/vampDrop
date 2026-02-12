using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Vampire.Player
{
    /// <summary>
    /// DISABLED: Movement is now handled by FPSController MonoBehaviour
    /// This system is kept for reference but will not run
    /// </summary>
    /*
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PlayerMovementSystem : ISystem
    {
        private bool hasLoggedOnce;

        public void OnUpdate(ref SystemState state)
        {
            if (!hasLoggedOnce)
            {
                UnityEngine.Debug.Log("[PlayerMovementSystem] System is running!");
                hasLoggedOnce = true;
            }

            var deltaTime = SystemAPI.Time.DeltaTime;
            
            // Get input (non-burst compatible, so we'll read it here)
            var horizontal = Input.GetAxis("Horizontal");
            var vertical = Input.GetAxis("Vertical");
            var moveDirection = new float3(horizontal, 0, vertical);

            if (math.lengthsq(moveDirection) > 0.01f && !hasLoggedOnce)
            {
                UnityEngine.Debug.Log($"[PlayerMovementSystem] Input detected: H={horizontal}, V={vertical}");
            }
            
            foreach (var (transform, player) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerData>>())
            {
                if (math.lengthsq(moveDirection) > 0.01f)
                {
                    moveDirection = math.normalize(moveDirection);
                    
                    // Move relative to player rotation
                    var rotation = transform.ValueRO.Rotation;
                    var forward = math.mul(rotation, new float3(0, 0, 1));
                    var right = math.mul(rotation, new float3(1, 0, 0));
                    
                    var movement = (right * moveDirection.x + forward * moveDirection.z) * player.ValueRO.MoveSpeed * deltaTime;
                    transform.ValueRW.Position += movement;
                    
                    UnityEngine.Debug.Log($"[PlayerMovementSystem] Moving to {transform.ValueRO.Position}");
                }
            }
        }
    }
    */
}
