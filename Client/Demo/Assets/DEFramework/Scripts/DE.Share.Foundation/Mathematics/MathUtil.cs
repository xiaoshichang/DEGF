using System;

namespace DE.Share.Foundation.Mathematics
{
    public static class MathUtil
    {
        public const float Pi = (float)Math.PI;
        public const float DegreesToRadians = Pi / 180f;
        public const float RadiansToDegrees = 180f / Pi;

        public static float Clamp(float value, float minimum, float maximum)
        {
            if (minimum > maximum)
            {
                throw new ArgumentException("Minimum must not exceed maximum.");
            }
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        public static bool Approximately(float left, float right, float tolerance = 1e-5f)
        {
            if (tolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            }
            return Math.Abs((double)left - right) <= tolerance * Math.Max(1.0, Math.Max(Math.Abs((double)left), Math.Abs((double)right)));
        }

        internal static float Divide(float value, float divisor)
        {
            // Reject exact zero, including -0; a small nonzero divisor is still valid.
            if (divisor.Equals(0f))
            {
                throw new DivideByZeroException();
            }
            return value / divisor;
        }

        internal static int Hash(int hash, float value)
        {
            return unchecked(hash * 397 ^ value.GetHashCode());
        }
    }
}
