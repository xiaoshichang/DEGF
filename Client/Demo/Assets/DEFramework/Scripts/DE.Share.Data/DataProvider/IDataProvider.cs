using DE.Share.Data.DataDescribe;

namespace DE.Share.Data.DataProvider
{
    public interface IDataProvider
    {
        IDataTableReader OpenTable(string rootDirectory, DataTableDescribe describe);
    }
}
