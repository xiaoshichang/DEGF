using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DE.Share.EntityPropertySG
{
    [Generator]
    public sealed class EntityPropertyGenerator : ISourceGenerator
    {
        private const string AttributeFullName = "DE.Share.Entities.EntityPropertyAttribute";
        private const string EntityFullName = "DE.Share.Entities.Entity";

        private static readonly DiagnosticDescriptor InvalidTargetTypeDescriptor = new DiagnosticDescriptor(
            id: "DEEP001",
            title: "EntityProperty target must derive from Entity",
            messageFormat: "Field '{0}' must be declared inside a type deriving from DE.Share.Entities.Entity",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor InvalidAccessibilityDescriptor = new DiagnosticDescriptor(
            id: "DEEP002",
            title: "EntityProperty field must be private",
            messageFormat: "Field '{0}' must be private to use [EntityProperty]",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor InvalidNameDescriptor = new DiagnosticDescriptor(
            id: "DEEP003",
            title: "EntityProperty field name is invalid",
            messageFormat: "Field '{0}' must start with '__' and leave a valid property name suffix",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor TypeMustBePartialDescriptor = new DiagnosticDescriptor(
            id: "DEEP004",
            title: "EntityProperty type must be partial",
            messageFormat: "Type '{0}' and all containing types must be partial to use [EntityProperty]",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor UnsupportedModifierDescriptor = new DiagnosticDescriptor(
            id: "DEEP005",
            title: "EntityProperty field modifier is unsupported",
            messageFormat: "Field '{0}' cannot be static, const, or readonly when using [EntityProperty]",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor NameConflictDescriptor = new DiagnosticDescriptor(
            id: "DEEP006",
            title: "Generated EntityProperty conflicts with an existing member",
            messageFormat: "Cannot generate property '{1}' for field '{0}' because the containing type already defines a member with that name",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(static () => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is SyntaxReceiver syntaxReceiver))
            {
                return;
            }

            var entitySymbol = context.Compilation.GetTypeByMetadataName(EntityFullName);
            var attributeSymbol = context.Compilation.GetTypeByMetadataName(AttributeFullName);
            if (entitySymbol == null || attributeSymbol == null)
            {
                return;
            }

            var fieldsByType = new Dictionary<INamedTypeSymbol, List<FieldGenerationInfo>>(SymbolEqualityComparer.Default);
            foreach (var variableSyntax in syntaxReceiver.CandidateFields)
            {
                var semanticModel = context.Compilation.GetSemanticModel(variableSyntax.SyntaxTree);
                if (!(semanticModel.GetDeclaredSymbol(variableSyntax) is IFieldSymbol fieldSymbol))
                {
                    continue;
                }

                if (!TryGetEntityPropertyAttribute(fieldSymbol, attributeSymbol, out var attributeData) || attributeData == null)
                {
                    continue;
                }

                if (!TryValidateField(context, fieldSymbol, entitySymbol, out var propertyName))
                {
                    continue;
                }

                var containingType = fieldSymbol.ContainingType;
                if (!fieldsByType.TryGetValue(containingType, out var fieldInfos))
                {
                    fieldInfos = new List<FieldGenerationInfo>();
                    fieldsByType.Add(containingType, fieldInfos);
                }

                fieldInfos.Add(
                    new FieldGenerationInfo(
                        fieldSymbol,
                        propertyName,
                        fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BuildSerializationTraitExpressions(attributeData)
                    )
                );
            }

            foreach (var pair in fieldsByType.OrderBy(entry => entry.Key.ToDisplayString(), StringComparer.Ordinal))
            {
                var source = GenerateSource(pair.Key, pair.Value);
                context.AddSource(
                    SanitizeHintName(pair.Key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) + ".EntityProperty.g.cs",
                    SourceText.From(source, Encoding.UTF8)
                );
            }
        }

        private static bool TryGetEntityPropertyAttribute(
            IFieldSymbol fieldSymbol,
            INamedTypeSymbol attributeSymbol,
            out AttributeData? attributeData
        )
        {
            attributeData = fieldSymbol
                .GetAttributes()
                .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
            return attributeData != null;
        }

        private static IReadOnlyList<string> BuildSerializationTraitExpressions(AttributeData attributeData)
        {
            var expressions = new List<string>();
            foreach (var constructorArgument in attributeData.ConstructorArguments)
            {
                AppendTypedConstantExpressions(expressions, constructorArgument);
            }

            return expressions;
        }

        private static void AppendTypedConstantExpressions(List<string> expressions, TypedConstant typedConstant)
        {
            if (typedConstant.Kind == TypedConstantKind.Array)
            {
                foreach (var value in typedConstant.Values)
                {
                    AppendTypedConstantExpressions(expressions, value);
                }

                return;
            }

            if (typedConstant.Kind == TypedConstantKind.Error || typedConstant.Type == null || typedConstant.Value == null)
            {
                return;
            }

            expressions.Add(BuildTypedConstantExpression(typedConstant.Type, typedConstant.Value));
        }

        private static string BuildTypedConstantExpression(ITypeSymbol typeSymbol, object value)
        {
            var valueLiteral = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);

            if (typeSymbol.TypeKind != TypeKind.Enum)
            {
                return valueLiteral;
            }

            return "(" + typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" + valueLiteral;
        }

        private static bool TryValidateField(
            GeneratorExecutionContext context,
            IFieldSymbol fieldSymbol,
            INamedTypeSymbol entitySymbol,
            out string propertyName
        )
        {
            propertyName = string.Empty;
            var location = fieldSymbol.Locations.FirstOrDefault();

            if (!InheritsFromEntity(fieldSymbol.ContainingType, entitySymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidTargetTypeDescriptor, location, fieldSymbol.Name));
                return false;
            }

            if (fieldSymbol.DeclaredAccessibility != Accessibility.Private)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidAccessibilityDescriptor, location, fieldSymbol.Name));
                return false;
            }

            if (fieldSymbol.IsStatic || fieldSymbol.IsConst || fieldSymbol.IsReadOnly)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedModifierDescriptor, location, fieldSymbol.Name));
                return false;
            }

            if (!TryBuildPropertyName(fieldSymbol.Name, out propertyName))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidNameDescriptor, location, fieldSymbol.Name));
                return false;
            }

            if (!AllContainingTypesArePartial(fieldSymbol.ContainingType))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(TypeMustBePartialDescriptor, location, fieldSymbol.ContainingType.ToDisplayString())
                );
                return false;
            }

            if (HasConflictingMember(fieldSymbol.ContainingType, propertyName))
            {
                context.ReportDiagnostic(Diagnostic.Create(NameConflictDescriptor, location, fieldSymbol.Name, propertyName));
                return false;
            }

            return true;
        }

        private static bool InheritsFromEntity(INamedTypeSymbol typeSymbol, INamedTypeSymbol entitySymbol)
        {
            for (var current = typeSymbol; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, entitySymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBuildPropertyName(string fieldName, out string propertyName)
        {
            propertyName = string.Empty;
            if (string.IsNullOrWhiteSpace(fieldName) || !fieldName.StartsWith("__", StringComparison.Ordinal))
            {
                return false;
            }

            propertyName = fieldName.Substring(2);
            return !string.IsNullOrWhiteSpace(propertyName) && SyntaxFacts.IsValidIdentifier(propertyName);
        }

        private static bool AllContainingTypesArePartial(INamedTypeSymbol typeSymbol)
        {
            for (var current = typeSymbol; current != null; current = current.ContainingType)
            {
                if (!IsPartial(current))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPartial(INamedTypeSymbol typeSymbol)
        {
            foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is TypeDeclarationSyntax declaration
                    && declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasConflictingMember(INamedTypeSymbol typeSymbol, string propertyName)
        {
            for (var current = typeSymbol; current != null; current = current.BaseType)
            {
                if (current.GetMembers(propertyName).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GenerateSource(INamedTypeSymbol typeSymbol, IReadOnlyCollection<FieldGenerationInfo> fieldInfos)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");

            var indentLevel = 0;
            if (!typeSymbol.ContainingNamespace.IsGlobalNamespace)
            {
                builder.Append("namespace ");
                builder.Append(typeSymbol.ContainingNamespace.ToDisplayString());
                builder.AppendLine();
                builder.AppendLine("{");
                indentLevel++;
            }

            var containingTypes = new Stack<INamedTypeSymbol>();
            for (var current = typeSymbol; current != null; current = current.ContainingType)
            {
                containingTypes.Push(current);
            }

            while (containingTypes.Count > 0)
            {
                var currentType = containingTypes.Pop();
                builder.Append(new string(' ', indentLevel * 4));
                builder.Append(GetAccessibilityKeyword(currentType.DeclaredAccessibility));
                builder.Append(" partial class ");
                builder.Append(currentType.Name);
                builder.AppendLine();
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("{");
                indentLevel++;
            }

            foreach (var fieldInfo in fieldInfos.OrderBy(info => info.PropertyName, StringComparer.Ordinal))
            {
                builder.Append(new string(' ', indentLevel * 4));
                builder.Append("[global::DE.Share.Entities.EntityProperty");
                if (fieldInfo.SerializationTraitExpressions.Count > 0)
                {
                    builder.Append('(');
                    builder.Append(string.Join(", ", fieldInfo.SerializationTraitExpressions));
                    builder.Append(')');
                }

                builder.AppendLine("]");
                builder.Append(new string(' ', indentLevel * 4));
                builder.Append("public ");
                builder.Append(fieldInfo.TypeName);
                builder.Append(' ');
                builder.Append(fieldInfo.PropertyName);
                builder.AppendLine();
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("{");

                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.Append("get => ");
                builder.Append(fieldInfo.FieldSymbol.Name);
                builder.AppendLine(";");

                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("set");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("{");
                builder.Append(new string(' ', (indentLevel + 2) * 4));
                builder.Append("if (global::System.Collections.Generic.EqualityComparer<");
                builder.Append(fieldInfo.TypeName);
                builder.Append(">.Default.Equals(");
                builder.Append(fieldInfo.FieldSymbol.Name);
                builder.AppendLine(", value))");
                builder.Append(new string(' ', (indentLevel + 2) * 4));
                builder.AppendLine("{");
                builder.Append(new string(' ', (indentLevel + 3) * 4));
                builder.AppendLine("return;");
                builder.Append(new string(' ', (indentLevel + 2) * 4));
                builder.AppendLine("}");
                builder.Append(new string(' ', (indentLevel + 2) * 4));
                builder.Append("On");
                builder.Append(fieldInfo.PropertyName);
                builder.AppendLine("Changing(value);");
                builder.Append(new string(' ', (indentLevel + 2) * 4));
                builder.Append(fieldInfo.FieldSymbol.Name);
                builder.AppendLine(" = value;");
                builder.Append(new string(' ', (indentLevel + 2) * 4));
                builder.Append("On");
                builder.Append(fieldInfo.PropertyName);
                builder.AppendLine("Changed(value);");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("}");

                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("}");
                builder.AppendLine();

                builder.Append(new string(' ', indentLevel * 4));
                builder.Append("partial void On");
                builder.Append(fieldInfo.PropertyName);
                builder.Append("Changing(");
                builder.Append(fieldInfo.TypeName);
                builder.AppendLine(" value);");
                builder.Append(new string(' ', indentLevel * 4));
                builder.Append("partial void On");
                builder.Append(fieldInfo.PropertyName);
                builder.Append("Changed(");
                builder.Append(fieldInfo.TypeName);
                builder.AppendLine(" value);");
                builder.AppendLine();
            }

            while (indentLevel > 0)
            {
                indentLevel--;
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static string GetAccessibilityKeyword(Accessibility accessibility)
        {
            switch (accessibility)
            {
            case Accessibility.Public:
                return "public";
            case Accessibility.Internal:
                return "internal";
            case Accessibility.Private:
                return "private";
            case Accessibility.Protected:
                return "protected";
            case Accessibility.ProtectedAndInternal:
                return "private protected";
            case Accessibility.ProtectedOrInternal:
                return "protected internal";
            default:
                return "internal";
            }
        }

        private static string SanitizeHintName(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : '_');
            }

            return builder.ToString();
        }

        private readonly struct FieldGenerationInfo
        {
            public FieldGenerationInfo(
                IFieldSymbol fieldSymbol,
                string propertyName,
                string typeName,
                IReadOnlyList<string> serializationTraitExpressions
            )
            {
                FieldSymbol = fieldSymbol;
                PropertyName = propertyName;
                TypeName = typeName;
                SerializationTraitExpressions = serializationTraitExpressions;
            }

            public IFieldSymbol FieldSymbol { get; }
            public string PropertyName { get; }
            public string TypeName { get; }
            public IReadOnlyList<string> SerializationTraitExpressions { get; }
        }

        private sealed class SyntaxReceiver : ISyntaxReceiver
        {
            public List<VariableDeclaratorSyntax> CandidateFields { get; } = new List<VariableDeclaratorSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (!(syntaxNode is FieldDeclarationSyntax fieldDeclaration) || fieldDeclaration.AttributeLists.Count == 0)
                {
                    return;
                }

                foreach (var variable in fieldDeclaration.Declaration.Variables)
                {
                    CandidateFields.Add(variable);
                }
            }
        }
    }
}
