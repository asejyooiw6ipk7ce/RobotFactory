using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Turns an Unloader into an endless parcel source: it gets its own ParcelStorage,
    /// which is topped up with random parcels whenever one is taken out.
    /// </summary>
    [RequireComponent(typeof(UnloaderConveyor))]
    [AddComponentMenu("Conveyor System/Demo/Demo Parcel Source")]
    public class DemoParcelSource : MonoBehaviour
    {
        [Tooltip("Parcel prefabs to pick from at random.")]
        public Parcel[] ParcelPrefabs;

        [Tooltip("How many parcels are kept ready in the internal storage.")]
        [Min(1)]
        public int Buffer = 2;

        private ParcelStorage storage;

        private void Awake()
        {
            var go = new GameObject("Source Storage");
            go.transform.SetParent(transform, false);
            storage = go.AddComponent<ParcelStorage>();
            storage.StorageRoot = go.transform;
        }

        private void OnEnable()
        {
            storage.ParcelRetrieved += OnRetrieved;
        }

        private void OnDisable()
        {
            storage.ParcelRetrieved -= OnRetrieved;
        }

        private void Start()
        {
            Refill();
            GetComponent<UnloaderConveyor>().ParcelStorage = storage;
        }

        private void OnRetrieved(ParcelStorage s, Parcel parcel) => Refill();

        /// <summary>
        /// Creates parcels until the buffer is full.
        /// </summary>
        private void Refill()
        {
            if (ParcelPrefabs == null || ParcelPrefabs.Length == 0) return;

            while (storage.Count < Buffer)
            {
                var prefab = ParcelPrefabs[Random.Range(0, ParcelPrefabs.Length)];
                if (prefab == null) return;

                var parcel = Instantiate(prefab, transform.position, Quaternion.identity);
                parcel.name = prefab.name;
                if (!storage.StoreParcel(parcel))
                {
                    Destroy(parcel.gameObject);
                    return;
                }
            }
        }
    }
}
