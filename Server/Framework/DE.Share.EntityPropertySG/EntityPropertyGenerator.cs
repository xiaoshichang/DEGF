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
        private static readonly DiagnosticDescriptor InvalidTargetTypeDescriptor = new DiagnosticDescriptor(
            id: "DEEP001",
            title: "EntityProperty target must derive from its module's entity or component base",
            messageFormat: "Field '{0}' must be declared inside a type deriving from its module's entity or component base",
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

        private static readonly DiagnosticDescriptor UnsupportedTypeDescriptor = new DiagnosticDescriptor(
            id: "DEEP007",
            title: "EntityProperty field type is unsupported",
            messageFormat: "Field '{0}' has unsupported entity property type '{1}'",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor InvalidFlagsDescriptor = new DiagnosticDescriptor(
            id: "DEEP009",
            title: "ClientOnly cannot be combined with server or public entity property flags",
            messageFormat: "Field '{0}' combines ClientOnly with ServerOnly, ClientServer, or AllClients",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor InvalidComponentDescriptor = new DiagnosticDescriptor(
            id: "DEEP010",
            title: "Entity component declaration is invalid",
            messageFormat: "Property '{0}' must be a readable instance component property on an entity from the same module",
            category: "EntityProperty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor HandwrittenPropertyDescriptor = new DiagnosticDescriptor(
            id: "DEEP011",
            title: "EntityProperty declarations must use backing fields",
            messageFormat: "Property '{0}' cannot use [EntityProperty]; annotate a private field with a '__' prefix instead",
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

            var fieldsByType = new Dictionary<INamedTypeSymbol, List<FieldGenerationInfo>>(SymbolEqualityComparer.Default);
            foreach (var variableSyntax in syntaxReceiver.CandidateFields)
            {
                var semanticModel = context.Compilation.GetSemanticModel(variableSyntax.SyntaxTree);
                if (!(semanticModel.GetDeclaredSymbol(variableSyntax) is IFieldSymbol fieldSymbol))
                {
                    continue;
                }

                var attributeData = EntityPropertySymbols.GetAttribute(fieldSymbol);
                if (attributeData == null)
                {
                    continue;
                }

                if (!TryValidateField(context, fieldSymbol, out var propertyName))
                {
                    continue;
                }

                if (!EntityPropertySerializationEmitter.TryGetCodec(fieldSymbol.Type, out var codec))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedTypeDescriptor, fieldSymbol.Locations.FirstOrDefault(), fieldSymbol.Name, fieldSymbol.Type.ToDisplayString()));
                    continue;
                }

                byte flags = EntityPropertySerializationEmitter.GetFlags(attributeData);
                if ((flags & 2) != 0 && (flags & (1 | 4 | 8)) != 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidFlagsDescriptor, fieldSymbol.Locations.FirstOrDefault(), fieldSymbol.Name));
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
                        attributeData.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BuildSerializationTraitExpressions(attributeData),
                        flags,
                        codec
                    )
                );
            }

            var nodesByType = CollectComponentNodes(context, syntaxReceiver.CandidateProperties, fieldsByType);
            AddRootTypes(context, syntaxReceiver.CandidateTypes, fieldsByType);
            var allTypes = fieldsByType.Keys
                .OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal)
                .ToList();

            foreach (var typeSymbol in allTypes)
            {
                IReadOnlyList<IPropertySymbol> nodes = nodesByType.TryGetValue(typeSymbol, out var declaredNodes)
                    ? declaredNodes.OrderBy(node => node.Name, StringComparer.Ordinal).ToList() : Array.Empty<IPropertySymbol>();
                EntityPropertySerializationEmitter.ReportHashCollisions(context, typeSymbol, fieldsByType[typeSymbol], nodes);
                var source = GenerateSource(typeSymbol, fieldsByType[typeSymbol], nodes);
                context.AddSource(
                    SanitizeHintName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) + ".EntityProperty.g.cs",
                    SourceText.From(source, Encoding.UTF8)
                );
            }
        }

        private static Dictionary<INamedTypeSymbol, List<IPropertySymbol>> CollectComponentNodes(
            GeneratorExecutionContext context,
            IEnumerable<BasePropertyDeclarationSyntax> properties,
            Dictionary<INamedTypeSymbol, List<FieldGenerationInfo>> fieldsByType)
        {
            var nodes = new Dictionary<INamedTypeSymbol, List<IPropertySymbol>>(SymbolEqualityComparer.Default);
            foreach (var declaration in properties)
            {
                var model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(model.GetDeclaredSymbol(declaration) is IPropertySymbol property))
                {
                    continue;
                }

                bool handwrittenLeaf = declaration.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
                    model.GetSymbolInfo(attribute).Symbol is IMethodSymbol constructor
                    && EntityPropertySymbols.IsPropertyAttribute(constructor.ContainingType));
                if (handwrittenLeaf)
                {
                    context.ReportDiagnostic(Diagnostic.Create(HandwrittenPropertyDescriptor, property.Locations.FirstOrDefault(), property.Name));
                    continue;
                }

                var attribute = EntityPropertySymbols.GetComponentAttribute(property);
                if (attribute == null)
                {
                    continue;
                }

                var module = EntityPropertySymbols.GetRuntimeModule(property.ContainingType);
                if (property.IsStatic || property.IsIndexer || property.GetMethod == null
                    || !EntityPropertySymbols.IsEntity(property.ContainingType, module)
                    || !(property.Type is INamedTypeSymbol component) || !EntityPropertySymbols.IsComponent(component, module))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidComponentDescriptor, property.Locations.FirstOrDefault(), property.Name));
                    continue;
                }

                if (!AllContainingTypesArePartial(property.ContainingType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        TypeMustBePartialDescriptor, property.Locations.FirstOrDefault(), property.ContainingType.ToDisplayString()));
                    continue;
                }

                if (!nodes.TryGetValue(property.ContainingType, out var declaredNodes))
                {
                    declaredNodes = new List<IPropertySymbol>();
                    nodes.Add(property.ContainingType, declaredNodes);
                }

                declaredNodes.Add(property);
                if (!fieldsByType.ContainsKey(property.ContainingType))
                {
                    fieldsByType.Add(property.ContainingType, new List<FieldGenerationInfo>());
                }
            }

            return nodes;
        }

        private static void AddRootTypes(
            GeneratorExecutionContext context,
            IEnumerable<TypeDeclarationSyntax> declarations,
            Dictionary<INamedTypeSymbol, List<FieldGenerationInfo>> fieldsByType)
        {
            foreach (var declaration in declarations)
            {
                var model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type) || !EntityPropertySymbols.IsRoot(type)
                    || fieldsByType.ContainsKey(type))
                {
                    continue;
                }

                if (!AllContainingTypesArePartial(type))
                {
                    context.ReportDiagnostic(Diagnostic.Create(TypeMustBePartialDescriptor, type.Locations.FirstOrDefault(), type.ToDisplayString()));
                    continue;
                }

                fieldsByType.Add(type, new List<FieldGenerationInfo>());
            }
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
            out string propertyName
        )
        {
            propertyName = string.Empty;
            var location = fieldSymbol.Locations.FirstOrDefault();

            var module = EntityPropertySymbols.GetRuntimeModule(fieldSymbol.ContainingType);
            if (!EntityPropertySymbols.IsEntityOrComponent(fieldSymbol.ContainingType, module))
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
            if (propertyName == "WriteSnapshot" || propertyName == "WriteProperty" || propertyName == "ReadProperty" || propertyName == "ValidateSnapshot"
                || propertyName == "HasSnapshotProperties"
                || propertyName == "ValidatePropertyTree" || propertyName == "TryGetComponentPropertyHash")
            {
                return true;
            }

            for (var current = typeSymbol; current != null; current = current.BaseType)
            {
                if (current.GetMembers(propertyName).Length > 0)
                {
                    return true;
                }

                if (!SymbolEqualityComparer.Default.Equals(current, typeSymbol)
                    && current.GetMembers("__" + propertyName).OfType<IFieldSymbol>()
                        .Any(field => EntityPropertySymbols.GetAttribute(field) != null))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GenerateSource(
            INamedTypeSymbol typeSymbol,
            IReadOnlyCollection<FieldGenerationInfo> fieldInfos,
            IReadOnlyList<IPropertySymbol> nodes
        )
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
                if (currentType.TypeParameters.Length > 0)
                {
                    builder.Append('<').Append(string.Join(", ", currentType.TypeParameters.Select(parameter => parameter.Name))).Append('>');
                }

                if (SymbolEqualityComparer.Default.Equals(currentType, typeSymbol) && EntityPropertySymbols.IsRoot(typeSymbol))
                {
                    builder.Append(" : global::").Append(EntityPropertySymbols.GetRuntimeModule(typeSymbol)).Append(".IEntityPropertyNode");
                }
                builder.AppendLine();
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("{");
                indentLevel++;
            }

            var sortedFields = fieldInfos.OrderBy(info => info.PropertyName, StringComparer.Ordinal).ToList();
            EntityPropertySerializationEmitter.Append(builder, typeSymbol, sortedFields, nodes, indentLevel);
            foreach (var fieldInfo in sortedFields)
            {
                builder.Append(new string(' ', indentLevel * 4));
                builder.Append('[');
                builder.Append(fieldInfo.AttributeTypeName);
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
                EntityPropertySerializationEmitter.AppendSetter(builder, typeSymbol, fieldInfo, indentLevel + 2);
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("}");

                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("}");
                builder.AppendLine();

                builder.Append(new string(' ', indentLevel * 4));
                builder.Append("partial void OnBefore");
                builder.Append(fieldInfo.PropertyName);
                builder.Append("Change(");
                builder.Append(fieldInfo.TypeName);
                builder.AppendLine(" value);");
                builder.Append(new string(' ', indentLevel * 4));
                builder.Append("partial void OnAfter");
                builder.Append(fieldInfo.PropertyName);
                builder.Append("Change(");
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

        internal readonly struct FieldGenerationInfo
        {
            public FieldGenerationInfo(
                IFieldSymbol fieldSymbol,
                string propertyName,
                string typeName,
                string attributeTypeName,
                IReadOnlyList<string> serializationTraitExpressions,
                byte flags,
                EntityPropertySerializationEmitter.PropertyCodec codec
            )
            {
                FieldSymbol = fieldSymbol;
                PropertyName = propertyName;
                TypeName = typeName;
                AttributeTypeName = attributeTypeName;
                SerializationTraitExpressions = serializationTraitExpressions;
                Flags = flags;
                Codec = codec;
            }

            public IFieldSymbol FieldSymbol { get; }
            public string PropertyName { get; }
            public string TypeName { get; }
            public string AttributeTypeName { get; }
            public IReadOnlyList<string> SerializationTraitExpressions { get; }
            public byte Flags { get; }
            public EntityPropertySerializationEmitter.PropertyCodec Codec { get; }
        }

        private sealed class SyntaxReceiver : ISyntaxReceiver
        {
            public List<VariableDeclaratorSyntax> CandidateFields { get; } = new List<VariableDeclaratorSyntax>();
            public List<TypeDeclarationSyntax> CandidateTypes { get; } = new List<TypeDeclarationSyntax>();
            public List<BasePropertyDeclarationSyntax> CandidateProperties { get; } = new List<BasePropertyDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is TypeDeclarationSyntax declaration)
                {
                    CandidateTypes.Add(declaration);
                }

                if (syntaxNode is BasePropertyDeclarationSyntax property && property.AttributeLists.Count > 0)
                {
                    CandidateProperties.Add(property);
                }

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
