using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DE.Share.DataTableSG
{
    [Generator]
    public sealed class DataTableGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var attributeType = context.Compilation.GetTypeByMetadataName("DE.Share.Data.DataTableAttribute");
            if (attributeType == null)
            {
                return;
            }
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var rows = new List<RowModel>();
            foreach (var tree in context.Compilation.SyntaxTrees)
            {
                var semanticModel = context.Compilation.GetSemanticModel(tree);
                foreach (var declaration in tree.GetRoot(context.CancellationToken).DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) as INamedTypeSymbol;
                    if (symbol == null || !seen.Add(symbol))
                    {
                        continue;
                    }
                    var attributes = symbol.GetAttributes().Where(item => SymbolEqualityComparer.Default.Equals(item.AttributeClass, attributeType)).ToArray();
                    if (attributes.Length == 0)
                    {
                        continue;
                    }
                    var row = RowValidator.Validate(symbol, attributes, context);
                    if (row != null)
                    {
                        rows.Add(row);
                    }
                }
            }
            var duplicates = new HashSet<string>(rows.GroupBy(row => row.TableName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.Ordinal);
            var generatedRows = rows
                .Where(row => !duplicates.Contains(row.TableName))
                .OrderBy(item => item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToList();
            foreach (var row in rows.Where(row => duplicates.Contains(row.TableName)))
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.DuplicateTable, row.Symbol.Locations.FirstOrDefault(), row.TableName));
            }
            foreach (var row in generatedRows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.AddSource(SourceEmitter.HintName(row), SourceText.From(SourceEmitter.Emit(row), Encoding.UTF8));
            }
            if (generatedRows.Count == 0)
            {
                return;
            }
            var moduleInitializerAttribute = context.Compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.ModuleInitializerAttribute"
            );
            if (moduleInitializerAttribute == null || moduleInitializerAttribute.DeclaredAccessibility != Accessibility.Public)
            {
                context.AddSource(
                    SourceEmitter.ModuleInitializerAttributeHintName,
                    SourceText.From(SourceEmitter.EmitModuleInitializerAttribute(), Encoding.UTF8)
                );
            }
            context.AddSource(
                SourceEmitter.CatalogHintName,
                SourceText.From(SourceEmitter.EmitCatalog(generatedRows), Encoding.UTF8)
            );
        }
    }
}
