using System;
using System.Globalization;

namespace DE.Share.Data.DataProvider
{
    internal static class ExcelValueConverter
    {
        // Excel numeric cells cannot reliably preserve more than 15 decimal digits.
        private const double MaximumExactInteger = 999999999999999d;

        public static object GetInteger(object value, Type targetType)
        {
            decimal integer;
            if (value is string text)
            {
                integer = decimal.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            else
            {
                double number = GetDouble(value);
                if (Math.Truncate(number) != number)
                {
                    throw new FormatException("An integer cell must not contain a fractional value.");
                }
                if (Math.Abs(number) > MaximumExactInteger)
                {
                    throw new FormatException("Store integers with more than 15 decimal digits as text.");
                }
                integer = (decimal)number;
            }
            return Convert.ChangeType(integer, targetType, CultureInfo.InvariantCulture);
        }

        public static double GetDouble(object value)
        {
            double number;
            if (value is string text)
            {
                number = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            else if (value is double || value is float || value is int || value is uint || value is long || value is ulong || value is short || value is ushort || value is byte || value is sbyte || value is decimal)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            else
            {
                throw new FormatException("Expected a numeric cell or numeric text.");
            }
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                throw new FormatException("The floating point value must be finite.");
            }
            return number;
        }

        public static float GetSingle(object value)
        {
            double number = GetDouble(value);
            if (number > float.MaxValue || number < -float.MaxValue)
            {
                throw new OverflowException("The value is outside the Single range.");
            }
            return (float)number;
        }

        public static bool GetBoolean(object value)
        {
            if (value is bool boolean)
            {
                return boolean;
            }
            if (value is string text && bool.TryParse(text, out boolean))
            {
                return boolean;
            }
            throw new FormatException("Expected a Boolean cell or true/false text.");
        }

        public static string GetString(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is string text)
            {
                return text;
            }
            throw new FormatException("Expected a text cell.");
        }

        public static TEnum GetEnum<TEnum>(object value) where TEnum : struct, Enum
        {
            TEnum result;
            if (value is string text)
            {
                if (text.IndexOf(',') >= 0 || !Enum.TryParse(text, false, out result))
                {
                    throw new FormatException("Expected an enum member name or its defined integer value.");
                }
            }
            else
            {
                result = (TEnum)Enum.ToObject(typeof(TEnum), GetInteger(value, Enum.GetUnderlyingType(typeof(TEnum))));
            }
            if (!Enum.IsDefined(typeof(TEnum), result))
            {
                throw new FormatException($"The value is not defined by enum '{typeof(TEnum).FullName}'.");
            }
            return result;
        }
    }
}
