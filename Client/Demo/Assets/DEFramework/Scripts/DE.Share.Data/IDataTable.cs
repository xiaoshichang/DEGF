using DE.Share.Data.DataDescribe;

namespace DE.Share.Data
{
    public interface IDataTable
    {
        DataTableDescribe Describe { get; }
        int Count { get; }
        int CachedRowCount { get; }
    }
}
