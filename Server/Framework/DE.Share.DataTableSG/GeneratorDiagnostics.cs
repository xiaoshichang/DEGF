using Microsoft.CodeAnalysis;

namespace DE.Share.DataTableSG
{
    internal static class GeneratorDiagnostics
    {
        public static readonly DiagnosticDescriptor InvalidRow = Create("DSG001", "Invalid data row declaration",
            "Row '{0}' must be a top-level, public, sealed, non-generic partial class directly inheriting DataRow, named '<Name>DataRow', with a non-extern parameterless constructor");
        public static readonly DiagnosticDescriptor InvalidMember = Create("DSG002", "Invalid data column member",
            "Column '{0}' must be a public instance auto-property with a public getter and private setter; fields, indexers, extern members and init accessors are unsupported");
        public static readonly DiagnosticDescriptor UnsupportedType = Create("DSG003", "Unsupported data column type",
            "Column '{0}' has unsupported type '{1}'; use int, uint, long, ulong, float, double, bool, string, enum, or a nullable value type from this list");
        public static readonly DiagnosticDescriptor HiddenKey = Create("DSG004", "The Id key cannot be hidden",
            "Row '{0}' must use the inherited DataRow.Id key without declaring another member named Id");
        public static readonly DiagnosticDescriptor InvalidAttribute = Create("DSG005", "Invalid data mapping attribute",
            "Mapping attribute on '{0}' must occur once and contain nonempty string names");
        public static readonly DiagnosticDescriptor DuplicateColumn = Create("DSG006", "Duplicate mapped column name",
            "Mapped column name '{0}' is already used in row '{1}' (including the inherited Id column)");
        public static readonly DiagnosticDescriptor InvalidSource = Create("DSG007", "Invalid relative data source",
            "Source on '{0}' must be a relative resource name without a file extension, empty segments, traversal or invalid path characters");
        public static readonly DiagnosticDescriptor DuplicateTable = Create("DSG008", "Duplicate logical table name",
            "Logical table name '{0}' is declared by multiple data rows in this compilation");
        public static readonly DiagnosticDescriptor GeneratedConflict = Create("DSG009", "Generated name conflicts with existing declaration",
            "Generated name '{0}' conflicts with an existing declaration; rename the declaration or the data row");
        public static readonly DiagnosticDescriptor InvalidPolicy = Create("DSG010", "Invalid data table policy",
            "Data policy '{0}' on row '{1}' has invalid value '{2}'; {3}");

        private static DiagnosticDescriptor Create(string id, string title, string message)
        {
            return new DiagnosticDescriptor(id, title, message, "DE.Share.DataTableSG", DiagnosticSeverity.Error, true);
        }
    }
}
