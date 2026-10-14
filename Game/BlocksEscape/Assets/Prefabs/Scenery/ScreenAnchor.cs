using UnityEngine;

namespace BlocksEscape
{
    // Keeps a prop (scenery, the level sign, buttons) standing on the ground under a fixed point of the screen.
    // BoardCamera re-frames for every level and aspect ratio, so props placed in the world would drift; after each framing
    // this moves the prop to where the ray through its viewport point meets the ground, and scales it so it keeps the
    // same size on screen. Props stay part of the scene: same light, shadows and outlines as the board.
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

        public Vector2 ViewportPoint
        {
            get => viewportPoint;
            set { viewportPoint = value; Place(); }
        }

        void OnEnable()
        {
            BoardCamera.Framed += OnFramed;
            Place();
        }

        void OnDisable() => BoardCamera.Framed -= OnFramed;
        void OnValidate() => Place();

        void OnFramed(BoardCamera boardCamera) => Place(boardCamera.Camera);

        void Place() => Place(Object.FindAnyObjectByType<BoardCamera>() is { } bc ? bc.Camera : Camera.main);

        void Place(Camera camera)
        {
            if (camera == null || !isActiveAndEnabled) return;
            var ray = camera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
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
        }
    }
}
