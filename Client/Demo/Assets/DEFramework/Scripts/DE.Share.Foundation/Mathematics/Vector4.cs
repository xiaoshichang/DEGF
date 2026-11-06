using System;
using System.Globalization;

namespace DE.Share.Foundation.Mathematics
{
    public readonly struct Vector4 : IEquatable<Vector4>
    {
        public Vector4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }
        public static Vector4 Zero => default;
        public static Vector4 One => new Vector4(1, 1, 1, 1);
        public static Vector4 UnitX => new Vector4(1, 0, 0, 0);
        public static Vector4 UnitY => new Vector4(0, 1, 0, 0);
        public static Vector4 UnitZ => new Vector4(0, 0, 1, 0);
        public static Vector4 UnitW => new Vector4(0, 0, 0, 1);
        internal double SquaredLength => (double)X * X + (double)Y * Y + (double)Z * Z + (double)W * W;
        public float LengthSquared => (float)SquaredLength;
        public float Length => (float)Math.Sqrt(SquaredLength);
        public Vector4 Normalized => Normalize(this);

        public static bool TryNormalize(Vector4 value, out Vector4 result)
        {
            result = Zero;
            double lengthSquared = value.SquaredLength;
            if (lengthSquared <= 0)
            {
                return false;
            }
            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            result = new Vector4(
                (float)(value.X * inverseLength),
                (float)(value.Y * inverseLength),
                (float)(value.Z * inverseLength),
                (float)(value.W * inverseLength));
            return true;
        }

        public static Vector4 Normalize(Vector4 value)
        {
            if (!TryNormalize(value, out var result))
            {
                throw new InvalidOperationException("Cannot normalize a zero vector.");
            }
            return result;
        }

        public static float Dot(Vector4 left, Vector4 right)
        {
            return (float)((double)left.X * right.X + (double)left.Y * right.Y + (double)left.Z * right.Z + (double)left.W * right.W);
        }

        public static float Distance(Vector4 left, Vector4 right)
        {
            return (left - right).Length;
        }

        public static float DistanceSquared(Vector4 left, Vector4 right)
        {
            return (left - right).LengthSquared;
        }

        public static Vector4 Lerp(Vector4 from, Vector4 to, float amount)
        {
            amount = MathUtil.Clamp(amount, 0, 1);
            return new Vector4(
                (float)((1.0 - amount) * from.X + (double)amount * to.X),
                (float)((1.0 - amount) * from.Y + (double)amount * to.Y),
                (float)((1.0 - amount) * from.Z + (double)amount * to.Z),
                (float)((1.0 - amount) * from.W + (double)amount * to.W));
        }

        public static Vector4 Min(Vector4 left, Vector4 right)
        {
            return new Vector4(Math.Min(left.X, right.X), Math.Min(left.Y, right.Y), Math.Min(left.Z, right.Z), Math.Min(left.W, right.W));
        }

        public static Vector4 Max(Vector4 left, Vector4 right)
        {
            return new Vector4(Math.Max(left.X, right.X), Math.Max(left.Y, right.Y), Math.Max(left.Z, right.Z), Math.Max(left.W, right.W));
        }

        public static Vector4 Scale(Vector4 left, Vector4 right)
        {
            return new Vector4(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);
        }

        public static bool Approximately(Vector4 left, Vector4 right, float tolerance = 1e-5f)
        {
            return MathUtil.Approximately(left.X, right.X, tolerance)
                && MathUtil.Approximately(left.Y, right.Y, tolerance)
                && MathUtil.Approximately(left.Z, right.Z, tolerance)
                && MathUtil.Approximately(left.W, right.W, tolerance);
        }

        public static Vector4 operator +(Vector4 left, Vector4 right)
        {
            return new Vector4(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
        }

        public static Vector4 operator -(Vector4 left, Vector4 right)
        {
            return new Vector4(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);
        }

        public static Vector4 operator -(Vector4 value)
        {
            return new Vector4(-value.X, -value.Y, -value.Z, -value.W);
        }

        public static Vector4 operator *(Vector4 value, float scale)
        {
            return new Vector4(value.X * scale, value.Y * scale, value.Z * scale, value.W * scale);
        }

        public static Vector4 operator *(float scale, Vector4 value)
        {
            return value * scale;
        }

        public static Vector4 operator /(Vector4 value, float divisor)
        {
            return new Vector4(
                MathUtil.Divide(value.X, divisor),
                MathUtil.Divide(value.Y, divisor),
                MathUtil.Divide(value.Z, divisor),
                MathUtil.Divide(value.W, divisor));
        }

        public bool Equals(Vector4 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        }

        public override bool Equals(object other)
        {
            return other is Vector4 value && Equals(value);
        }

        public override int GetHashCode()
        {
            int hash = X.GetHashCode();
            hash = MathUtil.Hash(hash, Y);
            hash = MathUtil.Hash(hash, Z);
            hash = MathUtil.Hash(hash, W);
            return hash;
        }

        public static bool operator ==(Vector4 left, Vector4 right) => left.Equals(right);
        public static bool operator !=(Vector4 left, Vector4 right) => !left.Equals(right);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2}, {3})", X, Y, Z, W);
        }
    }
}
