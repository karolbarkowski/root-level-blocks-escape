using UnityEngine;

namespace BlocksEscape
{
    // One cell of a gate. The gate mesh uses white materials (threshold, glass, arrow);
    // this tints the threshold and glass with the gate color through property blocks,
    // so every gate shares the same materials. Local +Z is the gate's outward (exit) direction.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class GateCell : MonoBehaviour
    {
        const int ThresholdMaterialIndex = 0;
        const int GlassMaterialIndex = 1;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] string gateId;
        [SerializeField] string colorName;
        [SerializeField] Color color = new Color(0.6f, 0.6f, 0.6f);
        [SerializeField, Range(0f, 1f)] float glassAlpha = 0.45f;

        MaterialPropertyBlock _block;

        public string GateId => gateId;
        public string ColorName => colorName;
        public Color Color => color;

        public void Setup(string id, string levelColor, Color displayColor)
        {
            gateId = id;
            colorName = levelColor;
            color = displayColor;
            ApplyColor();
        }

        void OnEnable() => ApplyColor();

        void OnValidate() => ApplyColor();

        void ApplyColor()
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;
            _block ??= new MaterialPropertyBlock();
            int materials = meshRenderer.sharedMaterials.Length;

            if (materials > ThresholdMaterialIndex)
                SetColor(meshRenderer, ThresholdMaterialIndex, color);
            if (materials > GlassMaterialIndex)
                SetColor(meshRenderer, GlassMaterialIndex, new Color(color.r, color.g, color.b, glassAlpha));
        }

        void SetColor(MeshRenderer meshRenderer, int materialIndex, Color value)
        {
            _block.Clear();
            _block.SetColor(BaseColorId, value);
            _block.SetColor(ColorId, value);
            meshRenderer.SetPropertyBlock(_block, materialIndex);
        }
    }
}
