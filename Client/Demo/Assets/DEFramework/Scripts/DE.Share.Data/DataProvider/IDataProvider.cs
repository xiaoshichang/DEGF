using System;
using DE.Share.Data.DataDescribe;

namespace DE.Share.Data.DataProvider
{
    public interface IDataProvider : IDisposable
    {
        // The provider owns the reader. The first open positions it before the first data row.
        // Repeated opens for the same binding return that reader without changing its position.
        IDataTableReader OpenTable(string rootDirectory, DataTableDescribe describe);
    }
}
