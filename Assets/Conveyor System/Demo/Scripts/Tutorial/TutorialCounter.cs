using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Live counter for a tutorial station, driven by events: counts parcels leaving a conveyor,
    /// or shows how many parcels a storage holds.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial/Counter")]
    public class TutorialCounter : MonoBehaviour
    {
        [Tooltip("Counts every ParcelExited of this conveyor.")]
        public ConveyorBase Conveyor;

        [Tooltip("Shows Count of this storage (updated on its Changed event).")]
        public ParcelStorage Storage;

        public TMP_Text Text;

        [Tooltip("{0} is replaced by the value.")]
        public string Format = "{0}";

        private int count;

        private void OnEnable()
        {
            if (Conveyor != null) Conveyor.ParcelExited += OnExited;
            if (Storage != null) Storage.Changed += OnChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (Conveyor != null) Conveyor.ParcelExited -= OnExited;
            if (Storage != null) Storage.Changed -= OnChanged;
        }

        private void OnExited(ConveyorBase c, Parcel p) { count++; Refresh(); }
        private void OnChanged(ParcelStorage s) => Refresh();

        private void Refresh()
        {
            if (Text == null) return;
            int value = Storage != null ? Storage.Count : count;
            Text.text = string.Format(Format, value);
        }
    }
}
