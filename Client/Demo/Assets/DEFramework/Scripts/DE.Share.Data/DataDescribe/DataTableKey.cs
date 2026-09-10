using System;
using System.Globalization;

namespace DE.Share.Data
{
    public readonly struct DataTableKey : IEquatable<DataTableKey>
    {
        private readonly int value;

        private DataTableKey(int value)
        {
            this.value = value;
        }

        public static DataTableKey FromInt32(int value)
        {
            return new DataTableKey(value);
        }

        public int AsInt32()
        {
            return value;
        }

        public bool Equals(DataTableKey other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is DataTableKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(DataTableKey left, DataTableKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DataTableKey left, DataTableKey right)
        {
            return !left.Equals(right);
        }
    }
}
