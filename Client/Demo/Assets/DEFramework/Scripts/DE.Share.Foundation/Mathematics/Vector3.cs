using System;
using System.Globalization;

namespace DE.Share.Foundation.Mathematics
{
    public readonly struct Vector3 : IEquatable<Vector3>
    {
        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public static Vector3 Zero => default;
        public static Vector3 One => new Vector3(1, 1, 1);
        public static Vector3 UnitX => new Vector3(1, 0, 0);
        public static Vector3 UnitY => new Vector3(0, 1, 0);
        public static Vector3 UnitZ => new Vector3(0, 0, 1);
        internal double SquaredLength => (double)X * X + (double)Y * Y + (double)Z * Z;
        public float LengthSquared => (float)SquaredLength;
        public float Length => (float)Math.Sqrt(SquaredLength);
        public Vector3 Normalized => Normalize(this);

        public static bool TryNormalize(Vector3 value, out Vector3 result)
        {
            result = Zero;
            double lengthSquared = value.SquaredLength;
            if (lengthSquared <= 0)
            {
                return false;
            }
            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            result = new Vector3((float)(value.X * inverseLength), (float)(value.Y * inverseLength), (float)(value.Z * inverseLength));
            return true;
        }

        public static Vector3 Normalize(Vector3 value)
        {
            if (!TryNormalize(value, out var result))
            {
                throw new InvalidOperationException("Cannot normalize a zero vector.");
            }
            return result;
        }

        public static float Dot(Vector3 left, Vector3 right)
        {
            return (float)((double)left.X * right.X + (double)left.Y * right.Y + (double)left.Z * right.Z);
        }

        public static float Distance(Vector3 left, Vector3 right)
        {
            return (left - right).Length;
        }

        public static float DistanceSquared(Vector3 left, Vector3 right)
        {
            return (left - right).LengthSquared;
        }

        public static Vector3 Lerp(Vector3 from, Vector3 to, float amount)
        {
            amount = MathUtil.Clamp(amount, 0, 1);
            return new Vector3(
                (float)((1.0 - amount) * from.X + (double)amount * to.X),
                (float)((1.0 - amount) * from.Y + (double)amount * to.Y),
                (float)((1.0 - amount) * from.Z + (double)amount * to.Z));
        }

        public static Vector3 Min(Vector3 left, Vector3 right)
        {
            return new Vector3(Math.Min(left.X, right.X), Math.Min(left.Y, right.Y), Math.Min(left.Z, right.Z));
        }

        public static Vector3 Max(Vector3 left, Vector3 right)
        {
            return new Vector3(Math.Max(left.X, right.X), Math.Max(left.Y, right.Y), Math.Max(left.Z, right.Z));
        }

        public static Vector3 Scale(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        }

        public static bool Approximately(Vector3 left, Vector3 right, float tolerance = 1e-5f)
        {
            return MathUtil.Approximately(left.X, right.X, tolerance)
                && MathUtil.Approximately(left.Y, right.Y, tolerance)
                && MathUtil.Approximately(left.Z, right.Z, tolerance);
        }

        public static Vector3 Cross(Vector3 left, Vector3 right)
        {
            return new Vector3(
                (float)((double)left.Y * right.Z - (double)left.Z * right.Y),
                (float)((double)left.Z * right.X - (double)left.X * right.Z),
                (float)((double)left.X * right.Y - (double)left.Y * right.X));
        }

        public static Vector3 Reflect(Vector3 value, Vector3 normal)
        {
            normal = Normalize(normal);
            double projection = (double)value.X * normal.X + (double)value.Y * normal.Y + (double)value.Z * normal.Z;
            return new Vector3(
                (float)(value.X - 2 * projection * normal.X),
                (float)(value.Y - 2 * projection * normal.Y),
                (float)(value.Z - 2 * projection * normal.Z));
        }

        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static Vector3 operator -(Vector3 value)
        {
            return new Vector3(-value.X, -value.Y, -value.Z);
        }

        public static Vector3 operator *(Vector3 value, float scale)
        {
            return new Vector3(value.X * scale, value.Y * scale, value.Z * scale);
        }

        public static Vector3 operator *(float scale, Vector3 value)
        {
            return value * scale;
        }

        public static Vector3 operator /(Vector3 value, float divisor)
        {
            return new Vector3(MathUtil.Divide(value.X, divisor), MathUtil.Divide(value.Y, divisor), MathUtil.Divide(value.Z, divisor));
        }

        public bool Equals(Vector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object other)
        {
            return other is Vector3 value && Equals(value);
        }

        public override int GetHashCode()
        {
            int hash = X.GetHashCode();
            hash = MathUtil.Hash(hash, Y);
            hash = MathUtil.Hash(hash, Z);
            return hash;
        }

        public static bool operator ==(Vector3 left, Vector3 right) => left.Equals(right);
        public static bool operator !=(Vector3 left, Vector3 right) => !left.Equals(right);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
        }
    }
}
