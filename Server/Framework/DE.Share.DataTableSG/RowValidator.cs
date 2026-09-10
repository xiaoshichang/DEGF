using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DE.Share.DataTableSG
{
    internal static class RowValidator
    {
        public static RowModel? Validate(INamedTypeSymbol symbol, AttributeData[] attributes, GeneratorExecutionContext context)
        {
            bool valid = true;
            void Report(DiagnosticDescriptor descriptor, ISymbol target, params object[] arguments)
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, target.Locations.FirstOrDefault(), arguments));
                valid = false;
            }

            var rowBase = context.Compilation.GetTypeByMetadataName("DE.Share.Data.DataRow");
            if (symbol.TypeKind != TypeKind.Class || symbol.ContainingType != null || symbol.Arity != 0
                || symbol.DeclaredAccessibility != Accessibility.Public || !symbol.IsSealed || symbol.IsStatic || symbol.IsAbstract
                || !SymbolEqualityComparer.Default.Equals(symbol.BaseType, rowBase)
                || !symbol.Name.EndsWith("DataRow", StringComparison.Ordinal) || symbol.Name.Length <= "DataRow".Length
                || !symbol.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0 && !constructor.IsExtern)
                || symbol.DeclaringSyntaxReferences.Any(reference => !(reference.GetSyntax(context.CancellationToken) is ClassDeclarationSyntax declaration)
                    || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Report(GeneratorDiagnostics.InvalidRow, symbol, symbol.Name);
                return null;
            }
            if (attributes.Length != 1 || !TryName(attributes[0], out var tableName))
            {
                Report(GeneratorDiagnostics.InvalidAttribute, symbol, symbol.Name);
                return null;
            }
            var row = new RowModel(symbol, tableName!);
            foreach (var argument in attributes[0].NamedArguments)
            {
                if (argument.Key == "Source")
                {
                    row.SourceName = argument.Value.Value as string ?? string.Empty;
                }
                else if (argument.Key == "Sheet")
                {
                    row.SheetName = argument.Value.Value as string ?? string.Empty;
                }
            }
            if (string.IsNullOrWhiteSpace(row.SheetName))
            {
                Report(GeneratorDiagnostics.InvalidAttribute, symbol, symbol.Name);
            }
            if (!IsValidSource(row.SourceName))
            {
                Report(GeneratorDiagnostics.InvalidSource, symbol, symbol.Name);
            }
            if (symbol.GetMembers("Id").Length != 0)
            {
                Report(GeneratorDiagnostics.HiddenKey, symbol, symbol.Name);
            }
            if (symbol.GetMembers(SourceEmitter.FactoryName).Length != 0)
            {
                Report(GeneratorDiagnostics.GeneratedConflict, symbol, SourceEmitter.FactoryName);
            }
            if (symbol.ContainingNamespace.GetMembers(row.TableTypeName).Any(member => member is INamespaceSymbol || member is INamedTypeSymbol type && type.Arity == 0))
            {
                Report(GeneratorDiagnostics.GeneratedConflict, symbol, row.TableTypeName);
            }

            var columnAttribute = context.Compilation.GetTypeByMetadataName("DE.Share.Data.DataColumnAttribute");
            var names = new HashSet<string>(StringComparer.Ordinal) { "Id" };
            foreach (var member in symbol.GetMembers().Where(member => !member.IsImplicitlyDeclared).OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var mappings = member.GetAttributes().Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, columnAttribute)).ToArray();
                if (member is IFieldSymbol)
                {
                    if (member.DeclaredAccessibility == Accessibility.Public || mappings.Length != 0)
                    {
                        Report(GeneratorDiagnostics.InvalidMember, member, member.Name);
                    }
                    continue;
                }
                if (!(member is IPropertySymbol property))
                {
                    continue;
                }
                if (property.Name == "Id")
                {
                    continue;
                }
                if (property.DeclaredAccessibility != Accessibility.Public && mappings.Length == 0)
                {
                    continue;
                }
                if (!IsValidProperty(property, context))
                {
                    Report(GeneratorDiagnostics.InvalidMember, member, member.Name);
                    continue;
                }
                var columnName = property.Name;
                if (mappings.Length != 0)
                {
                    if (mappings.Length != 1 || !TryName(mappings[0], out var mappedName))
                    {
                        Report(GeneratorDiagnostics.InvalidAttribute, member, member.Name);
                        continue;
                    }
                    columnName = mappedName!;
                }
                if (!names.Add(columnName))
                {
                    Report(GeneratorDiagnostics.DuplicateColumn, member, columnName, symbol.Name);
                }
                var type = property.Type;
                bool nullable = type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                if (nullable)
                {
                    type = ((INamedTypeSymbol)type).TypeArguments[0];
                }
                var kind = GetKind(type);
                if (kind == null)
                {
                    Report(GeneratorDiagnostics.UnsupportedType, member, property.Name, property.Type.ToDisplayString());
                    continue;
                }
                row.Columns.Add(new ColumnModel(property.Name, columnName, kind, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), nullable, type.TypeKind == TypeKind.Enum));
            }
            return valid ? row : null;
        }

        private static bool TryName(AttributeData attribute, out string? value)
        {
            value = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool IsValidProperty(IPropertySymbol property, GeneratorExecutionContext context)
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic || property.IsIndexer
                || property.GetMethod?.DeclaredAccessibility != Accessibility.Public || property.SetMethod?.DeclaredAccessibility != Accessibility.Private
                || property.IsExtern || property.GetMethod.IsExtern || property.SetMethod.IsExtern
                || property.SetMethod.IsInitOnly || property.RefKind != RefKind.None)
            {
                return false;
            }
            return property.DeclaringSyntaxReferences.Length == 1
                && property.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax declaration
                && declaration.ExpressionBody == null && declaration.AccessorList != null && declaration.AccessorList.Accessors.Count == 2
                && declaration.AccessorList.Accessors.All(accessor => accessor.Body == null && accessor.ExpressionBody == null && !accessor.SemicolonToken.IsMissing
                    && !accessor.Modifiers.Any(SyntaxKind.ExternKeyword)
                    && (accessor.IsKind(SyntaxKind.GetAccessorDeclaration) || accessor.IsKind(SyntaxKind.SetAccessorDeclaration)));
        }

        private static string? GetKind(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
            {
                return "Enum";
            }
            return type.SpecialType switch
            {
                SpecialType.System_Int32 => "Int32",
                SpecialType.System_UInt32 => "UInt32",
                SpecialType.System_Int64 => "Int64",
                SpecialType.System_UInt64 => "UInt64",
                SpecialType.System_Single => "Single",
                SpecialType.System_Double => "Double",
                SpecialType.System_Boolean => "Boolean",
                SpecialType.System_String => "String",
                _ => null
            };
        }

        // Keep this lexical check aligned with DataTableDescribe's platform-independent source validation.
        private static bool IsValidSource(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName) || sourceName.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|', '\0' }) >= 0)
            {
                return false;
            }
            var segments = sourceName.Replace('\\', '/').Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return !segments[segments.Length - 1].Contains(".");
        }
    }
}
