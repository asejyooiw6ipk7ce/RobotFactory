using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ConveyorSystem
{
    /// <summary>
    /// Manages storage of inactive parcels in a conveyor system.
    /// Provides pooling functionality to efficiently store and retrieve parcel objects.
    /// </summary>
    [DisallowMultipleComponent]
    public class ParcelStorage : MonoBehaviour
    {
        /// <summary>
        /// Root transform under which deactivated parcels will be stored.
        /// </summary>
        [Tooltip("Root transform under which deactivated parcels will be stored")]
        public Transform StorageRoot;

        /// <summary>
        /// Maximum number of parcels in storage (0 = unlimited).
        /// </summary>
        [Tooltip("Maximum number of parcels in storage (0 = unlimited)")]
        public int Capacity = 0;

        /// <summary>
        /// Logs every store/retrieve operation to the console.
        /// </summary>
        [Tooltip("Log every store/retrieve operation to the console")]
        public bool LogEvents = false;

        /// <summary>
        /// Invoked after a parcel has been stored (Inspector-friendly counterpart of ParcelStored).
        /// </summary>
        [Header("Events")]
        public UnityEvent<Parcel> OnParcelStored = new UnityEvent<Parcel>();

        /// <summary>
        /// Invoked after a parcel has been retrieved (Inspector-friendly counterpart of ParcelRetrieved).
        /// </summary>
        public UnityEvent<Parcel> OnParcelRetrieved = new UnityEvent<Parcel>();

        /// <summary>
        /// Raised after a parcel has been stored.
        /// </summary>
        public event Action<ParcelStorage, Parcel> ParcelStored;

        /// <summary>
        /// Raised after a parcel has been retrieved.
        /// </summary>
        public event Action<ParcelStorage, Parcel> ParcelRetrieved;

        /// <summary>
        /// Raised whenever Count or IsFull may have changed. Loaders and Unloaders listen to this instead of polling.
        /// </summary>
        public event Action<ParcelStorage> Changed;

        /// <summary>
        /// Internal list maintaining references to all stored parcels.
        /// </summary>
        private readonly List<Parcel> stored = new List<Parcel>();

        /// <summary>
        /// Gets the current number of stored parcels.
        /// </summary>
        public int Count => stored.Count;

        /// <summary>
        /// Checks if the storage is full based on the defined capacity.
        /// </summary>
        public bool IsFull => Capacity > 0 && stored.Count >= Capacity;

        /// <summary>
        /// Called when the component is added or reset in the Unity Editor.
        /// Initializes StorageRoot to this transform if not already assigned.
        /// </summary>
        private void Reset()
        {
            // Set default storage root to this transform if not assigned
            if (StorageRoot == null)
                StorageRoot = this.transform;
        }

        /// <summary>
        /// Picks up parcels that were placed under StorageRoot in the scene, so they can be unloaded.
        /// </summary>
        private void Awake()
        {
            if (StorageRoot == null) StorageRoot = transform;

            foreach (var parcel in StorageRoot.GetComponentsInChildren<Parcel>(true))
            {
                if (stored.Contains(parcel)) continue;
                parcel.gameObject.SetActive(false);
                stored.Add(parcel);
            }

            // Unloaders may have subscribed before this Awake ran
            if (stored.Count > 0) NotifyChanged();
        }

        /// <summary>
        /// Applies Inspector changes to Capacity made during play mode.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying) NotifyChanged();
        }

        /// <summary>
        /// Raises Changed. Call it after changing Capacity from code so waiting Loaders re-check.
        /// </summary>
        public void NotifyChanged()
        {
            Changed?.Invoke(this);
        }

        /// <summary>
        /// Stores a parcel in the storage system.
        /// Deactivates the parcel and parents it under StorageRoot.
        /// </summary>
        /// <param name="parcel">The parcel to be stored.</param>
        /// <returns>True if the parcel was successfully stored, false if storage is full or parcel is null.</returns>
        public bool StoreParcel(Parcel parcel)
        {
            if (parcel == null) return false;
            if (IsFull) return false;

            // Notify custom scripts on the parcel (optional OnStored method) while it is still active
            parcel.SendMessage("OnStored", SendMessageOptions.DontRequireReceiver);

            parcel.transform.SetParent(StorageRoot, worldPositionStays: false);
            parcel.gameObject.SetActive(false);

            stored.Add(parcel);

            if (LogEvents) Debug.Log($"ParcelStorage [{gameObject.name}]: stored parcel ({stored.Count} total)", this);

            ParcelStored?.Invoke(this, parcel);
            OnParcelStored.Invoke(parcel);
            NotifyChanged();
            return true;
        }

        /// <summary>
        /// Retrieves a parcel from storage.
        /// Activates the parcel and unparents it from StorageRoot.
        /// </summary>
        /// <returns>The retrieved parcel, or null if storage is empty.</returns>
        public Parcel RetrieveParcel()
        {
            if (stored.Count == 0) return null;

            Parcel p = stored[0];
            stored.RemoveAt(0);

            p.transform.SetParent(null, worldPositionStays: true);
            p.gameObject.SetActive(true);

            if (LogEvents) Debug.Log($"ParcelStorage [{gameObject.name}]: retrieved parcel ({stored.Count} remaining)", this);

            ParcelRetrieved?.Invoke(this, p);
            OnParcelRetrieved.Invoke(p);
            NotifyChanged();
            return p;
        }

        /// <summary>
        /// Provides a read-only list of currently stored parcels for external reference.
        /// </summary>
        public IReadOnlyList<Parcel> StoredItems => stored.AsReadOnly();
    }
}