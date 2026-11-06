using System;
using System.Globalization;

namespace DE.Share.Foundation.Mathematics
{
    /// <summary>Row-major entries and column-vector multiplication. Matrix products apply the right operand first.</summary>
    public readonly struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        public Matrix4x4(
            float m11, float m12, float m13, float m14,
            float m21, float m22, float m23, float m24,
            float m31, float m32, float m33, float m34,
            float m41, float m42, float m43, float m44)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M14 = m14;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M24 = m24;
            M31 = m31;
            M32 = m32;
            M33 = m33;
            M34 = m34;
            M41 = m41;
            M42 = m42;
            M43 = m43;
            M44 = m44;
        }

        public float M11 { get; }
        public float M12 { get; }
        public float M13 { get; }
        public float M14 { get; }
        public float M21 { get; }
        public float M22 { get; }
        public float M23 { get; }
        public float M24 { get; }
        public float M31 { get; }
        public float M32 { get; }
        public float M33 { get; }
        public float M34 { get; }
        public float M41 { get; }
        public float M42 { get; }
        public float M43 { get; }
        public float M44 { get; }
        public static Matrix4x4 Zero => default;
        public static Matrix4x4 Identity => new Matrix4x4(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        public float Determinant => (float)DeterminantValue;
        internal double DeterminantValue =>
            (double)M11 * new Matrix3x3(
                M22, M23, M24,
                M32, M33, M34,
                M42, M43, M44).DeterminantValue
            - (double)M12 * new Matrix3x3(
                M21, M23, M24,
                M31, M33, M34,
                M41, M43, M44).DeterminantValue
            + (double)M13 * new Matrix3x3(
                M21, M22, M24,
                M31, M32, M34,
                M41, M42, M44).DeterminantValue
            - (double)M14 * new Matrix3x3(
                M21, M22, M23,
                M31, M32, M33,
                M41, M42, M43).DeterminantValue;
        public Matrix4x4 Transposed => Transpose(this);

        public float this[int row, int column]
        {
            get
            {
                if (row < 0 || row >= 4)
                {
                    throw new ArgumentOutOfRangeException(nameof(row));
                }
                if (column < 0 || column >= 4)
                {
                    throw new ArgumentOutOfRangeException(nameof(column));
                }
                return (row * 4 + column) switch
                {
                    0 => M11,
                    1 => M12,
                    2 => M13,
                    3 => M14,
                    4 => M21,
                    5 => M22,
                    6 => M23,
                    7 => M24,
                    8 => M31,
                    9 => M32,
                    10 => M33,
                    11 => M34,
                    12 => M41,
                    13 => M42,
                    14 => M43,
                    15 => M44,
                    _ => throw new ArgumentOutOfRangeException(nameof(row)),
                };
            }
        }

        public static Matrix4x4 Transpose(Matrix4x4 value)
        {
            return new Matrix4x4(
                value.M11, value.M21, value.M31, value.M41,
                value.M12, value.M22, value.M32, value.M42,
                value.M13, value.M23, value.M33, value.M43,
                value.M14, value.M24, value.M34, value.M44);
        }

        public bool TryInverse(out Matrix4x4 result) => TryInverse(this, out result);

        public static bool TryInverse(Matrix4x4 value, out Matrix4x4 result)
        {
            result = Zero;
            var entries = new float[]
            {
                value.M11, value.M12, value.M13, value.M14,
                value.M21, value.M22, value.M23, value.M24,
                value.M31, value.M32, value.M33, value.M34,
                value.M41, value.M42, value.M43, value.M44
            };
            if (!MatrixOperations.TryInverse(entries, 4, out var inverse))
            {
                return false;
            }
            result = new Matrix4x4(
                inverse[0], inverse[1], inverse[2], inverse[3],
                inverse[4], inverse[5], inverse[6], inverse[7],
                inverse[8], inverse[9], inverse[10], inverse[11],
                inverse[12], inverse[13], inverse[14], inverse[15]);
            return true;
        }

        public Matrix4x4 Inverse() => Inverse(this);

        public static Matrix4x4 Inverse(Matrix4x4 value)
        {
            if (!TryInverse(value, out var result))
            {
                throw new InvalidOperationException("Matrix cannot be inverted within the numerical tolerances.");
            }
            return result;
        }

        public static Matrix4x4 CreateTranslation(Vector3 translation)
        {
            return new Matrix4x4(
                1, 0, 0, translation.X,
                0, 1, 0, translation.Y,
                0, 0, 1, translation.Z,
                0, 0, 0, 1);
        }

        public static Matrix4x4 CreateScale(Vector3 scale)
        {
            return new Matrix4x4(
                scale.X, 0, 0, 0,
                0, scale.Y, 0, 0,
                0, 0, scale.Z, 0,
                0, 0, 0, 1);
        }

        public static Matrix4x4 CreateScale(float scale)
        {
            return CreateScale(new Vector3(scale, scale, scale));
        }

        public static Matrix4x4 CreateRotation(Quaternion rotation)
        {
            var matrix = Matrix3x3.CreateRotation(rotation);
            return new Matrix4x4(
                matrix.M11, matrix.M12, matrix.M13, 0,
                matrix.M21, matrix.M22, matrix.M23, 0,
                matrix.M31, matrix.M32, matrix.M33, 0,
                0, 0, 0, 1);
        }

        public static Matrix4x4 TRS(Vector3 translation, Quaternion rotation, Vector3 scale)
        {
            return CreateTranslation(translation) * CreateRotation(rotation) * CreateScale(scale);
        }

        public Vector3 TransformVector(Vector3 vector)
        {
            return new Vector3(
                (float)((double)M11 * vector.X + (double)M12 * vector.Y + (double)M13 * vector.Z),
                (float)((double)M21 * vector.X + (double)M22 * vector.Y + (double)M23 * vector.Z),
                (float)((double)M31 * vector.X + (double)M32 * vector.Y + (double)M33 * vector.Z));
        }

        public Vector3 TransformPoint(Vector3 point)
        {
            double w = (double)M41 * point.X + (double)M42 * point.Y + (double)M43 * point.Z + M44;
            // Homogeneous division is invariant under matrix scaling, so only exact zero is rejected.
            if (w.Equals(0d))
            {
                throw new InvalidOperationException("Homogeneous divisor must not be zero.");
            }
            return new Vector3(
                (float)(((double)M11 * point.X + (double)M12 * point.Y + (double)M13 * point.Z + M14) / w),
                (float)(((double)M21 * point.X + (double)M22 * point.Y + (double)M23 * point.Z + M24) / w),
                (float)(((double)M31 * point.X + (double)M32 * point.Y + (double)M33 * point.Z + M34) / w));
        }

        public static Matrix4x4 operator *(Matrix4x4 left, Matrix4x4 right)
        {
            return new Matrix4x4(
                (float)((double)left.M11 * right.M11 + (double)left.M12 * right.M21 + (double)left.M13 * right.M31 + (double)left.M14 * right.M41),
                (float)((double)left.M11 * right.M12 + (double)left.M12 * right.M22 + (double)left.M13 * right.M32 + (double)left.M14 * right.M42),
                (float)((double)left.M11 * right.M13 + (double)left.M12 * right.M23 + (double)left.M13 * right.M33 + (double)left.M14 * right.M43),
                (float)((double)left.M11 * right.M14 + (double)left.M12 * right.M24 + (double)left.M13 * right.M34 + (double)left.M14 * right.M44),
                (float)((double)left.M21 * right.M11 + (double)left.M22 * right.M21 + (double)left.M23 * right.M31 + (double)left.M24 * right.M41),
                (float)((double)left.M21 * right.M12 + (double)left.M22 * right.M22 + (double)left.M23 * right.M32 + (double)left.M24 * right.M42),
                (float)((double)left.M21 * right.M13 + (double)left.M22 * right.M23 + (double)left.M23 * right.M33 + (double)left.M24 * right.M43),
                (float)((double)left.M21 * right.M14 + (double)left.M22 * right.M24 + (double)left.M23 * right.M34 + (double)left.M24 * right.M44),
                (float)((double)left.M31 * right.M11 + (double)left.M32 * right.M21 + (double)left.M33 * right.M31 + (double)left.M34 * right.M41),
                (float)((double)left.M31 * right.M12 + (double)left.M32 * right.M22 + (double)left.M33 * right.M32 + (double)left.M34 * right.M42),
                (float)((double)left.M31 * right.M13 + (double)left.M32 * right.M23 + (double)left.M33 * right.M33 + (double)left.M34 * right.M43),
                (float)((double)left.M31 * right.M14 + (double)left.M32 * right.M24 + (double)left.M33 * right.M34 + (double)left.M34 * right.M44),
                (float)((double)left.M41 * right.M11 + (double)left.M42 * right.M21 + (double)left.M43 * right.M31 + (double)left.M44 * right.M41),
                (float)((double)left.M41 * right.M12 + (double)left.M42 * right.M22 + (double)left.M43 * right.M32 + (double)left.M44 * right.M42),
                (float)((double)left.M41 * right.M13 + (double)left.M42 * right.M23 + (double)left.M43 * right.M33 + (double)left.M44 * right.M43),
                (float)((double)left.M41 * right.M14 + (double)left.M42 * right.M24 + (double)left.M43 * right.M34 + (double)left.M44 * right.M44));
        }

        public static Vector4 operator *(Matrix4x4 matrix, Vector4 vector)
        {
            return new Vector4(
                (float)((double)matrix.M11 * vector.X + (double)matrix.M12 * vector.Y + (double)matrix.M13 * vector.Z + (double)matrix.M14 * vector.W),
                (float)((double)matrix.M21 * vector.X + (double)matrix.M22 * vector.Y + (double)matrix.M23 * vector.Z + (double)matrix.M24 * vector.W),
                (float)((double)matrix.M31 * vector.X + (double)matrix.M32 * vector.Y + (double)matrix.M33 * vector.Z + (double)matrix.M34 * vector.W),
                (float)((double)matrix.M41 * vector.X + (double)matrix.M42 * vector.Y + (double)matrix.M43 * vector.Z + (double)matrix.M44 * vector.W));
        }

        public static Matrix4x4 operator +(Matrix4x4 left, Matrix4x4 right)
        {
            return new Matrix4x4(
                left.M11 + right.M11, left.M12 + right.M12, left.M13 + right.M13, left.M14 + right.M14,
                left.M21 + right.M21, left.M22 + right.M22, left.M23 + right.M23, left.M24 + right.M24,
                left.M31 + right.M31, left.M32 + right.M32, left.M33 + right.M33, left.M34 + right.M34,
                left.M41 + right.M41, left.M42 + right.M42, left.M43 + right.M43, left.M44 + right.M44);
        }

        public static Matrix4x4 operator -(Matrix4x4 left, Matrix4x4 right)
        {
            return new Matrix4x4(
                left.M11 - right.M11, left.M12 - right.M12, left.M13 - right.M13, left.M14 - right.M14,
                left.M21 - right.M21, left.M22 - right.M22, left.M23 - right.M23, left.M24 - right.M24,
                left.M31 - right.M31, left.M32 - right.M32, left.M33 - right.M33, left.M34 - right.M34,
                left.M41 - right.M41, left.M42 - right.M42, left.M43 - right.M43, left.M44 - right.M44);
        }

        public static Matrix4x4 operator -(Matrix4x4 value)
        {
            return new Matrix4x4(
                -value.M11, -value.M12, -value.M13, -value.M14,
                -value.M21, -value.M22, -value.M23, -value.M24,
                -value.M31, -value.M32, -value.M33, -value.M34,
                -value.M41, -value.M42, -value.M43, -value.M44);
        }

        public static Matrix4x4 operator *(Matrix4x4 value, float scale)
        {
            return new Matrix4x4(
                value.M11 * scale, value.M12 * scale, value.M13 * scale, value.M14 * scale,
                value.M21 * scale, value.M22 * scale, value.M23 * scale, value.M24 * scale,
                value.M31 * scale, value.M32 * scale, value.M33 * scale, value.M34 * scale,
                value.M41 * scale, value.M42 * scale, value.M43 * scale, value.M44 * scale);
        }

        public static Matrix4x4 operator *(float scale, Matrix4x4 value) => value * scale;

        public static Matrix4x4 operator /(Matrix4x4 value, float divisor)
        {
            return new Matrix4x4(
                MathUtil.Divide(value.M11, divisor), MathUtil.Divide(value.M12, divisor),
                MathUtil.Divide(value.M13, divisor), MathUtil.Divide(value.M14, divisor),
                MathUtil.Divide(value.M21, divisor), MathUtil.Divide(value.M22, divisor),
                MathUtil.Divide(value.M23, divisor), MathUtil.Divide(value.M24, divisor),
                MathUtil.Divide(value.M31, divisor), MathUtil.Divide(value.M32, divisor),
                MathUtil.Divide(value.M33, divisor), MathUtil.Divide(value.M34, divisor),
                MathUtil.Divide(value.M41, divisor), MathUtil.Divide(value.M42, divisor),
                MathUtil.Divide(value.M43, divisor), MathUtil.Divide(value.M44, divisor));
        }

        public static bool Approximately(Matrix4x4 left, Matrix4x4 right, float tolerance = 1e-5f)
        {
            return MathUtil.Approximately(left.M11, right.M11, tolerance)
                && MathUtil.Approximately(left.M12, right.M12, tolerance)
                && MathUtil.Approximately(left.M13, right.M13, tolerance)
                && MathUtil.Approximately(left.M14, right.M14, tolerance)
                && MathUtil.Approximately(left.M21, right.M21, tolerance)
                && MathUtil.Approximately(left.M22, right.M22, tolerance)
                && MathUtil.Approximately(left.M23, right.M23, tolerance)
                && MathUtil.Approximately(left.M24, right.M24, tolerance)
                && MathUtil.Approximately(left.M31, right.M31, tolerance)
                && MathUtil.Approximately(left.M32, right.M32, tolerance)
                && MathUtil.Approximately(left.M33, right.M33, tolerance)
                && MathUtil.Approximately(left.M34, right.M34, tolerance)
                && MathUtil.Approximately(left.M41, right.M41, tolerance)
                && MathUtil.Approximately(left.M42, right.M42, tolerance)
                && MathUtil.Approximately(left.M43, right.M43, tolerance)
                && MathUtil.Approximately(left.M44, right.M44, tolerance);
        }

        public bool Equals(Matrix4x4 other)
        {
            return M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13) && M14.Equals(other.M14)
                && M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23) && M24.Equals(other.M24)
                && M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33) && M34.Equals(other.M34)
                && M41.Equals(other.M41) && M42.Equals(other.M42) && M43.Equals(other.M43) && M44.Equals(other.M44);
        }

        public override bool Equals(object other) => other is Matrix4x4 value && Equals(value);
        public static bool operator ==(Matrix4x4 left, Matrix4x4 right) => left.Equals(right);
        public static bool operator !=(Matrix4x4 left, Matrix4x4 right) => !left.Equals(right);

        public override int GetHashCode()
        {
            int hash = M11.GetHashCode();
            hash = MathUtil.Hash(hash, M12);
            hash = MathUtil.Hash(hash, M13);
            hash = MathUtil.Hash(hash, M14);
            hash = MathUtil.Hash(hash, M21);
            hash = MathUtil.Hash(hash, M22);
            hash = MathUtil.Hash(hash, M23);
            hash = MathUtil.Hash(hash, M24);
            hash = MathUtil.Hash(hash, M31);
            hash = MathUtil.Hash(hash, M32);
            hash = MathUtil.Hash(hash, M33);
            hash = MathUtil.Hash(hash, M34);
            hash = MathUtil.Hash(hash, M41);
            hash = MathUtil.Hash(hash, M42);
            hash = MathUtil.Hash(hash, M43);
            hash = MathUtil.Hash(hash, M44);
            return hash;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[{0}, {1}, {2}, {3}; {4}, {5}, {6}, {7}; {8}, {9}, {10}, {11}; {12}, {13}, {14}, {15}]",
                M11, M12, M13, M14,
                M21, M22, M23, M24,
                M31, M32, M33, M34,
                M41, M42, M43, M44);
        }
    }
}
