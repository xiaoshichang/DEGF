using System;
using System.Globalization;

namespace DE.Share.Foundation.Mathematics
{
    /// <summary>Row-major entries and column-vector multiplication. Matrix products apply the right operand first.</summary>
    public readonly struct Matrix3x3 : IEquatable<Matrix3x3>
    {
        public Matrix3x3(
            float m11, float m12, float m13,
            float m21, float m22, float m23,
            float m31, float m32, float m33)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M31 = m31;
            M32 = m32;
            M33 = m33;
        }

        public float M11 { get; }
        public float M12 { get; }
        public float M13 { get; }
        public float M21 { get; }
        public float M22 { get; }
        public float M23 { get; }
        public float M31 { get; }
        public float M32 { get; }
        public float M33 { get; }
        public static Matrix3x3 Zero => default;
        public static Matrix3x3 Identity => new Matrix3x3(
            1, 0, 0,
            0, 1, 0,
            0, 0, 1);
        public float Determinant => (float)DeterminantValue;
        internal double DeterminantValue =>
            (double)M11 * ((double)M22 * M33 - (double)M23 * M32)
            - (double)M12 * ((double)M21 * M33 - (double)M23 * M31)
            + (double)M13 * ((double)M21 * M32 - (double)M22 * M31);
        public Matrix3x3 Transposed => Transpose(this);

        public float this[int row, int column]
        {
            get
            {
                if (row < 0 || row >= 3)
                {
                    throw new ArgumentOutOfRangeException(nameof(row));
                }
                if (column < 0 || column >= 3)
                {
                    throw new ArgumentOutOfRangeException(nameof(column));
                }
                return (row * 3 + column) switch
                {
                    0 => M11,
                    1 => M12,
                    2 => M13,
                    3 => M21,
                    4 => M22,
                    5 => M23,
                    6 => M31,
                    7 => M32,
                    8 => M33,
                    _ => throw new ArgumentOutOfRangeException(nameof(row)),
                };
            }
        }

        public static Matrix3x3 Transpose(Matrix3x3 value)
        {
            return new Matrix3x3(
                value.M11, value.M21, value.M31,
                value.M12, value.M22, value.M32,
                value.M13, value.M23, value.M33);
        }

        public bool TryInverse(out Matrix3x3 result) => TryInverse(this, out result);

        public static bool TryInverse(Matrix3x3 value, out Matrix3x3 result)
        {
            result = Zero;
            var entries = new float[]
            {
                value.M11, value.M12, value.M13,
                value.M21, value.M22, value.M23,
                value.M31, value.M32, value.M33
            };
            if (!MatrixOperations.TryInverse(entries, 3, out var inverse))
            {
                return false;
            }
            result = new Matrix3x3(
                inverse[0], inverse[1], inverse[2],
                inverse[3], inverse[4], inverse[5],
                inverse[6], inverse[7], inverse[8]);
            return true;
        }

        public Matrix3x3 Inverse() => Inverse(this);

        public static Matrix3x3 Inverse(Matrix3x3 value)
        {
            if (!TryInverse(value, out var result))
            {
                throw new InvalidOperationException("Matrix cannot be inverted within the numerical tolerances.");
            }
            return result;
        }

        public static Matrix3x3 CreateRotation(Quaternion rotation)
        {
            rotation = Quaternion.Normalize(rotation);
            double x = rotation.X, y = rotation.Y, z = rotation.Z, w = rotation.W;
            return new Matrix3x3(
                (float)(1 - 2 * (y * y + z * z)), (float)(2 * (x * y - z * w)), (float)(2 * (x * z + y * w)),
                (float)(2 * (x * y + z * w)), (float)(1 - 2 * (x * x + z * z)), (float)(2 * (y * z - x * w)),
                (float)(2 * (x * z - y * w)), (float)(2 * (y * z + x * w)), (float)(1 - 2 * (x * x + y * y)));
        }

        public static Matrix3x3 CreateRotation(float angle)
        {
            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            return new Matrix3x3(cosine, -sine, 0, sine, cosine, 0, 0, 0, 1);
        }

        public static Matrix3x3 CreateScale(Vector3 scale)
        {
            return new Matrix3x3(scale.X, 0, 0, 0, scale.Y, 0, 0, 0, scale.Z);
        }

        public static Matrix3x3 CreateScale(Vector2 scale)
        {
            return new Matrix3x3(scale.X, 0, 0, 0, scale.Y, 0, 0, 0, 1);
        }

        public static Matrix3x3 CreateTranslation(Vector2 translation)
        {
            return new Matrix3x3(1, 0, translation.X, 0, 1, translation.Y, 0, 0, 1);
        }

        public Vector3 TransformVector(Vector3 vector) => this * vector;

        public Vector2 TransformVector(Vector2 vector)
        {
            return new Vector2(
                (float)((double)M11 * vector.X + (double)M12 * vector.Y),
                (float)((double)M21 * vector.X + (double)M22 * vector.Y));
        }

        public Vector2 TransformPoint(Vector2 point)
        {
            double w = (double)M31 * point.X + (double)M32 * point.Y + M33;
            // Homogeneous division is invariant under matrix scaling, so only exact zero is rejected.
            if (w.Equals(0d))
            {
                throw new InvalidOperationException("Homogeneous divisor must not be zero.");
            }
            return new Vector2(
                (float)(((double)M11 * point.X + (double)M12 * point.Y + M13) / w),
                (float)(((double)M21 * point.X + (double)M22 * point.Y + M23) / w));
        }

        public static Matrix3x3 operator *(Matrix3x3 left, Matrix3x3 right)
        {
            return new Matrix3x3(
                (float)((double)left.M11 * right.M11 + (double)left.M12 * right.M21 + (double)left.M13 * right.M31),
                (float)((double)left.M11 * right.M12 + (double)left.M12 * right.M22 + (double)left.M13 * right.M32),
                (float)((double)left.M11 * right.M13 + (double)left.M12 * right.M23 + (double)left.M13 * right.M33),
                (float)((double)left.M21 * right.M11 + (double)left.M22 * right.M21 + (double)left.M23 * right.M31),
                (float)((double)left.M21 * right.M12 + (double)left.M22 * right.M22 + (double)left.M23 * right.M32),
                (float)((double)left.M21 * right.M13 + (double)left.M22 * right.M23 + (double)left.M23 * right.M33),
                (float)((double)left.M31 * right.M11 + (double)left.M32 * right.M21 + (double)left.M33 * right.M31),
                (float)((double)left.M31 * right.M12 + (double)left.M32 * right.M22 + (double)left.M33 * right.M32),
                (float)((double)left.M31 * right.M13 + (double)left.M32 * right.M23 + (double)left.M33 * right.M33));
        }

        public static Vector3 operator *(Matrix3x3 matrix, Vector3 vector)
        {
            return new Vector3(
                (float)((double)matrix.M11 * vector.X + (double)matrix.M12 * vector.Y + (double)matrix.M13 * vector.Z),
                (float)((double)matrix.M21 * vector.X + (double)matrix.M22 * vector.Y + (double)matrix.M23 * vector.Z),
                (float)((double)matrix.M31 * vector.X + (double)matrix.M32 * vector.Y + (double)matrix.M33 * vector.Z));
        }

        public static Matrix3x3 operator +(Matrix3x3 left, Matrix3x3 right)
        {
            return new Matrix3x3(
                left.M11 + right.M11, left.M12 + right.M12, left.M13 + right.M13,
                left.M21 + right.M21, left.M22 + right.M22, left.M23 + right.M23,
                left.M31 + right.M31, left.M32 + right.M32, left.M33 + right.M33);
        }

        public static Matrix3x3 operator -(Matrix3x3 left, Matrix3x3 right)
        {
            return new Matrix3x3(
                left.M11 - right.M11, left.M12 - right.M12, left.M13 - right.M13,
                left.M21 - right.M21, left.M22 - right.M22, left.M23 - right.M23,
                left.M31 - right.M31, left.M32 - right.M32, left.M33 - right.M33);
        }

        public static Matrix3x3 operator -(Matrix3x3 value)
        {
            return new Matrix3x3(
                -value.M11, -value.M12, -value.M13,
                -value.M21, -value.M22, -value.M23,
                -value.M31, -value.M32, -value.M33);
        }

        public static Matrix3x3 operator *(Matrix3x3 value, float scale)
        {
            return new Matrix3x3(
                value.M11 * scale, value.M12 * scale, value.M13 * scale,
                value.M21 * scale, value.M22 * scale, value.M23 * scale,
                value.M31 * scale, value.M32 * scale, value.M33 * scale);
        }

        public static Matrix3x3 operator *(float scale, Matrix3x3 value) => value * scale;

        public static Matrix3x3 operator /(Matrix3x3 value, float divisor)
        {
            return new Matrix3x3(
                MathUtil.Divide(value.M11, divisor), MathUtil.Divide(value.M12, divisor), MathUtil.Divide(value.M13, divisor),
                MathUtil.Divide(value.M21, divisor), MathUtil.Divide(value.M22, divisor), MathUtil.Divide(value.M23, divisor),
                MathUtil.Divide(value.M31, divisor), MathUtil.Divide(value.M32, divisor), MathUtil.Divide(value.M33, divisor));
        }

        public static bool Approximately(Matrix3x3 left, Matrix3x3 right, float tolerance = 1e-5f)
        {
            return MathUtil.Approximately(left.M11, right.M11, tolerance)
                && MathUtil.Approximately(left.M12, right.M12, tolerance)
                && MathUtil.Approximately(left.M13, right.M13, tolerance)
                && MathUtil.Approximately(left.M21, right.M21, tolerance)
                && MathUtil.Approximately(left.M22, right.M22, tolerance)
                && MathUtil.Approximately(left.M23, right.M23, tolerance)
                && MathUtil.Approximately(left.M31, right.M31, tolerance)
                && MathUtil.Approximately(left.M32, right.M32, tolerance)
                && MathUtil.Approximately(left.M33, right.M33, tolerance);
        }

        public bool Equals(Matrix3x3 other)
        {
            return M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13)
                && M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23)
                && M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33);
        }

        public override bool Equals(object other) => other is Matrix3x3 value && Equals(value);
        public static bool operator ==(Matrix3x3 left, Matrix3x3 right) => left.Equals(right);
        public static bool operator !=(Matrix3x3 left, Matrix3x3 right) => !left.Equals(right);

        public override int GetHashCode()
        {
            int hash = M11.GetHashCode();
            hash = MathUtil.Hash(hash, M12);
            hash = MathUtil.Hash(hash, M13);
            hash = MathUtil.Hash(hash, M21);
            hash = MathUtil.Hash(hash, M22);
            hash = MathUtil.Hash(hash, M23);
            hash = MathUtil.Hash(hash, M31);
            hash = MathUtil.Hash(hash, M32);
            hash = MathUtil.Hash(hash, M33);
            return hash;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[{0}, {1}, {2}; {3}, {4}, {5}; {6}, {7}, {8}]",
                M11, M12, M13,
                M21, M22, M23,
                M31, M32, M33);
        }
    }
}
