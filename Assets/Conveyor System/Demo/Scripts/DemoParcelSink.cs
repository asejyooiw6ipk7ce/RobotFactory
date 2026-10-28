using System;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Turns a Loader into a parcel sink: every parcel that leaves it is counted and destroyed.
    /// </summary>
    [RequireComponent(typeof(LoaderConveyor))]
    [AddComponentMenu("Conveyor System/Demo/Demo Parcel Sink")]
    public class DemoParcelSink : MonoBehaviour
    {
        /// <summary>
        /// Raised for every delivered parcel, just before it is destroyed.
        /// </summary>
        public event Action<DemoParcelSink, Parcel> Delivered;

        /// <summary>
        /// Number of parcels delivered to this sink.
        /// </summary>
        public int Count { get; private set; }

        private LoaderConveyor loader;

        private void Awake()
        {
            loader = GetComponent<LoaderConveyor>();
        }

        private void OnEnable()
        {
            loader.ParcelExited += OnExited;
        }

        private void OnDisable()
        {
            loader.ParcelExited -= OnExited;
        }

        private void OnExited(ConveyorBase conveyor, Parcel parcel)
        {
            if (parcel == null) return;
            Count++;
            Delivered?.Invoke(this, parcel);
            Destroy(parcel.gameObject);
        }
    }
}
