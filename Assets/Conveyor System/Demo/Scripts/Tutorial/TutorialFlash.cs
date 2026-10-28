using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Flashes a light and an emissive renderer. Wired in the Inspector to ConveyorEvents.OnParcelEntered
    /// to show how events drive gameplay without code.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial/Flash")]
    public class TutorialFlash : MonoBehaviour
    {
        public Light Lamp;
        public Renderer Glow;
        public Color Color = new Color(1f, 0.75f, 0.2f);
        public float Duration = 0.35f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock block;
        private Coroutine flash;
        private float baseIntensity;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (Lamp != null) baseIntensity = Lamp.intensity;
            Set(0f);
        }

        /// <summary>
        /// Plays one flash. Signature matches UnityEvent&lt;Parcel&gt; so it can be wired in the Inspector.
        /// </summary>
        public void Flash(Parcel parcel)
        {
            if (!isActiveAndEnabled) return;
            if (flash != null) StopCoroutine(flash);
            flash = StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / Duration)
            {
                Set(1f - t);
                yield return null;
            }
            Set(0f);
            flash = null;
        }

        private void Set(float strength)
        {
            if (Lamp != null) Lamp.intensity = baseIntensity * strength;
            if (Glow == null) return;
            Glow.GetPropertyBlock(block);
            block.SetColor(EmissionColorId, Color * (0.2f + 4f * strength));
            Glow.SetPropertyBlock(block);
        }
    }
}
