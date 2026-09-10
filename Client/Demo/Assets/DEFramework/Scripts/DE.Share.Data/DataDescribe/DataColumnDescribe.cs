using System;
using System.Collections.Generic;
using System.Globalization;

namespace DE.Share.Data.DataDescribe
{
    public sealed class DataColumnDescribe
    {
        public DataColumnDescribe(string propertyName, string columnName, DataValueKind valueKind,
            bool isNullable = false, bool isKey = false, Type enumType = null)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw new ArgumentException("A property name is required.", nameof(propertyName));
            }
            if (string.IsNullOrWhiteSpace(columnName))
            {
                throw new ArgumentException("A column name is required.", nameof(columnName));
            }
            if (!Enum.IsDefined(typeof(DataValueKind), valueKind))
            {
                throw new ArgumentOutOfRangeException(nameof(valueKind));
            }
            if ((valueKind == DataValueKind.Enum) != (enumType != null) || (enumType != null && !enumType.IsEnum))
            {
                throw new ArgumentException("Enum columns require an enum type; other columns must not specify one.", nameof(enumType));
            }
            if (isKey && (propertyName != "Id" || valueKind != DataValueKind.Int32 || isNullable))
            {
                throw new ArgumentException("The key must be the non-nullable Int32 Id column.", nameof(isKey));
            }

            PropertyName = propertyName;
            ColumnName = columnName;
            ValueKind = valueKind;
            IsNullable = isNullable;
            IsKey = isKey;
            EnumType = enumType;
            var names = enumType == null ? Array.Empty<string>() : Enum.GetNames(enumType);
            var values = new string[names.Length];
            if (enumType != null)
            {
                EnumUnderlyingType = Enum.GetUnderlyingType(enumType);
                for (int index = 0; index < names.Length; index++)
                {
                    values[index] = Convert.ToString(Convert.ChangeType(Enum.Parse(enumType, names[index]), EnumUnderlyingType, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                }
            }
            EnumNames = Array.AsReadOnly(names);
            EnumValues = Array.AsReadOnly(values);
        }

        public string PropertyName { get; }
        public string ColumnName { get; }
        public DataValueKind ValueKind { get; }
        public bool IsNullable { get; }
        public bool IsKey { get; }
        public Type EnumType { get; }
        public Type EnumUnderlyingType { get; }
        public IReadOnlyList<string> EnumNames { get; }
        public IReadOnlyList<string> EnumValues { get; }
    }
}
