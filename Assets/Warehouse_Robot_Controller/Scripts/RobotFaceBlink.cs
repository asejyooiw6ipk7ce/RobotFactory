using System.Collections;
using UnityEngine;

namespace Warcool.WarehouseRobot
{
    public class RobotEyesController : MonoBehaviour
    {
        [Header("Eye Screen Renderer")]
        [SerializeField] private Renderer eyeRenderer;

        [Header("Textures")]
        [SerializeField] private Texture2D eyesOpen;
        [SerializeField] private Texture2D eyesBlink;

        [Header("Blinking")]
        [SerializeField] private float minBlinkDelay = 2f;
        [SerializeField] private float maxBlinkDelay = 5f;
        [SerializeField] private float blinkDuration = 0.12f;

        [Header("Emission")]
        [ColorUsage(true, true)]
        [SerializeField] private Color emissionColor = Color.cyan;

        private Material mat;
        private Coroutine blinkRoutine;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (eyeRenderer == null)
                eyeRenderer = GetComponent<Renderer>();

            // Create a material instance so the shared project asset is not modified.
            mat = eyeRenderer.material;

            mat.EnableKeyword("_EMISSION");

            SetEyes(eyesOpen);
        }

        private void OnEnable()
        {
            blinkRoutine = StartCoroutine(BlinkLoop());
        }

        private void OnDisable()
        {
            if (blinkRoutine != null)
                StopCoroutine(blinkRoutine);
        }

        private IEnumerator BlinkLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(minBlinkDelay, maxBlinkDelay));

                SetEyes(eyesBlink);
                yield return new WaitForSeconds(blinkDuration);

                SetEyes(eyesOpen);
            }
        }

        private void SetEyes(Texture2D texture)
        {
            if (mat == null || texture == null)
                return;

            // URP Lit Base Map
            if (mat.HasProperty(BaseMap))
                mat.SetTexture(BaseMap, texture);

            // Legacy and unlit shaders
            if (mat.HasProperty(MainTex))
                mat.SetTexture(MainTex, texture);

            // Emission Map
            if (mat.HasProperty(EmissionMap))
                mat.SetTexture(EmissionMap, texture);

            if (mat.HasProperty(EmissionColor))
                mat.SetColor(EmissionColor, emissionColor);
        }
    }
}
