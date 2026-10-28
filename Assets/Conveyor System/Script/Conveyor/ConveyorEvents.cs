using UnityEngine;
using UnityEngine.Events;

namespace ConveyorSystem
{
    /// <summary>
    /// Exposes a conveyor's C# events as UnityEvents, so gameplay reactions can be wired in the Inspector.
    /// Add it next to any ConveyorBase-derived component. Code can subscribe to the C# events directly instead.
    /// </summary>
    [RequireComponent(typeof(ConveyorBase))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Conveyor System/Conveyor Events")]
    public class ConveyorEvents : MonoBehaviour
    {
        /// <summary>
        /// Invoked when a parcel is accepted onto the conveyor.
        /// </summary>
        [Tooltip("Invoked when a parcel is accepted onto the conveyor.")]
        public UnityEvent<Parcel> OnParcelEntered = new UnityEvent<Parcel>();

        /// <summary>
        /// Invoked when a parcel leaves the conveyor (handed over, stored, consumed or picked up).
        /// </summary>
        [Tooltip("Invoked when a parcel leaves the conveyor (handed over, stored, consumed or picked up).")]
        public UnityEvent<Parcel> OnParcelExited = new UnityEvent<Parcel>();

        /// <summary>
        /// Invoked when a parcel starts waiting because nothing downstream can take it.
        /// </summary>
        [Tooltip("Invoked when a parcel starts waiting because nothing downstream can take it.")]
        public UnityEvent<Parcel> OnParcelBlocked = new UnityEvent<Parcel>();

        /// <summary>
        /// Invoked when a parcel is assigned an exit. The int is the output index.
        /// </summary>
        [Tooltip("Invoked when a parcel is assigned an exit. The int is the output index.")]
        public UnityEvent<Parcel, int> OnParcelRouted = new UnityEvent<Parcel, int>();

        /// <summary>
        /// Invoked when a Pauser is started (true) or stopped (false). Unused on other types.
        /// </summary>
        [Tooltip("Pauser only: invoked when the conveyor is started (true) or stopped (false).")]
        public UnityEvent<bool> OnActiveChanged = new UnityEvent<bool>();

        /// <summary>
        /// The conveyor whose events are forwarded.
        /// </summary>
        private ConveyorBase conveyor;

        /// <summary>
        /// Caches the conveyor component.
        /// </summary>
        private void Awake()
        {
            conveyor = GetComponent<ConveyorBase>();
        }

        /// <summary>
        /// Subscribes to the conveyor's events.
        /// </summary>
        private void OnEnable()
        {
            if (conveyor == null) return;
            conveyor.ParcelEntered += HandleEntered;
            conveyor.ParcelExited += HandleExited;
            conveyor.ParcelBlocked += HandleBlocked;
            conveyor.ParcelRouted += HandleRouted;
            if (conveyor is PauserConveyor pauser) pauser.ActiveChanged += HandleActiveChanged;
        }

        /// <summary>
        /// Unsubscribes from the conveyor's events.
        /// </summary>
        private void OnDisable()
        {
            if (conveyor == null) return;
            conveyor.ParcelEntered -= HandleEntered;
            conveyor.ParcelExited -= HandleExited;
            conveyor.ParcelBlocked -= HandleBlocked;
            conveyor.ParcelRouted -= HandleRouted;
            if (conveyor is PauserConveyor pauser) pauser.ActiveChanged -= HandleActiveChanged;
        }

        private void HandleEntered(ConveyorBase c, Parcel p) => OnParcelEntered.Invoke(p);
        private void HandleExited(ConveyorBase c, Parcel p) => OnParcelExited.Invoke(p);
        private void HandleBlocked(ConveyorBase c, Parcel p) => OnParcelBlocked.Invoke(p);
        private void HandleRouted(ConveyorBase c, Parcel p, int outputIndex) => OnParcelRouted.Invoke(p, outputIndex);
        private void HandleActiveChanged(PauserConveyor c, bool active) => OnActiveChanged.Invoke(active);
    }
}
