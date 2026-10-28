using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Manages collision detection for conveyor trigger zones.
    /// This component detects when conveyor segments connect and registers/unregisters them dynamically.
    /// </summary>
    public class ConveyorTrigger : MonoBehaviour
    {
        /// <summary>
        /// Indicates whether this trigger is an output point of a conveyor segment.
        /// Input triggers do not register connections to other conveyors.
        /// </summary>
        public bool IsOutput = false;
        /// <summary>
        /// The index of the output connection when this trigger has multiple possible outputs.
        /// Used in splitters and smart splitters to distinguish between different output paths.
        /// </summary>
        public int OutputIndex = 0;
        /// <summary>
        /// Reference to the parent ConveyorBase component that owns this trigger.
        /// </summary>
        private ConveyorBase parentConveyor;
        /// <summary>
        /// Reference to the collider component used for trigger detection.
        /// </summary>
        private Collider _collider;

        /// <summary>
        /// Called when the component is initialized.
        /// Caches references to the parent conveyor and the collider component for later use in trigger detection.
        /// </summary>
        private void Awake()
        {
            parentConveyor = GetComponentInParent<ConveyorBase>();
            _collider = GetComponent<Collider>();
        }

        /// <summary>
        /// Called when another collider enters this trigger zone.
        /// If this trigger is an output and the other trigger is an input,
        /// registers the connection between the parent conveyor and the other conveyor in the ConveyorBase logic.
        /// </summary>
        /// <param name="other"></param>
        private void OnTriggerEnter(Collider other)
        {
            var otherTrigger = other.GetComponentInParent<ConveyorTrigger>();
            if (otherTrigger == null)
                return;

            if (IsOutput && !otherTrigger.IsOutput)
            {
                var otherConv = otherTrigger.GetComponentInParent<ConveyorBase>();
                parentConveyor?.RegisterNextConveyor(OutputIndex, otherConv);
            }
        }

        /// <summary>
        /// Called when another collider exits this trigger zone.
        /// If this trigger is an output and the other trigger is an input,
        /// unregisters the connection between the parent conveyor and the other conveyor in the ConveyorBase logic.
        /// </summary>
        /// <param name="other"></param>
        private void OnTriggerExit(Collider other)
        {
            var otherTrigger = other.GetComponentInParent<ConveyorTrigger>();
            if (otherTrigger == null) return;

            if (IsOutput && !otherTrigger.IsOutput)
            {
                var otherConv = otherTrigger.GetComponentInParent<ConveyorBase>();
                parentConveyor?.UnregisterNextConveyor(OutputIndex, otherConv);
            }
        }
    }
}