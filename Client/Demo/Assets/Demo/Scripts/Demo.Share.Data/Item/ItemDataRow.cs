using DE.Share.Data;

namespace Demo.Share.Data
{
    [DataTable("ItemData")]
    public sealed partial class ItemDataRow : DataRow
    {
        public string Name { get; private set; }

        public int StackLimit { get; private set; }
    }
}
