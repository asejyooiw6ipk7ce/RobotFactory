using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Manages visual highlighting of game objects by overriding material colors.
    /// Uses a MaterialPropertyBlock, so no material instances are created and shared materials stay untouched.
    /// Works with URP/HDRP (_BaseColor) and Built-in (_Color) shaders.
    /// </summary>
    public class Highlightable : MonoBehaviour
    {
        /// <summary>
        /// The color applied when the object is highlighted.
        /// </summary>
        [Tooltip("The color applied when the object is highlighted.")]
        public Color HighlightColor = Color.cyan;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// Array of all renderers in this object and its children.
        /// </summary>
        private Renderer[] rends;

        /// <summary>
        /// Reusable property block used to apply the highlight color.
        /// </summary>
        private MaterialPropertyBlock block;

        /// <summary>
        /// Called when the component is initialized.
        /// Caches all child renderers for later use in highlighting.
        /// </summary>
        private void Awake()
        {
            rends = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Sets the highlight state of the object.
        /// </summary>
        /// <param name="on">If true, applies the highlight color. If false, restores the original color.</param>
        public void SetHighlight(bool on)
        {
            if (rends == null) return;

            for (int i = 0; i < rends.Length; i++)
            {
                var rend = rends[i];
                if (rend == null) continue;

                if (!on)
                {
                    // Removing the override restores the material's own colors
                    rend.SetPropertyBlock(null);
                    continue;
                }

                var mat = rend.sharedMaterial;
                if (mat == null) continue;

                rend.GetPropertyBlock(block);
                if (mat.HasProperty(BaseColorId)) block.SetColor(BaseColorId, HighlightColor);
                if (mat.HasProperty(ColorId)) block.SetColor(ColorId, HighlightColor);
                rend.SetPropertyBlock(block);
            }
        }
    }
}