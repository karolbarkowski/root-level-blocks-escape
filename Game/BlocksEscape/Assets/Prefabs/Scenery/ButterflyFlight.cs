using UnityEngine;

namespace BlocksEscape
{
    // The butterfly rests on its spot, slowly opening and closing its wings, and every so often (or when tapped) flutters
    // up, loops over the board and settles back. Flapping is a squash of the model across its wings, which reads as
    // wingbeats from the camera above. Animates the model (first child) so ScreenAnchor keeps owning the root.
    [DisallowMultipleComponent]
    public sealed class ButterflyFlight : MonoBehaviour
    {
        [SerializeField] Board board;
        [Tooltip("Seconds resting between flights.")]
        [SerializeField] Vector2 restTime = new Vector2(7f, 16f);
        [SerializeField, Min(1f)] float flightTime = 6f;
        [Tooltip("Height of the flight above the ground, in cells.")]
        [SerializeField] float altitude = 1.8f;
        [SerializeField] float flapsPerSecond = 9f;
        [Tooltip("Yaw that turns the model's head along +Z.")]
        [SerializeField] float headingOffset = 180f;

        Transform _model;
        Vector3 _restPosition, _restScale;
        Quaternion _restRotation;
        Vector3[] _path;
        float _wait, _time = -1f;

        void Start()
        {
            _model = transform.childCount > 0 ? transform.GetChild(0) : transform;
            _restPosition = _model.localPosition;
            _restRotation = _model.localRotation;
            _restScale = _model.localScale;
            if (board == null) board = FindAnyObjectByType<Board>();
            _wait = Random.Range(restTime.x * 0.5f, restTime.y);
        }

        public void TakeOff()
        {
            if (_time < 0f && _model != null) _wait = 0f;
        }

        void Update()
        {
            if (_model == null) return;
            if (_time < 0f)
            {
                // Resting: a slow, occasional fan of the wings.
                float fan = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * 1.3f)), 8f);
                _model.localScale = Vector3.Scale(_restScale, new Vector3(1f - 0.5f * fan, 1f, 1f));
                _wait -= Time.deltaTime;
                if (_wait <= 0f) Begin();
                return;
            }

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
                _wait = Random.Range(restTime.x, restTime.y);
            }
        }

        // A loop from the resting spot over two random points above the board and back, through a smooth curve.
        void Begin()
        {
            var home = transform.TransformPoint(_restPosition);
            Vector3 a = home, b = home;
            if (board != null)
            {
                var size = board.Size;
                Vector3 Point() => board.transform.TransformPoint(new Vector3(Random.Range(0.15f, 0.85f) * size.x, 0f, Random.Range(0.15f, 0.85f) * size.y));
                a = Point();
                b = Point();
            }
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
