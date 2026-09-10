using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DE.Share.DataTableSG
{
    internal sealed class RowModel
    {
        public RowModel(INamedTypeSymbol symbol, string tableName)
        {
            Symbol = symbol;
            TableName = tableName;
            SourceName = tableName;
            SheetName = tableName;
        }

        public INamedTypeSymbol Symbol { get; }
        public string TableName { get; }
        public string SourceName { get; set; }
        public string SheetName { get; set; }
        public int Load { get; set; }
        public int Cache { get; set; }
        public int CacheCapacity { get; set; } = 1024;
        public string TableTypeName => Symbol.Name.Substring(0, Symbol.Name.Length - "DataRow".Length) + "DataTable";
        public List<ColumnModel> Columns { get; } = new List<ColumnModel>();
    }

    internal sealed class ColumnModel
    {
        public ColumnModel(string propertyName, string columnName, string kind, string typeName, bool isNullable, bool isEnum)
        {
            PropertyName = propertyName;
            ColumnName = columnName;
            Kind = kind;
            TypeName = typeName;
            IsNullable = isNullable;
            IsEnum = isEnum;
        }

        public string PropertyName { get; }
        public string ColumnName { get; }
        public string Kind { get; }
        public string TypeName { get; }
        public bool IsNullable { get; }
        public bool IsEnum { get; }
    }
}
