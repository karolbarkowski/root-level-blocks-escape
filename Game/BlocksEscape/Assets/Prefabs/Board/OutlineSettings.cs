using UnityEngine;

namespace BlocksEscape
{
    // One place to style the outline around every block and gate (Shaders/Outline.shader).
    // Publishes the values as global shader properties, in edit mode too, so changes show live in the Scene and Game views.
    // Disabling the component hides the outlines.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class OutlineSettings : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int TintId = Shader.PropertyToID("_OutlineTint");
        static readonly int DarkenId = Shader.PropertyToID("_OutlineDarken");
        static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int OpacityId = Shader.PropertyToID("_OutlineOpacity");

        [SerializeField] Color color = new Color(0.16f, 0.1f, 0.07f);
        [Tooltip("0 = always the colour above, 1 = each piece's own colour, darkened.")]
        [SerializeField, Range(0f, 1f)] float tintFromPiece = 1f;
        [Tooltip("Brightness of the piece's own colour in the outline; lower is darker.")]
        [SerializeField, Range(0f, 1f)] float pieceBrightness = 0.26f;
        [Tooltip("Outline thickness, in cells.")]
        [SerializeField, Range(0f, 0.15f)] float width = 0.075f;
        [SerializeField, Range(0f, 1f)] float opacity = 1f;

        public Color Color { get => color; set { color = value; Apply(); } }
        public float TintFromPiece { get => tintFromPiece; set { tintFromPiece = Mathf.Clamp01(value); Apply(); } }
        public float PieceBrightness { get => pieceBrightness; set { pieceBrightness = Mathf.Clamp01(value); Apply(); } }
        public float Width { get => width; set { width = Mathf.Max(0f, value); Apply(); } }
        public float Opacity { get => opacity; set { opacity = Mathf.Clamp01(value); Apply(); } }

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        void OnDisable()
        {
            Shader.SetGlobalFloat(WidthId, 0f);
            Shader.SetGlobalFloat(OpacityId, 0f);
        }

        void Apply()
        {
            if (!isActiveAndEnabled) return;
            Shader.SetGlobalColor(ColorId, color);
            Shader.SetGlobalFloat(TintId, tintFromPiece);
            Shader.SetGlobalFloat(DarkenId, pieceBrightness);
            Shader.SetGlobalFloat(WidthId, width);
            Shader.SetGlobalFloat(OpacityId, opacity);
        }
    }
}
