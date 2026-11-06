using System;
using System.Globalization;

namespace DE.Share.Foundation.Mathematics
{
    public readonly struct Quaternion : IEquatable<Quaternion>
    {
        public Quaternion(float x, float y, float z, float w)
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
        public static Quaternion Identity => new Quaternion(0, 0, 0, 1);
        private double SquaredLength => (double)X * X + (double)Y * Y + (double)Z * Z + (double)W * W;
        public float LengthSquared => (float)SquaredLength;
        public float Length => (float)Math.Sqrt(SquaredLength);
        public Quaternion Normalized => Normalize(this);

        public static bool TryNormalize(Quaternion value, out Quaternion result)
        {
            result = Identity;
            if (value.SquaredLength <= 0)
            {
                return false;
            }
            double inverseLength = 1.0 / Math.Sqrt(value.SquaredLength);
            result = new Quaternion(
                (float)(value.X * inverseLength), (float)(value.Y * inverseLength),
                (float)(value.Z * inverseLength), (float)(value.W * inverseLength));
            return true;
        }

        public static Quaternion Normalize(Quaternion value)
        {
            if (!TryNormalize(value, out var result))
            {
                throw new InvalidOperationException("Cannot normalize a zero quaternion.");
            }
            return result;
        }

        public static Quaternion Conjugate(Quaternion value)
        {
            return new Quaternion(-value.X, -value.Y, -value.Z, value.W);
        }

        public static bool TryInverse(Quaternion value, out Quaternion result)
        {
            result = Identity;
            if (value.SquaredLength <= 0)
            {
                return false;
            }
            double inverseSquaredLength = 1.0 / value.SquaredLength;
            result = new Quaternion(
                (float)(-value.X * inverseSquaredLength), (float)(-value.Y * inverseSquaredLength),
                (float)(-value.Z * inverseSquaredLength), (float)(value.W * inverseSquaredLength));
            return true;
        }

        public static Quaternion Inverse(Quaternion value)
        {
            if (!TryInverse(value, out var result))
            {
                throw new InvalidOperationException("Cannot invert a zero quaternion.");
            }
            return result;
        }

        public static float Dot(Quaternion left, Quaternion right)
        {
            return (float)((double)left.X * right.X + (double)left.Y * right.Y + (double)left.Z * right.Z + (double)left.W * right.W);
        }

        /// <summary>Creates a right-handed rotation around an arbitrary nonzero axis. Angle is in radians.</summary>
        public static Quaternion CreateFromAxisAngle(Vector3 axis, float angle)
        {
            axis = Vector3.Normalize(axis);
            double halfAngle = (double)angle / 2;
            double sine = Math.Sin(halfAngle);
            return Normalize(new Quaternion(
                (float)(axis.X * sine), (float)(axis.Y * sine), (float)(axis.Z * sine), (float)Math.Cos(halfAngle)));
        }

        /// <summary>Angles are radians; composition is Y * X * Z, so roll is applied first.</summary>
        public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
        {
            return Normalize(CreateFromAxisAngle(Vector3.UnitY, yaw)
                * CreateFromAxisAngle(Vector3.UnitX, pitch) * CreateFromAxisAngle(Vector3.UnitZ, roll));
        }

        /// <summary>The input must be an orthonormal rotation matrix using column vectors.</summary>
        public static Quaternion CreateFromRotationMatrix(Matrix3x3 matrix)
        {
            double x, y, z, w;
            double trace = (double)matrix.M11 + matrix.M22 + matrix.M33;
            if (trace > 0)
            {
                double scale = Math.Sqrt(trace + 1) * 2;
                w = scale / 4;
                x = ((double)matrix.M32 - matrix.M23) / scale;
                y = ((double)matrix.M13 - matrix.M31) / scale;
                z = ((double)matrix.M21 - matrix.M12) / scale;
            }
            else if (matrix.M11 > matrix.M22 && matrix.M11 > matrix.M33)
            {
                double scale = Math.Sqrt(1.0 + matrix.M11 - matrix.M22 - matrix.M33) * 2;
                w = ((double)matrix.M32 - matrix.M23) / scale;
                x = scale / 4;
                y = ((double)matrix.M12 + matrix.M21) / scale;
                z = ((double)matrix.M13 + matrix.M31) / scale;
            }
            else if (matrix.M22 > matrix.M33)
            {
                double scale = Math.Sqrt(1.0 + matrix.M22 - matrix.M11 - matrix.M33) * 2;
                w = ((double)matrix.M13 - matrix.M31) / scale;
                x = ((double)matrix.M12 + matrix.M21) / scale;
                y = scale / 4;
                z = ((double)matrix.M23 + matrix.M32) / scale;
            }
            else
            {
                double scale = Math.Sqrt(1.0 + matrix.M33 - matrix.M11 - matrix.M22) * 2;
                w = ((double)matrix.M21 - matrix.M12) / scale;
                x = ((double)matrix.M13 + matrix.M31) / scale;
                y = ((double)matrix.M23 + matrix.M32) / scale;
                z = scale / 4;
            }
            return Normalize(new Quaternion((float)x, (float)y, (float)z, (float)w));
        }

        public Vector3 Rotate(Vector3 vector)
        {
            return Matrix3x3.CreateRotation(this) * vector;
        }

        public static Quaternion Lerp(Quaternion from, Quaternion to, float amount)
        {
            from = Normalize(from);
            to = Normalize(to);
            amount = MathUtil.Clamp(amount, 0, 1);
            if (Dot(from, to) < 0)
            {
                to = -to;
            }
            return Normalize(new Quaternion(
                (float)((1.0 - amount) * from.X + (double)amount * to.X),
                (float)((1.0 - amount) * from.Y + (double)amount * to.Y),
                (float)((1.0 - amount) * from.Z + (double)amount * to.Z),
                (float)((1.0 - amount) * from.W + (double)amount * to.W)));
        }

        public static Quaternion Slerp(Quaternion from, Quaternion to, float amount)
        {
            from = Normalize(from);
            to = Normalize(to);
            amount = MathUtil.Clamp(amount, 0, 1);
            double dot = Dot(from, to);
            if (dot < 0)
            {
                to = -to;
                dot = -dot;
            }
            dot = Math.Min(1, dot);
            if (dot > 0.9995)
            {
                return Lerp(from, to, amount);
            }
            double angle = Math.Acos(dot);
            double inverseSine = 1.0 / Math.Sin(angle);
            double fromWeight = Math.Sin((1.0 - amount) * angle) * inverseSine;
            double toWeight = Math.Sin(amount * angle) * inverseSine;
            return Normalize(new Quaternion(
                (float)(fromWeight * from.X + toWeight * to.X), (float)(fromWeight * from.Y + toWeight * to.Y),
                (float)(fromWeight * from.Z + toWeight * to.Z), (float)(fromWeight * from.W + toWeight * to.W)));
        }

        /// <summary>Shortest angular distance in radians, including stable behavior near identical rotations.</summary>
        public static float Angle(Quaternion left, Quaternion right)
        {
            var difference = Conjugate(Normalize(left)) * Normalize(right);
            double imaginaryLength = Math.Sqrt((double)difference.X * difference.X
                + (double)difference.Y * difference.Y + (double)difference.Z * difference.Z);
            return (float)(2 * Math.Atan2(imaginaryLength, Math.Abs(difference.W)));
        }

        public static bool Approximately(Quaternion left, Quaternion right, float tolerance = 1e-5f)
        {
            return MathUtil.Approximately(left.X, right.X, tolerance) && MathUtil.Approximately(left.Y, right.Y, tolerance)
                && MathUtil.Approximately(left.Z, right.Z, tolerance) && MathUtil.Approximately(left.W, right.W, tolerance);
        }

        /// <summary>Hamilton product: left * right applies right first.</summary>
        public static Quaternion operator *(Quaternion left, Quaternion right)
        {
            return new Quaternion(
                (float)((double)left.W * right.X + (double)left.X * right.W + (double)left.Y * right.Z - (double)left.Z * right.Y),
                (float)((double)left.W * right.Y - (double)left.X * right.Z + (double)left.Y * right.W + (double)left.Z * right.X),
                (float)((double)left.W * right.Z + (double)left.X * right.Y - (double)left.Y * right.X + (double)left.Z * right.W),
                (float)((double)left.W * right.W - (double)left.X * right.X - (double)left.Y * right.Y - (double)left.Z * right.Z));
        }

        public static Vector3 operator *(Quaternion rotation, Vector3 vector) => rotation.Rotate(vector);
        public static Quaternion operator -(Quaternion value) => new Quaternion(-value.X, -value.Y, -value.Z, -value.W);

        public bool Equals(Quaternion other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        }

        public override bool Equals(object other) => other is Quaternion value && Equals(value);
        public static bool operator ==(Quaternion left, Quaternion right) => left.Equals(right);
        public static bool operator !=(Quaternion left, Quaternion right) => !left.Equals(right);

        public override int GetHashCode()
        {
            return MathUtil.Hash(MathUtil.Hash(MathUtil.Hash(X.GetHashCode(), Y), Z), W);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2}, {3})", X, Y, Z, W);
        }
    }
}
