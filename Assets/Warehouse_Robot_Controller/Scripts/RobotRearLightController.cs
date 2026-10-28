using UnityEngine;

namespace Warcool.WarehouseRobot
{
    public class RobotRearLightController : MonoBehaviour
    {
        [Header("Rear Light Renderer")]
        [SerializeField] private Renderer rearLightRenderer;

        [Header("Textures")]
        [SerializeField] private Texture2D offTexture;
        [SerializeField] private Texture2D stopTextureRed;
        [SerializeField] private Texture2D reverseTextureWhite;

        [Header("Emission Colors")]
        [ColorUsage(true, true)]
        [SerializeField] private Color offEmission = Color.black;

        [ColorUsage(true, true)]
        [SerializeField] private Color stopEmission = Color.red * 3f;

        [ColorUsage(true, true)]
        [SerializeField] private Color reverseEmission = Color.white * 3f;

        private Material mat;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public enum RearLightState
        {
            Off,
            Stop,
            Reverse
        }

        private RearLightState currentState;

        private void Awake()
        {
            if (rearLightRenderer == null)
                rearLightRenderer = GetComponent<Renderer>();

            mat = rearLightRenderer.material;
            mat.EnableKeyword("_EMISSION");

            SetState(RearLightState.Off);
        }

        public void SetState(RearLightState state)
        {
            if (mat == null)
                return;

            if (currentState == state)
                return;

            currentState = state;

            switch (state)
            {
                case RearLightState.Off:
                    ApplyTexture(offTexture, offEmission);
                    break;

                case RearLightState.Stop:
                    ApplyTexture(stopTextureRed, stopEmission);
                    break;

                case RearLightState.Reverse:
                    ApplyTexture(reverseTextureWhite, reverseEmission);
                    break;
            }
        }

        private void ApplyTexture(Texture2D texture, Color emissionColor)
        {
            if (texture != null)
            {
                if (mat.HasProperty(BaseMap))
                    mat.SetTexture(BaseMap, texture);

                if (mat.HasProperty(MainTex))
                    mat.SetTexture(MainTex, texture);

                if (mat.HasProperty(EmissionMap))
                    mat.SetTexture(EmissionMap, texture);
            }

            if (mat.HasProperty(EmissionColor))
                mat.SetColor(EmissionColor, emissionColor);
        }
    }
}
