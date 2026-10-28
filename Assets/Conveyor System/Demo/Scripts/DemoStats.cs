using TMPro;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Live counters for the demo HUD, driven entirely by the conveyor events (no polling).
    /// Also lets the viewer start/stop the quality-check Pauser with the Space key.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Demo Stats")]
    public class DemoStats : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("Conveyor at the end of the line (e.g. the Loader); every parcel leaving it counts as delivered.")]
        public ConveyorBase DeliveryPoint;

        [Tooltip("SmartSplitter whose routing decisions are counted.")]
        public SmartSplitterConveyor Sorter;

        [Tooltip("Sorter output index that leads to the express lane.")]
        public int ExpressOutput = 0;

        [Tooltip("Pauser toggled with the Space key.")]
        public PauserConveyor QualityCheck;

        [Header("Texts")]
        public TMP_Text DeliveredText;
        public TMP_Text ExpressText;
        public TMP_Text StandardText;
        public TMP_Text StatusText;

        private int delivered;
        private int express;
        private int standard;

        private void OnEnable()
        {
            if (DeliveryPoint != null) DeliveryPoint.ParcelExited += OnDelivered;
            if (Sorter != null) Sorter.ParcelRouted += OnRouted;
            if (QualityCheck != null) QualityCheck.ActiveChanged += OnActiveChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (DeliveryPoint != null) DeliveryPoint.ParcelExited -= OnDelivered;
            if (Sorter != null) Sorter.ParcelRouted -= OnRouted;
            if (QualityCheck != null) QualityCheck.ActiveChanged -= OnActiveChanged;
        }

        private void Update()
        {
            if (QualityCheck != null && DemoInput.TogglePressed) QualityCheck.ToggleConveyor();
        }

        private void OnDelivered(ConveyorBase conveyor, Parcel parcel)
        {
            delivered++;
            Refresh();
        }

        private void OnRouted(ConveyorBase conveyor, Parcel parcel, int outputIndex)
        {
            if (outputIndex == ExpressOutput) express++;
            else standard++;
            Refresh();
        }

        private void OnActiveChanged(PauserConveyor pauser, bool active) => Refresh();

        /// <summary>
        /// Writes the current values into the HUD texts.
        /// </summary>
        private void Refresh()
        {
            if (DeliveredText != null) DeliveredText.text = delivered.ToString();
            if (ExpressText != null) ExpressText.text = express.ToString();
            if (StandardText != null) StandardText.text = standard.ToString();

            if (StatusText != null && QualityCheck != null)
            {
                StatusText.text = QualityCheck.IsActive
                    ? "<color=#7CFFB2>RUNNING</color>"
                    : "<color=#FF6B6B>STOPPED</color>";
            }
        }
    }
}
