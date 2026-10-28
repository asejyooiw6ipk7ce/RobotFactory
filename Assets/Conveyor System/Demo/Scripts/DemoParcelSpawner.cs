using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Fills a ParcelStorage with a random mix of parcel prefabs when the scene starts.
    /// Together with a Loader and an Unloader that share the storage, this keeps the demo line running forever.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Demo Parcel Spawner")]
    public class DemoParcelSpawner : MonoBehaviour
    {
        [Tooltip("Storage that receives the spawned parcels.")]
        public ParcelStorage Storage;

        [Tooltip("Parcel prefabs to pick from at random.")]
        public Parcel[] ParcelPrefabs;

        [Tooltip("How many parcels to spawn.")]
        [Min(1)]
        public int Count = 28;

        [Tooltip("Random seed (0 = different every run).")]
        public int Seed = 0;

        private void Start()
        {
            if (Storage == null || ParcelPrefabs == null || ParcelPrefabs.Length == 0) return;

            var random = Seed == 0 ? new System.Random() : new System.Random(Seed);
            for (int i = 0; i < Count; i++)
            {
                var prefab = ParcelPrefabs[random.Next(ParcelPrefabs.Length)];
                if (prefab == null) continue;

                var parcel = Instantiate(prefab, Storage.transform.position, Quaternion.identity);
                parcel.name = $"{prefab.name} {i + 1:00}";
                if (!Storage.StoreParcel(parcel))
                {
                    Destroy(parcel.gameObject);
                    break;
                }
            }
        }
    }
}
