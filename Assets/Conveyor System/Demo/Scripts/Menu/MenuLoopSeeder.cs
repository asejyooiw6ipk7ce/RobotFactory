using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Puts parcels on every other conveyor of a closed loop when the scene starts, so the menu backdrop keeps moving.
    /// A loop needs at least one free module, which is why only every other one gets a parcel.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Menu Loop Seeder")]
    public class MenuLoopSeeder : MonoBehaviour
    {
        public Parcel[] ParcelPrefabs;

        [Tooltip("Put a parcel on every Nth conveyor.")]
        [Min(2)]
        public int Every = 2;

        private void Start()
        {
            if (ParcelPrefabs == null || ParcelPrefabs.Length == 0) return;

            var conveyors = GetComponentsInChildren<ConveyorBase>();
            for (int i = 0; i < conveyors.Length; i += Every)
            {
                var c = conveyors[i];
                var prefab = ParcelPrefabs[(i / Every) % ParcelPrefabs.Length];
                var parcel = Instantiate(prefab, c.CenterPoint.position, c.transform.rotation);
                parcel.name = prefab.name;
                if (!parcel.PlaceOnConveyor(c)) Destroy(parcel.gameObject);
            }
        }
    }
}
