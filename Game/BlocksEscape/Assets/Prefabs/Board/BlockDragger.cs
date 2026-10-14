using UnityEngine;
using UnityEngine.InputSystem;

namespace BlocksEscape
{
    // Drag and drop of blocks with mouse or touch. A held block lifts slightly and follows the pointer,
    // colliding with the grid through BlockSlider. On release it snaps to the nearest cell with a short animation,
    // then Board.Drop applies the rules (a matching gate next to it removes it). While a block is held, the gates of
    // its outer type glow, fading in and out over the lift time.
    [RequireComponent(typeof(Board))]
    [DisallowMultipleComponent]
    public sealed class BlockDragger : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField, Min(0f)] float liftHeight = 0.3f;
        [SerializeField, Min(0.01f)] float liftTime = 0.08f;
        [SerializeField, Min(0.01f)] float snapTime = 0.09f;
        [Tooltip("How far, in cells, a blocked block may be eased sideways to line up with a gap.")]
        [SerializeField, Range(0f, 0.95f)] float cornerAssist = 0.6f;

        Board _board;
        bool _pointerHeld; // the Input System pointer is down; scripted PointerDown calls do not set it
        int _held = -1;
        BlockSlider _slider;
        Vector2 _position;   // held block's anchor in level coordinates, fractional while dragging
        Vector2 _grabOffset; // anchor minus the grabbed point
        float _lift;         // 0..1
        float _dragHeight;   // height of the plane the held block is dragged on, set on grab
        string _axis;        // held block's movement axis, set on grab

        int _snapping = -1;
        Vector3 _snapFrom;
        Vector2Int _snapTo;
        float _snapProgress;

        string _glowType; // outer type of the held (or last held) block, whose gates glow
        float _glow;      // 0..1

        void Awake() => _board = GetComponent<Board>();

        public bool IsHolding => _held >= 0;

        void Update()
        {
            if (_snapping >= 0) AnimateSnap();
            UpdateGlow();
            var pointer = Pointer.current;
            if (pointer == null) return;
            var screen = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame) { _pointerHeld = true; PointerDown(screen); }
            else if (_pointerHeld && pointer.press.isPressed) PointerMove(screen);
            else if (_pointerHeld) { _pointerHeld = false; PointerUp(); }
        }

        // Pointer input in screen pixels; Update feeds these from the Input System, tests and replays can call them directly.
        public void PointerDown(Vector2 screen)
        {
            if (_held < 0 && _board.Rules != null) TryGrab(screen);
        }

        public void PointerMove(Vector2 screen)
        {
            if (_held >= 0) Drag(screen);
        }

        public void PointerUp()
        {
            if (_held >= 0) Release();
        }

        void OnDisable()
        {
            if (_held >= 0) Release();
            if (_snapping >= 0) FinishSnap();
        }

        void TryGrab(Vector2 screen)
        {
            if (_snapping >= 0) FinishSnap();
            var rules = _board.Rules;
            // Aim at block tops first (they hide the floor behind them at this camera angle), then at the floor.
            float top = TopHeight();
            int i = BlockUnder(screen, top);
            if (i < 0) i = BlockUnder(screen, 0f);
            if (i < 0 || _board.BlockAt(i) == null || !rules.CanMove(i)) return;
            float dragHeight = top + liftHeight;
            if (!TryPointerToLevel(screen, dragHeight, out var grabbed)) return;

            _held = i;
            _dragHeight = dragHeight;
            _axis = rules.Axis(i);
            _slider = new BlockSlider(rules, i, cornerAssist);
            _position = rules.Anchor(i);
            _grabOffset = _position - grabbed;
            _lift = 0f;
            var types = rules.RemainingTypes(i);
            var type = types != null && types.Length > 0 ? types[0] : null;
            if (type != _glowType) { _board.GlowGates(_glowType, 0f); _glowType = type; _glow = 0f; }
        }

        void UpdateGlow()
        {
            if (_glowType == null) return;
            float target = _held >= 0 ? 1f : 0f;
            if (Mathf.Approximately(_glow, target)) return;
            _glow = Mathf.MoveTowards(_glow, target, Time.deltaTime / liftTime);
            _board.GlowGates(_glowType, EaseOut(_glow));
        }

        void Drag(Vector2 screen)
        {
            if (TryPointerToLevel(screen, _dragHeight, out var point))
            {
                var target = point + _grabOffset;
                if (_axis == "horizontal") target.y = _position.y;
                else if (_axis == "vertical") target.x = _position.x;
                _position = _slider.Slide(_position, target);
            }
            _lift = Mathf.MoveTowards(_lift, 1f, Time.deltaTime / liftTime);
            _board.BlockAt(_held).transform.localPosition =
                _board.BlockLocalPosition(_held, _position) + Vector3.up * (liftHeight * EaseOut(_lift));
        }

        void Release()
        {
            _snapping = _held;
            _snapFrom = _board.BlockAt(_held).transform.localPosition;
            _snapTo = _slider.Snap(_position);
            _snapProgress = 0f;
            _held = -1;
            _slider = null;
        }

        void AnimateSnap()
        {
            var block = _board.BlockAt(_snapping);
            if (block == null) { _snapping = -1; return; }
            _snapProgress = Mathf.MoveTowards(_snapProgress, 1f, Time.deltaTime / snapTime);
            block.transform.localPosition = Vector3.LerpUnclamped(_snapFrom, _board.BlockLocalPosition(_snapping, _snapTo), EaseOut(_snapProgress));
            if (_snapProgress >= 1f) FinishSnap();
        }

        void FinishSnap()
        {
            int i = _snapping;
            _snapping = -1;
            _board.Drop(i, _snapTo);
        }

        bool TryPointerToLevel(Vector2 screen, float height, out Vector2 point)
        {
            point = default;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(transform.up, transform.TransformPoint(Vector3.up * height));
            if (!plane.Raycast(ray, out float distance)) return false;
            point = _board.LocalToLevel(transform.InverseTransformPoint(ray.GetPoint(distance)));
            return true;
        }

        float TopHeight()
        {
            for (int i = 0; i < _board.Rules.BlockCount; i++)
                if (_board.BlockAt(i) != null) return _board.BlockAt(i).PivotHeight * 2f;
            return 0f;
        }

        int BlockUnder(Vector2 screen, float height) =>
            TryPointerToLevel(screen, height, out var point) ? _board.Rules.BlockAt(Vector2Int.RoundToInt(point)) : -1;

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
    }
}
