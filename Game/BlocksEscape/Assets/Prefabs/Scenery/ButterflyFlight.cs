using UnityEngine;

namespace BlocksEscape
{
    // The butterfly sits still on its spot until tapped (TapReaction calls TakeOff); then it flutters up, loops over the
    // meadow below the board and settles back. Flapping is a squash of the model across its wings, which reads as
    // wingbeats from the camera above. Animates the model (first child) so ScreenAnchor keeps owning the root.
    [DisallowMultipleComponent]
    public sealed class ButterflyFlight : MonoBehaviour
    {
        [Tooltip("Where it flies, in viewport coordinates (0..1 across the screen, y up): the meadow below the board.")]
        [SerializeField] Rect flightArea = new Rect(0.12f, 0.08f, 0.76f, 0.2f);
        [SerializeField, Min(1f)] float flightTime = 6f;
        [Tooltip("Height of the flight above the ground, in cells.")]
        [SerializeField] float altitude = 1.2f;
        [SerializeField] float flapsPerSecond = 9f;
        [Tooltip("Yaw that turns the model's head along +Z.")]
        [SerializeField] float headingOffset;

        Transform _model;
        Vector3 _restPosition, _restScale;
        Quaternion _restRotation;
        Vector3[] _path;
        float _time = -1f;

        void Start()
        {
            _model = transform.childCount > 0 ? transform.GetChild(0) : transform;
            _restPosition = _model.localPosition;
            _restRotation = _model.localRotation;
            _restScale = _model.localScale;
        }

        public void TakeOff()
        {
            if (_time < 0f && _model != null) Begin();
        }

        void Update()
        {
            if (_model == null || _time < 0f) return; // resting: still

            _time += Time.deltaTime;
            float u = Mathf.Clamp01(_time / flightTime);
            var p = Path(u);
            var ahead = Path(Mathf.Min(u + 0.01f, 1f)) - p;
            _model.position = p;
            ahead.y = 0f;
            if (ahead.sqrMagnitude > 1e-6f)
                _model.rotation = Quaternion.Slerp(_model.rotation,
                    Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, headingOffset, 0f), Time.deltaTime * 8f);
            float beat = Mathf.Abs(Mathf.Sin(_time * flapsPerSecond * Mathf.PI));
            _model.localScale = Vector3.Scale(_restScale, new Vector3(0.2f + 0.8f * beat, 1f, 1f));

            if (u >= 1f)
            {
                _time = -1f;
                _model.localPosition = _restPosition;
                _model.localRotation = _restRotation;
                _model.localScale = _restScale;
            }
        }

        // A loop from the resting spot over two random points in the flight area and back, through a smooth curve.
        void Begin()
        {
            var home = transform.TransformPoint(_restPosition);
            var cam = Camera.main;
            var ground = new Plane(Vector3.up, home);
            Vector3 Point()
            {
                if (cam == null) return home;
                var ray = cam.ViewportPointToRay(new Vector3(Random.Range(flightArea.xMin, flightArea.xMax), Random.Range(flightArea.yMin, flightArea.yMax), 0f));
                return ground.Raycast(ray, out float d) ? ray.GetPoint(d) : home;
            }
            var a = Point();
            var b = Point();
            _path = new[] { home, home, a, b, home, home };
            _time = 0f;
        }

        Vector3 Path(float u)
        {
            // Catmull-Rom through home, a, b, home; height rises after take-off and falls for landing.
            int segments = _path.Length - 3;
            float f = u * segments;
            int i = Mathf.Min((int)f, segments - 1);
            float t = f - i;
            var p = CatmullRom(_path[i], _path[i + 1], _path[i + 2], _path[i + 3], t);
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.15f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.15f));
            float flutter = Mathf.Sin(u * Mathf.PI * 9f) * 0.15f; // a little bob
            p.y = _path[0].y + (altitude + flutter) * GridUnits.CellSize * lift;
            return p;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
