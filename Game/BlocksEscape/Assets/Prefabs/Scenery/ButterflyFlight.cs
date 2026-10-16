using UnityEngine;

namespace BlocksEscape
{
    // The butterfly sits still on its spot until tapped (TapReaction calls TakeOff); then it flutters up, makes one
    // circle over Circle Around (the pond, the circle nudged towards its spot to stay on screen) and settles back. Flapping is a squash of the model across its wings, which
    // reads as wingbeats from the camera above. Animates the model (first child) so ScreenAnchor keeps owning the root.
    // The path is kept in the root's space, so when a new level re-frames the screen mid-flight (ScreenAnchor moving and
    // scaling the root) the flight moves along and still lands on the spot.
    [DisallowMultipleComponent]
    public sealed class ButterflyFlight : MonoBehaviour
    {
        [Tooltip("What it circles over; its pivot is the circle's centre. Without one it circles just ahead of its spot.")]
        [SerializeField] Transform circleAround;
        [Tooltip("Radius of the circle, in cells.")]
        [SerializeField, Min(0.1f)] float circleRadius = 0.9f;
        [SerializeField, Min(0.3f)] float flightTime = 1.3f;
        [Tooltip("Height of the flight above the ground, in cells.")]
        [SerializeField] float altitude = 0.9f;
        [SerializeField] float flapsPerSecond = 11f;
        [Tooltip("Yaw that turns the model's head along +Z.")]
        [SerializeField] float headingOffset;

        const int CirclePoints = 8;

        Transform _model;
        Vector3 _restPosition, _restScale;
        Quaternion _restRotation;
        Vector3[] _path; // in the root's local space
        float _time = -1f;

        void Start()
        {
            _model = transform.childCount > 0 ? transform.GetChild(0) : transform;
            _restPosition = _model.localPosition;
            _restRotation = _model.localRotation;
            _restScale = _model.localScale;
        }

        // Starts a flight unless one is under way; returns whether it did.
        public bool TakeOff()
        {
            if (_time >= 0f || _model == null) return false;
            Begin();
            return true;
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
                    Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, headingOffset, 0f), Time.deltaTime * 14f);
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

        // From the spot to the near side of the circle, once around it, and back to the spot.
        void Begin()
        {
            var home = _restPosition;
            var homeWorld = transform.TransformPoint(home);
            var centreWorld = circleAround != null ? circleAround.position : homeWorld + transform.forward * (circleRadius * 2f * GridUnits.CellSize);
            centreWorld.y = homeWorld.y;
            // The pond reaches past the screen's corner: slide the circle towards the spot until all of it is on screen.
            var cam = Camera.main;
            for (int step = 0; step < 12 && cam != null && !OnScreen(cam, centreWorld, circleRadius * GridUnits.CellSize); step++)
                centreWorld = Vector3.Lerp(centreWorld, homeWorld, 0.15f);
            var centre = transform.InverseTransformPoint(centreWorld);
            float radius = circleRadius * GridUnits.CellSize / Mathf.Max(transform.lossyScale.x, 1e-4f);
            // Start on the side facing the spot and go round either way.
            var toHome = home - centre;
            float start = Mathf.Atan2(toHome.z, toHome.x);
            float turn = Random.value < 0.5f ? 1f : -1f;

            _path = new Vector3[CirclePoints + 5];
            _path[0] = _path[1] = home;
            for (int k = 0; k <= CirclePoints; k++)
            {
                float a = start + turn * k * 2f * Mathf.PI / CirclePoints;
                _path[k + 2] = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            }
            _path[CirclePoints + 3] = _path[CirclePoints + 4] = home;
            _time = 0f;
        }

        // Whether a circle around the centre, at flight height, stays inside the screen with a small margin.
        bool OnScreen(Camera cam, Vector3 centre, float radius)
        {
            const float margin = 0.05f;
            for (int k = 0; k < CirclePoints; k++)
            {
                float a = k * 2f * Mathf.PI / CirclePoints;
                var p = centre + new Vector3(Mathf.Cos(a) * radius, altitude * GridUnits.CellSize, Mathf.Sin(a) * radius);
                var v = cam.WorldToViewportPoint(p);
                if (v.x < margin || v.x > 1f - margin || v.y < margin || v.y > 1f - margin) return false;
            }
            return true;
        }

        Vector3 Path(float u)
        {
            // Catmull-Rom through the points; height rises after take-off and falls for landing.
            int segments = _path.Length - 3;
            float f = u * segments;
            int i = Mathf.Min((int)f, segments - 1);
            float t = f - i;
            var p = transform.TransformPoint(CatmullRom(_path[i], _path[i + 1], _path[i + 2], _path[i + 3], t));
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.15f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.15f));
            float flutter = Mathf.Sin(u * Mathf.PI * 6f) * 0.12f; // a little bob
            p.y = transform.TransformPoint(_restPosition).y + (altitude + flutter) * GridUnits.CellSize * lift;
            return p;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
