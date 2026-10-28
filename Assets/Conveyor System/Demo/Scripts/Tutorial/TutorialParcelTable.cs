using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Keeps a table stocked with parcels the viewer can pick up. When a parcel is delivered
    /// (ParcelExited of the end conveyor) it is removed and a fresh one appears on the table.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial/Parcel Table")]
    public class TutorialParcelTable : MonoBehaviour
    {
        public Transform[] Spots;
        public Parcel[] ParcelPrefabs;

        [Tooltip("Conveyor at the end of the practice belt.")]
        public ConveyorBase EndOfBelt;

        private readonly Dictionary<Transform, Parcel> stock = new Dictionary<Transform, Parcel>();

        private void Start()
        {
            for (int i = 0; i < Spots.Length; i++) Spawn(Spots[i], i);
        }

        private void OnEnable()
        {
            if (EndOfBelt != null) EndOfBelt.ParcelExited += OnDelivered;
        }

        private void OnDisable()
        {
            if (EndOfBelt != null) EndOfBelt.ParcelExited -= OnDelivered;
        }

        private void OnDelivered(ConveyorBase conveyor, Parcel parcel)
        {
            if (parcel != null) Destroy(parcel.gameObject);

            // Restock the first spot whose parcel has been taken away
            for (int i = 0; i < Spots.Length; i++)
            {
                stock.TryGetValue(Spots[i], out var p);
                if (p == null || Vector3.Distance(p.transform.position, Spots[i].position) > 0.4f)
                {
                    Spawn(Spots[i], i);
                    return;
                }
            }
        }

        private void Spawn(Transform spot, int i)
        {
            if (ParcelPrefabs == null || ParcelPrefabs.Length == 0) return;
            var prefab = ParcelPrefabs[i % ParcelPrefabs.Length];
            var parcel = Instantiate(prefab, spot.position, spot.rotation);
            parcel.name = prefab.name;
            stock[spot] = parcel;
        }
    }
}
