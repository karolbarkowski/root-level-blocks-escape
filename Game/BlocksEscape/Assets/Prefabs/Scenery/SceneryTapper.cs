using UnityEngine;
using UnityEngine.InputSystem;

namespace BlocksEscape
{
    // Turns short taps that don't pick up a block into TapReactions on the scenery. Runs after BlockDragger, so a press
    // that grabbed a block is never also a tap; a press that moves too far is a drag and is ignored too.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class SceneryTapper : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] BlockDragger dragger;
        [Tooltip("How far a press may move and still count as a tap, as a fraction of the screen height.")]
        [SerializeField, Range(0.005f, 0.1f)] float tapSlop = 0.03f;

        bool _pressed;
        Vector2 _pressedAt;

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;
            var screen = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                _pressed = dragger == null || !dragger.IsHolding;
                _pressedAt = screen;
            }
            else if (_pressed && pointer.press.isPressed)
            {
                if ((screen - _pressedAt).magnitude > tapSlop * Screen.height) _pressed = false;
            }
            else if (_pressed)
            {
                _pressed = false;
                Tap(_pressedAt);
            }
        }

        public void Tap(Vector2 screen)
        {
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return;
            var reaction = TapReaction.Under(cam.ScreenPointToRay(screen), out var point);
            if (reaction != null) reaction.Tap(point);
        }
    }
}
