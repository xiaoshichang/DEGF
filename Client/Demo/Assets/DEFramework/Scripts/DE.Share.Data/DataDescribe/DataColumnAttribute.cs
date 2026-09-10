using System;

namespace DE.Share.Data
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class DataColumnAttribute : Attribute
    {
        public DataColumnAttribute(string columnName)
        {
            ColumnName = columnName;
        }

        public string ColumnName { get; }
    }
}
