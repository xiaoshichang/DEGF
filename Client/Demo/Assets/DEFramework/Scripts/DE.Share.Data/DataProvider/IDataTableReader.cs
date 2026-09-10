using System;

namespace DE.Share.Data.DataProvider
{
    public interface IDataTableReader : IDisposable
    {
        string SourcePath { get; }
        string SheetName { get; }
        long RowNumber { get; }

        bool Read();
        bool IsNull(int columnIndex);
        int GetInt32(int columnIndex);
        uint GetUInt32(int columnIndex);
        long GetInt64(int columnIndex);
        ulong GetUInt64(int columnIndex);
        float GetSingle(int columnIndex);
        double GetDouble(int columnIndex);
        bool GetBoolean(int columnIndex);
        string GetString(int columnIndex);
        TEnum GetEnum<TEnum>(int columnIndex) where TEnum : struct, Enum;
    }
}
