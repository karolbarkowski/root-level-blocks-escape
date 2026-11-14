using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace BlocksEscape
{
    // A 3D prop that works as a button: pressing it with mouse or touch squashes it, and releasing over it raises Clicked.
    // A press that moves further than Drag Tolerance is a drag or a swipe (the level picker's pages), not a click.
    // Hit-tests its own colliders with a ray from the camera, so it needs a collider on itself or a child.
    // Every click is also announced on AnyClicked, after the button's own listeners (SoundPlayer's click sound).
    [DisallowMultipleComponent]
    public sealed class WorldButton : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [Tooltip("Height scale while pressed.")]
        [SerializeField, Range(0.5f, 1f)] float pressedSquash = 0.8f;
        [SerializeField, Min(0.01f)] float animationTime = 0.08f;
        [Tooltip("How far the pointer may move while pressed and still click, as a share of the screen height.")]
        [SerializeField, Range(0f, 0.2f)] float dragTolerance = 0.03f;
        [SerializeField] UnityEvent clicked = new UnityEvent();

        public UnityEvent Clicked => clicked;
        public static event Action<WorldButton> AnyClicked;

        static int _syncedFrame = -1; // frame the colliders were last synced to their props, shared by all buttons

        Transform _visual;
        Collider[] _colliders; // found on the first press
        bool _pressed;
        Vector2 _pressedAt;
        float _squash = 1f;

        void Awake()
        {
            // Squash a child, so the anchor's position and scale stay its own.
            _visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
        }

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer != null)
            {
                var screen = pointer.position.ReadValue();
                if (pointer.press.wasPressedThisFrame)
                {
                    _pressed = IsUnder(screen);
                    _pressedAt = screen;
                }
                else if (_pressed && (screen - _pressedAt).magnitude > dragTolerance * Screen.height) _pressed = false;
                else if (_pressed && pointer.press.wasReleasedThisFrame)
                {
                    _pressed = false;
                    if (IsUnder(screen))
                    {
                        clicked.Invoke();
                        AnyClicked?.Invoke(this);
                    }
                }
            }
            float target = _pressed ? pressedSquash : 1f;
            if (_squash == target) return; // at rest: the model is left alone
            _squash = Mathf.MoveTowards(_squash, target, (1f - pressedSquash) / animationTime * Time.unscaledDeltaTime);
            var scale = _visual.localScale;
            _visual.localScale = new Vector3(scale.x, _squash, scale.z);
        }

        void OnDisable()
        {
            _pressed = false;
            _squash = 1f;
            if (_visual != null) _visual.localScale = new Vector3(_visual.localScale.x, 1f, _visual.localScale.z);
        }

        bool IsUnder(Vector2 screen)
        {
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screen);
            // Physics is stepped from script, so moved props' colliders lag until synced: once a frame, for every button.
            if (_syncedFrame != Time.frameCount)
            {
                _syncedFrame = Time.frameCount;
                Physics.SyncTransforms();
            }
            _colliders ??= GetComponentsInChildren<Collider>(true);
            foreach (var c in _colliders)
                if (c != null && c.Raycast(ray, out _, 1000f)) return true;
            return false;
        }
    }
}
