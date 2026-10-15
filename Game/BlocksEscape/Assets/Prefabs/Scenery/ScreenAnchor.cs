using UnityEngine;

namespace BlocksEscape
{
    // Keeps a prop (scenery, the level sign, buttons) standing on the ground under a fixed point of the screen.
    // BoardCamera re-frames for every level and aspect ratio, so props placed in the world would drift; after each framing
    // this moves the prop to where the ray through its viewport point meets the ground, and scales it so it keeps the
    // same size on screen. Props stay part of the scene: same light, shadows and outlines as the board.
    //
    // On screens shaped differently from the one the layout was made for, the board takes up a different part of the
    // screen, so scenery can end up on top of it; with Avoid Board on, such a prop is hidden until a framing clears it.
    // Each level's board is different too, so in Play mode a prop the new board covers shrinks away (and one it clears
    // grows back) rather than blinking. Hiding ends in Renderer.forceRenderingOff, which is not saved, so the scene doesn't
    // change with the Game view size.
    // Pop scales the prop on top of that, for show and hide animations (PopTransition).
    //
    // In the editor, a prop moved or scaled by hand (or carried along by its parent) takes its new place as its anchor:
    // Viewport Point and Size are worked out from where it stands, so it stays there instead of snapping back.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ScreenAnchor : MonoBehaviour
    {
        [Tooltip("Where the prop's pivot sits on screen: (0, 0) bottom left, (1, 1) top right.")]
        [SerializeField] Vector2 viewportPoint = new Vector2(0.5f, 0.9f);
        [Tooltip("World height of the ground the prop stands on.")]
        [SerializeField] float groundHeight;
        [Tooltip("Uniform scale when the screen is Reference Screen Height world units tall at the prop.")]
        [SerializeField, Min(0.01f)] float size = 1f;
        [Tooltip("Screen height, in world units at the prop, that Size is made for.")]
        [SerializeField, Min(0.1f)] float referenceScreenHeight = 20f;
        [Tooltip("Hide the prop while it overlaps the board. Off for UI that must always show.")]
        [SerializeField] bool avoidBoard = true;
        [Tooltip("Read Viewport Point inside the screen's safe area (clear of notches and rounded corners). For UI.")]
        [SerializeField] bool keepInSafeArea;

        const float AvoidTime = 0.25f; // seconds a prop takes to shrink away from the board or grow back, in Play mode

        float _pop = 1f;
        float _placedScale; // from the last placement, before Pop
        bool _hidden;       // overlaps the board as of the last placement
        float _shown = 1f;  // 0 hidden .. 1 shown; eases towards !_hidden in Play mode, jumps in the editor
        bool _settled;      // placed once since enabled; the first placement doesn't animate
#if UNITY_EDITOR
        Vector3 _placedPosition;
        bool _placed;
#endif

        // Extra scale for show and hide animations; 1 is the placed size. Not saved.
        public float Pop
        {
            get => _pop;
            set { _pop = value; ApplyScale(); }
        }

        public Vector2 ViewportPoint
        {
            get => viewportPoint;
            set { viewportPoint = value; Place(); }
        }

        void OnEnable()
        {
            BoardCamera.Framed += Place;
            Place();
        }

        void OnDisable()
        {
            BoardCamera.Framed -= Place;
            _settled = false;
        }
        void OnValidate() => Place();

        void Place()
        {
            var boardCamera = Object.FindAnyObjectByType<BoardCamera>();
            if (boardCamera != null) Place(boardCamera);
        }

        void Place(BoardCamera boardCamera)
        {
            var camera = boardCamera.Camera;
            if (camera == null || !isActiveAndEnabled) return;
            var vp = viewportPoint;
            if (keepInSafeArea && Screen.width > 0 && Screen.height > 0)
            {
                var safe = Screen.safeArea;
                vp = new Vector2((safe.xMin + vp.x * safe.width) / Screen.width, (safe.yMin + vp.y * safe.height) / Screen.height);
            }
            var ray = camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            var ground = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            if (!ground.Raycast(ray, out float distance)) return;
            var point = ray.GetPoint(distance);
            transform.position = point;
            // World height of the screen at this depth, for a perspective camera.
            float depth = Vector3.Dot(point - camera.transform.position, camera.transform.forward);
            float screenHeight = camera.orthographic
                ? camera.orthographicSize * 2f
                : 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _placedScale = size * screenHeight / referenceScreenHeight;
#if UNITY_EDITOR
            _placedPosition = transform.position;
            _placed = true;
#endif

            // Tested at full size (renderer bounds follow the scale); nothing is drawn before ApplyScale below.
            transform.localScale = Vector3.one * (_placedScale * Mathf.Max(_pop, 1e-3f));
            _hidden = avoidBoard && OverlapsBoard(boardCamera.BoardBounds);
            if (!Application.isPlaying || !_settled) _shown = _hidden ? 0f : 1f;
            _settled = true;
            ApplyScale();
            ApplyVisibility();
        }

        void Update()
        {
            if (Application.isPlaying)
            {
                // A new level's board covering the prop (or clearing it): shrink away / grow back instead of blinking.
                float target = _hidden ? 0f : 1f;
                if (_shown == target) return;
                _shown = Mathf.MoveTowards(_shown, target, Time.deltaTime / AvoidTime);
                ApplyScale();
                ApplyVisibility();
                return;
            }
#if UNITY_EDITOR
            EditorUpdate();
#endif
        }

        void ApplyVisibility()
        {
            bool off = _shown <= 0f;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = off;
        }

#if UNITY_EDITOR
        void EditorUpdate()
        {
            if (!_placed) return;
            bool moved = (transform.position - _placedPosition).sqrMagnitude > 1e-8f;
            bool scaled = Mathf.Abs(transform.localScale.x - _placedScale * Mathf.Max(_pop, 1e-3f)) > 1e-4f;
            if (moved || scaled) Adopt();
        }

        // Re-anchors the prop where it now stands and at its current scale.
        void Adopt()
        {
            var boardCamera = Object.FindAnyObjectByType<BoardCamera>();
            var camera = boardCamera != null ? boardCamera.Camera : null;
            if (camera == null || _placedScale <= 0f) return;
            UnityEditor.Undo.RecordObject(this, "Move Anchored Prop");
            Vector2 vp = camera.WorldToViewportPoint(transform.position);
            if (keepInSafeArea && Screen.width > 0 && Screen.height > 0)
            {
                var safe = Screen.safeArea;
                vp = new Vector2((vp.x * Screen.width - safe.xMin) / safe.width, (vp.y * Screen.height - safe.yMin) / safe.height);
            }
            viewportPoint = vp;
            size *= transform.localScale.x / (_placedScale * Mathf.Max(_pop, 1e-3f));
            Place(boardCamera);
        }
#endif

        void ApplyScale()
        {
            // Never quite zero: colliders and bounds don't like degenerate scales.
            // In the editor a hidden prop keeps its size (it's only not drawn), so moving it by hand still re-anchors it.
            float shown = Application.isPlaying ? Mathf.Max(Ease.OutBack(_shown), 1e-3f) : 1f;
            if (_placedScale > 0f) transform.localScale = Vector3.one * (_placedScale * Mathf.Max(_pop, 1e-3f) * shown);
        }

        // Whether the prop's footprint on the ground, shrunk a little so leaves may brush the fence, overlaps the board's.
        bool OverlapsBoard(Bounds board)
        {
            if (board.size == Vector3.zero) return false;
            bool any = false;
            var prop = new Bounds();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                if (!any) { prop = b; any = true; }
                else prop.Encapsulate(b);
            }
            if (!any) return false;
            var shrink = new Vector3(prop.size.x * 0.25f, 0f, prop.size.z * 0.25f);
            prop.size -= shrink;
            return prop.min.x < board.max.x && prop.max.x > board.min.x &&
                   prop.min.z < board.max.z && prop.max.z > board.min.z;
        }
    }
}
