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
    // Hiding uses Renderer.forceRenderingOff, which is not saved, so the scene doesn't change with the Game view size.
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

        void OnDisable() => BoardCamera.Framed -= Place;
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
            transform.localScale = Vector3.one * (size * screenHeight / referenceScreenHeight);

            bool hide = avoidBoard && OverlapsBoard(boardCamera.BoardBounds);
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = hide;
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
