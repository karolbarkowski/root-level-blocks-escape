using UnityEngine;
using UnityEngine.InputSystem;

namespace BlocksEscape
{
    // Drag and drop of blocks with mouse or touch. A held block lifts slightly and follows the pointer,
    // colliding with the grid through BlockSlider. On release it snaps to the nearest cell with a short animation,
    // then Board.Drop applies the rules (a matching gate next to it removes it).
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

        int _snapping = -1;
        Vector3 _snapFrom;
        Vector2Int _snapTo;
        float _snapProgress;

        void Awake() => _board = GetComponent<Board>();

        public bool IsHolding => _held >= 0;

        void Update()
        {
            if (_snapping >= 0) AnimateSnap();
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
            int i = -1;
            foreach (float height in new[] { TopHeight(), 0f })
                if (TryPointerToLevel(screen, height, out var point) && (i = rules.BlockAt(Vector2Int.RoundToInt(point))) >= 0) break;
            if (i < 0 || _board.BlockAt(i) == null || !rules.CanMove(i)) return;
            if (!TryPointerToLevel(screen, DragHeight(), out var grabbed)) return;

            _held = i;
            _slider = new BlockSlider(rules, i, cornerAssist);
            _position = rules.Anchor(i);
            _grabOffset = _position - grabbed;
            _lift = 0f;
        }

        void Drag(Vector2 screen)
        {
            if (TryPointerToLevel(screen, DragHeight(), out var point))
            {
                var target = point + _grabOffset;
                string axis = _board.Rules.Axis(_held);
                if (axis == "horizontal") target.y = _position.y;
                else if (axis == "vertical") target.x = _position.x;
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

        float DragHeight() => TopHeight() + liftHeight;

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
    }
}
