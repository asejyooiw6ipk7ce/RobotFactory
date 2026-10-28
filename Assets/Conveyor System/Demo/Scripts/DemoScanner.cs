using System.Collections;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Quality-check scanner effect for a Pauser: the light sweeps while a parcel is being inspected,
    /// turns green when it passes, and red while the station is stopped. Driven by the conveyor events.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Demo Scanner")]
    public class DemoScanner : MonoBehaviour
    {
        [Tooltip("The Pauser this scanner belongs to.")]
        public PauserConveyor Station;

        [Tooltip("Light that shows the scanner state.")]
        public Light ScanLight;

        [Tooltip("Renderers whose emission shows the scanner state.")]
        public Renderer[] Glow;

        public Color IdleColor = new Color(0.25f, 0.6f, 1f);
        public Color ScanColor = new Color(0.2f, 0.9f, 1f);
        public Color PassColor = new Color(0.3f, 1f, 0.45f);
        public Color StoppedColor = new Color(1f, 0.2f, 0.15f);

        [Tooltip("Emission intensity multiplier.")]
        public float GlowIntensity = 3f;

        private MaterialPropertyBlock block;
        private Coroutine effect;
        private float baseLightIntensity;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (ScanLight != null) baseLightIntensity = ScanLight.intensity;
        }

        private void OnEnable()
        {
            if (Station == null) return;
            Station.ParcelEntered += OnEntered;
            Station.ParcelExited += OnExited;
            Station.ActiveChanged += OnActiveChanged;
            SetColor(Station.IsActive ? IdleColor : StoppedColor, 1f);
        }

        private void OnDisable()
        {
            if (Station == null) return;
            Station.ParcelEntered -= OnEntered;
            Station.ParcelExited -= OnExited;
            Station.ActiveChanged -= OnActiveChanged;
        }

        private void OnEntered(ConveyorBase conveyor, Parcel parcel) => Play(Scan());
        private void OnExited(ConveyorBase conveyor, Parcel parcel) => Play(Pass());

        private void OnActiveChanged(PauserConveyor pauser, bool active)
        {
            if (effect != null) StopCoroutine(effect);
            effect = null;
            SetColor(active ? (pauser.CurrentParcel != null ? ScanColor : IdleColor) : StoppedColor, 1f);
            if (active && pauser.CurrentParcel != null) Play(Scan());
        }

        private void Play(IEnumerator routine)
        {
            if (!isActiveAndEnabled) return;
            if (effect != null) StopCoroutine(effect);
            effect = StartCoroutine(routine);
        }

        /// <summary>
        /// Pulses while the parcel is on the station.
        /// </summary>
        private IEnumerator Scan()
        {
            float t = 0f;
            while (Station.CurrentParcel != null)
            {
                if (Station.IsActive)
                {
                    t += Time.deltaTime;
                    SetColor(ScanColor, 0.6f + 0.6f * Mathf.Abs(Mathf.Sin(t * 6f)));
                }
                yield return null;
            }
            effect = null;
        }

        /// <summary>
        /// Short green flash, then back to idle.
        /// </summary>
        private IEnumerator Pass()
        {
            SetColor(PassColor, 1.6f);
            yield return new WaitForSeconds(0.35f);
            SetColor(Station.IsActive ? IdleColor : StoppedColor, 1f);
            effect = null;
        }

        private void SetColor(Color color, float strength)
        {
            if (ScanLight != null)
            {
                ScanLight.color = color;
                ScanLight.intensity = baseLightIntensity * strength;
            }

            if (Glow == null) return;
            foreach (var r in Glow)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, color * (GlowIntensity * strength));
                r.SetPropertyBlock(block);
            }
        }
    }
}
