using System;

namespace DE.Share.Foundation.Mathematics
{
    internal static class MatrixOperations
    {
        // Reject pivots at double-roundoff scale instead of treating a residual as an independent row.
        private const double PivotTolerance = 1e-14;
        private const double ResidualTolerance = 1e-5;

        // Gauss-Jordan elimination in double precision with scaled partial pivoting.
        internal static bool TryInverse(float[] source, int size, out float[] result)
        {
            result = null;
            var augmented = new double[size, size * 2];
            var rowScales = new double[size];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    float value = source[row * size + column];
                    augmented[row, column] = value;
                    rowScales[row] = Math.Max(rowScales[row], Math.Abs((double)value));
                }
                if (rowScales[row] <= 0)
                {
                    return false;
                }
                augmented[row, row + size] = 1;
            }

            for (int column = 0; column < size; column++)
            {
                int pivotRow = column;
                double pivotRatio = Math.Abs(augmented[pivotRow, column]) / rowScales[pivotRow];
                for (int row = column + 1; row < size; row++)
                {
                    double ratio = Math.Abs(augmented[row, column]) / rowScales[row];
                    if (ratio > pivotRatio)
                    {
                        pivotRow = row;
                        pivotRatio = ratio;
                    }
                }
                if (pivotRatio <= PivotTolerance)
                {
                    return false;
                }
                if (pivotRow != column)
                {
                    double rowScale = rowScales[column];
                    rowScales[column] = rowScales[pivotRow];
                    rowScales[pivotRow] = rowScale;
                    for (int index = 0; index < size * 2; index++)
                    {
                        double temporary = augmented[column, index];
                        augmented[column, index] = augmented[pivotRow, index];
                        augmented[pivotRow, index] = temporary;
                    }
                }
                double divisor = augmented[column, column];
                for (int index = 0; index < size * 2; index++)
                {
                    augmented[column, index] /= divisor;
                }
                for (int row = 0; row < size; row++)
                {
                    if (row == column)
                    {
                        continue;
                    }
                    double factor = augmented[row, column];
                    for (int index = 0; index < size * 2; index++)
                    {
                        augmented[row, index] -= factor * augmented[column, index];
                    }
                }
            }

            var inverse = new float[source.Length];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    float value = (float)augmented[row, column + size];
                    inverse[row * size + column] = value;
                }
            }
            if (!HasSmallResidual(source, inverse, size) || !HasSmallResidual(inverse, source, size))
            {
                return false;
            }
            result = inverse;
            return true;
        }

        private static bool HasSmallResidual(float[] left, float[] right, int size)
        {
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    double value = 0;
                    double scale = 0;
                    for (int index = 0; index < size; index++)
                    {
                        double term = (double)left[row * size + index] * right[index * size + column];
                        value += term;
                        scale += Math.Abs(term);
                    }
                    double expected = row == column ? 1 : 0;
                    if (Math.Abs(value - expected) > ResidualTolerance * Math.Max(1, scale))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
