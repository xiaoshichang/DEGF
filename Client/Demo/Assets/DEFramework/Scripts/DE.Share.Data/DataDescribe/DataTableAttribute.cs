using System;

namespace DE.Share.Data
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DataTableAttribute : Attribute
    {
        public DataTableAttribute(string tableName)
        {
            TableName = tableName;
        }

        public string TableName { get; }
        public string Source { get; set; }
        public string Sheet { get; set; }
    }
}
