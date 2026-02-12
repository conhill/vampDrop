using Unity.Entities;
using Unity.Mathematics;

namespace Vampire.Rice
{
    public struct RiceSpawner : IComponentData
    {
        public Entity Prefab;
        public int Count;
        public uint Seed;
    }

    public struct RiceSpawned : IComponentData
    {
    }

    public struct RiceSpawnPoint : IComponentData
    {
        public float3 Center;
        public float3 Size;
        public int Count;
        public bool SpawnOnFloor;
        public float FloorY;
    }
}
