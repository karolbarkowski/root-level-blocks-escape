using UnityEngine;

namespace BlocksEscape
{
    // Frames the board with a perspective camera looking down from the level's bottom edge,
    // tilted so block sides show slightly. Fits everything the board renders (surface, walls, gates, blocks)
    // with a margin, centered between the reserved strips at the top and bottom of the screen (kept free for the level
    // sign and scenery), and re-frames when a level is initialized or the aspect ratio changes.
    // Raises Framed afterwards, so screen-anchored props (ScreenAnchor) can follow.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class BoardCamera : MonoBehaviour
    {
        [SerializeField] Board board;
        [SerializeField, Range(30f, 90f)] float pitch = 70f;      // 90 = straight down
        [SerializeField, Range(10f, 60f)] float fieldOfView = 30f; // lower = flatter, less perspective distortion
        [SerializeField, Min(0f)] float margin = 0.4f;             // in cells, around the framed content
        [Tooltip("Screen height kept free above the board, 0..1.")]
        [SerializeField, Range(0f, 0.45f)] float reserveTop = 0.18f;
        [Tooltip("Screen height kept free below the board, 0..1.")]
        [SerializeField, Range(0f, 0.45f)] float reserveBottom = 0.08f;

        // Raised after every framing, with the camera already moved.
        public static event System.Action<BoardCamera> Framed;

        public Camera Camera => _camera;

        // World bounds of everything the board renders, as of the last framing; scenery keeps clear of it.
        public Bounds BoardBounds { get; private set; }

        Camera _camera;
        float _framedAspect;
        Board _subscribed;

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Subscribe();
            Frame();
        }

        void OnDisable()
        {
            if (_subscribed != null) _subscribed.Initialized -= Frame;
            _subscribed = null;
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled || _camera == null) return;
            Subscribe();
            Frame();
        }

        void Subscribe()
        {
            if (_subscribed == board) return;
            if (_subscribed != null) _subscribed.Initialized -= Frame;
            _subscribed = board;
            if (_subscribed != null) _subscribed.Initialized += Frame;
        }

        void LateUpdate()
        {
            if (!Mathf.Approximately(_framedAspect, _camera.aspect)) Frame();
        }

        public void Frame()
        {
            if (board == null || _camera == null) return;
            _framedAspect = _camera.aspect;
            _camera.orthographic = false;
            _camera.fieldOfView = fieldOfView;

            var rotation = board.transform.rotation * Quaternion.Euler(pitch, 0f, 0f);
            var bounds = ContentBounds();
            BoardBounds = bounds;
            bounds.Expand(margin * 2f * GridUnits.CellSize);

            // In camera-aligned axes around the bounds center, a point (x, y, z) is in view when
            // -tanH (z - cz) <= x - cx <= tanH (z - cz) and -tanDown (z - cz) <= y - cy <= tanUp (z - cz), where tanUp and
            // tanDown narrow the vertical frustum to the screen between the reserved strips. Solving for the camera
            // position (cx, cy, cz) that touches the frustum on both sides of the tighter axis, and centering on the other,
            // fits the content in that window.
            float tanV = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * _camera.aspect;
            float bottom = Mathf.Min(reserveBottom, 0.9f - reserveTop);
            float tanUp = tanV * (1f - 2f * reserveTop);   // viewport y (1 - reserveTop)
            float tanDown = tanV * (1f - 2f * bottom);     // viewport y (bottom)
            float up = float.MinValue, down = float.MinValue, right = float.MinValue, left = float.MinValue;
            var toCamera = Quaternion.Inverse(rotation);
            for (int i = 0; i < 8; i++)
            {
                var p = toCamera * (Corner(bounds, i) - bounds.center);
                up = Mathf.Max(up, p.y - tanUp * p.z);
                down = Mathf.Max(down, -p.y - tanDown * p.z);
                right = Mathf.Max(right, p.x - tanH * p.z);
                left = Mathf.Max(left, -p.x - tanH * p.z);
            }

            float cz = Mathf.Min(-(up + down) / (tanUp + tanDown), -(right + left) / (2f * tanH));
            float cx = (right - left) * 0.5f;
            float cy = ((up + tanUp * cz) - (down + tanDown * cz)) * 0.5f;
            transform.SetPositionAndRotation(bounds.center + rotation * new Vector3(cx, cy, cz), rotation);
            FitClipPlanes();
            Framed?.Invoke(this);
        }

        // The distance to the board changes with the level and the aspect ratio, so fixed clip planes cut the near side
        // of the view (or the far trees) on some screens. Near stays small; far reaches past the ground at the top edge.
        void FitClipPlanes()
        {
            var ground = new Plane(board.transform.up, board.transform.position);
            float far = 0f;
            foreach (var x in new[] { 0f, 0.5f, 1f })
            {
                var ray = _camera.ViewportPointToRay(new Vector3(x, 1f, 0f));
                far = Mathf.Max(far, ground.Raycast(ray, out float d) ? d : 200f);
            }
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = far + 10f; // trees and scenery stand up to a few cells above the ground
        }

        static Vector3 Corner(Bounds b, int i) =>
            b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));

        Bounds ContentBounds()
        {
            var renderers = board.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                var size = board.Size;
                return new Bounds(board.transform.TransformPoint(new Vector3(size.x, 0f, size.y) * 0.5f), new Vector3(size.x, 0f, size.y));
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }
    }
}
