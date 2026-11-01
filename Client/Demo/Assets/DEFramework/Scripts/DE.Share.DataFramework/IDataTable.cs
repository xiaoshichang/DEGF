using DE.Share.DataFramework.DataDescribe;

namespace DE.Share.DataFramework
{
    public interface IDataTable
    {
        DataTableDescribe Describe { get; }
        int Count { get; }
        int CachedRowCount { get; }
    }
}
