using System;
using System.Globalization;

namespace DE.Share.Foundation.Mathematics
{
    public readonly struct Vector2 : IEquatable<Vector2>
    {
        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
        public static Vector2 Zero => default;
        public static Vector2 One => new Vector2(1, 1);
        public static Vector2 UnitX => new Vector2(1, 0);
        public static Vector2 UnitY => new Vector2(0, 1);
        internal double SquaredLength => (double)X * X + (double)Y * Y;
        public float LengthSquared => (float)SquaredLength;
        public float Length => (float)Math.Sqrt(SquaredLength);
        public Vector2 Normalized => Normalize(this);

        public static bool TryNormalize(Vector2 value, out Vector2 result)
        {
            result = Zero;
            double lengthSquared = value.SquaredLength;
            if (lengthSquared <= 0)
            {
                return false;
            }
            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            result = new Vector2((float)(value.X * inverseLength), (float)(value.Y * inverseLength));
            return true;
        }

        public static Vector2 Normalize(Vector2 value)
        {
            if (!TryNormalize(value, out var result))
            {
                throw new InvalidOperationException("Cannot normalize a zero vector.");
            }
            return result;
        }

        public static float Dot(Vector2 left, Vector2 right)
        {
            return (float)((double)left.X * right.X + (double)left.Y * right.Y);
        }

        public static float Distance(Vector2 left, Vector2 right)
        {
            return (left - right).Length;
        }

        public static float DistanceSquared(Vector2 left, Vector2 right)
        {
            return (left - right).LengthSquared;
        }

        public static Vector2 Lerp(Vector2 from, Vector2 to, float amount)
        {
            amount = MathUtil.Clamp(amount, 0, 1);
            return new Vector2((float)((1.0 - amount) * from.X + (double)amount * to.X), (float)((1.0 - amount) * from.Y + (double)amount * to.Y));
        }

        public static Vector2 Min(Vector2 left, Vector2 right)
        {
            return new Vector2(Math.Min(left.X, right.X), Math.Min(left.Y, right.Y));
        }

        public static Vector2 Max(Vector2 left, Vector2 right)
        {
            return new Vector2(Math.Max(left.X, right.X), Math.Max(left.Y, right.Y));
        }

        public static Vector2 Scale(Vector2 left, Vector2 right)
        {
            return new Vector2(left.X * right.X, left.Y * right.Y);
        }

        public static bool Approximately(Vector2 left, Vector2 right, float tolerance = 1e-5f)
        {
            return MathUtil.Approximately(left.X, right.X, tolerance)
                && MathUtil.Approximately(left.Y, right.Y, tolerance);
        }

        public static Vector2 operator +(Vector2 left, Vector2 right)
        {
            return new Vector2(left.X + right.X, left.Y + right.Y);
        }

        public static Vector2 operator -(Vector2 left, Vector2 right)
        {
            return new Vector2(left.X - right.X, left.Y - right.Y);
        }

        public static Vector2 operator -(Vector2 value)
        {
            return new Vector2(-value.X, -value.Y);
        }

        public static Vector2 operator *(Vector2 value, float scale)
        {
            return new Vector2(value.X * scale, value.Y * scale);
        }

        public static Vector2 operator *(float scale, Vector2 value)
        {
            return value * scale;
        }

        public static Vector2 operator /(Vector2 value, float divisor)
        {
            return new Vector2(MathUtil.Divide(value.X, divisor), MathUtil.Divide(value.Y, divisor));
        }

        public bool Equals(Vector2 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object other)
        {
            return other is Vector2 value && Equals(value);
        }

        public override int GetHashCode()
        {
            int hash = X.GetHashCode();
            hash = MathUtil.Hash(hash, Y);
            return hash;
        }

        public static bool operator ==(Vector2 left, Vector2 right) => left.Equals(right);
        public static bool operator !=(Vector2 left, Vector2 right) => !left.Equals(right);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
        }
    }
}
