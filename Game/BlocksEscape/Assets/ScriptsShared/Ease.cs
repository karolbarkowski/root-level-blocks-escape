namespace BlocksEscape
{
    // Easing curves for t in 0..1.
    public static class Ease
    {
        // Overshoots past 1 and settles back: things popping in.
        public static float OutBack(float t, float c = 1.4f)
        {
            float u = t - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }

        // Pulls back below 0 first, then speeds up: things popping out (as 1 - InBack).
        public static float InBack(float t, float c = 1.4f) => (c + 1f) * t * t * t - c * t * t;
    }
}
