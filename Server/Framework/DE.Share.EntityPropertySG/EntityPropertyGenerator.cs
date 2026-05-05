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
        private const string ServerRpcAttributeFullName = "DE.Share.Rpc.ServerRpcAttribute";
        private const string ClientRpcAttributeFullName = "DE.Share.Rpc.ClientRpcAttribute";
        private const string EntityFullName = "DE.Share.Entities.Entity";
        private const string EntityComponentFullName = "DE.Share.Entities.EntityComponent";

        private static readonly DiagnosticDescriptor InvalidTargetTypeDescriptor = new DiagnosticDescriptor(
            id: "DEEP001",
            title: "EntityProperty target must derive from Entity",
            messageFormat: "Field '{0}' must be declared inside a type deriving from DE.Share.Entities.Entity or DE.Share.Entities.EntityComponent",
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

        private static readonly DiagnosticDescriptor InvalidRpcReturnTypeDescriptor = new DiagnosticDescriptor(
            id: "DERPC001",
            title: "RPC method must return void",
            messageFormat: "RPC method '{0}' must return void",
            category: "ServerRpc",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor UnsupportedRpcParameterDescriptor = new DiagnosticDescriptor(
            id: "DERPC002",
            title: "RPC parameter type is unsupported",
            messageFormat: "RPC method '{0}' parameter '{1}' type '{2}' is not supported",
            category: "ServerRpc",
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
            var entityComponentSymbol = context.Compilation.GetTypeByMetadataName(EntityComponentFullName);
            var attributeSymbol = context.Compilation.GetTypeByMetadataName(AttributeFullName);
            var serverRpcAttributeSymbol = context.Compilation.GetTypeByMetadataName(ServerRpcAttributeFullName);
            var clientRpcAttributeSymbol = context.Compilation.GetTypeByMetadataName(ClientRpcAttributeFullName);
            if (entitySymbol == null || entityComponentSymbol == null || attributeSymbol == null)
            {
                return;
            }

            var fieldsByType = new Dictionary<INamedTypeSymbol, List<FieldGenerationInfo>>(SymbolEqualityComparer.Default);
            var rpcMethodsByType = new Dictionary<INamedTypeSymbol, List<RpcMethodGenerationInfo>>(SymbolEqualityComparer.Default);
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

                if (!TryValidateField(context, fieldSymbol, entitySymbol, entityComponentSymbol, out var propertyName))
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

            if (serverRpcAttributeSymbol != null || clientRpcAttributeSymbol != null)
            {
                foreach (var methodSyntax in syntaxReceiver.CandidateMethods)
                {
                    var semanticModel = context.Compilation.GetSemanticModel(methodSyntax.SyntaxTree);
                    if (!(semanticModel.GetDeclaredSymbol(methodSyntax) is IMethodSymbol methodSymbol))
                    {
                        continue;
                    }

                    var isServerRpc = serverRpcAttributeSymbol != null && HasAttribute(methodSymbol, serverRpcAttributeSymbol);
                    var isClientRpc = clientRpcAttributeSymbol != null && HasAttribute(methodSymbol, clientRpcAttributeSymbol);
                    if (!isServerRpc && !isClientRpc)
                    {
                        continue;
                    }

                    if (!TryValidateRpcMethod(context, methodSymbol))
                    {
                        continue;
                    }

                    var containingType = methodSymbol.ContainingType;
                    if (!rpcMethodsByType.TryGetValue(containingType, out var rpcInfos))
                    {
                        rpcInfos = new List<RpcMethodGenerationInfo>();
                        rpcMethodsByType.Add(containingType, rpcInfos);
                    }

                    rpcInfos.Add(new RpcMethodGenerationInfo(methodSymbol, isServerRpc, isClientRpc, ComputeRpcMethodId(methodSymbol)));
                }
            }

            var allTypes = fieldsByType.Keys
                .Concat(rpcMethodsByType.Keys)
                .Distinct(SymbolEqualityComparer.Default)
                .OfType<INamedTypeSymbol>()
                .OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal)
                .ToList();

            foreach (var typeSymbol in allTypes)
            {
                fieldsByType.TryGetValue(typeSymbol, out var fieldInfos);
                rpcMethodsByType.TryGetValue(typeSymbol, out var rpcMethodInfos);
                var source = GenerateSource(
                    typeSymbol,
                    fieldInfos ?? new List<FieldGenerationInfo>(),
                    rpcMethodInfos ?? new List<RpcMethodGenerationInfo>()
                );
                context.AddSource(
                    SanitizeHintName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) + ".EntityProperty.g.cs",
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

        private static bool HasAttribute(IMethodSymbol methodSymbol, INamedTypeSymbol attributeSymbol)
        {
            return methodSymbol
                .GetAttributes()
                .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
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
            INamedTypeSymbol entityComponentSymbol,
            out string propertyName
        )
        {
            propertyName = string.Empty;
            var location = fieldSymbol.Locations.FirstOrDefault();

            if (!InheritsFrom(fieldSymbol.ContainingType, entitySymbol) && !InheritsFrom(fieldSymbol.ContainingType, entityComponentSymbol))
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

        private static bool TryValidateRpcMethod(GeneratorExecutionContext context, IMethodSymbol methodSymbol)
        {
            var location = methodSymbol.Locations.FirstOrDefault();
            if (!methodSymbol.ReturnsVoid)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidRpcReturnTypeDescriptor, location, methodSymbol.Name));
                return false;
            }

            foreach (var parameter in methodSymbol.Parameters)
            {
                if (!IsSupportedRpcParameterType(parameter.Type))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            UnsupportedRpcParameterDescriptor,
                            parameter.Locations.FirstOrDefault() ?? location,
                            methodSymbol.Name,
                            parameter.Name,
                            parameter.Type.ToDisplayString()
                        )
                    );
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportedRpcParameterType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType == SpecialType.System_String
                || typeSymbol.SpecialType == SpecialType.System_Int32
                || IsEntityProxyType(typeSymbol)
                || IsEntityMailBoxType(typeSymbol);
        }

        private static string GetRpcReaderMethodName(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.SpecialType)
            {
            case SpecialType.System_String:
                return "ReadString";
            case SpecialType.System_Int32:
                return "ReadInt32";
            default:
                if (IsEntityProxyType(typeSymbol))
                {
                    return "ReadEntityProxy";
                }

                if (IsEntityMailBoxType(typeSymbol))
                {
                    return "ReadEntityMailBox";
                }

                throw new NotSupportedException("Unsupported RPC parameter type: " + typeSymbol.ToDisplayString());
            }
        }

        private static string GetRpcParameterTypeName(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.SpecialType)
            {
            case SpecialType.System_String:
                return "string";
            case SpecialType.System_Int32:
                return "int";
            default:
                if (IsEntityProxyType(typeSymbol))
                {
                    return "DE.Share.Rpc.EntityProxy";
                }

                if (IsEntityMailBoxType(typeSymbol))
                {
                    return "DE.Share.Rpc.EntityMailBox";
                }

                return typeSymbol.ToDisplayString();
            }
        }

        private static bool IsEntityProxyType(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "DE.Share.Rpc.EntityProxy", StringComparison.Ordinal)
                || string.Equals(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::DE.Share.Rpc.EntityProxy", StringComparison.Ordinal);
        }

        private static bool IsEntityMailBoxType(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "DE.Share.Rpc.EntityMailBox", StringComparison.Ordinal)
                || string.Equals(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::DE.Share.Rpc.EntityMailBox", StringComparison.Ordinal);
        }

        private static bool InheritsFrom(INamedTypeSymbol typeSymbol, INamedTypeSymbol baseTypeSymbol)
        {
            for (var current = typeSymbol; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseTypeSymbol))
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

        private static string GenerateSource(
            INamedTypeSymbol typeSymbol,
            IReadOnlyCollection<FieldGenerationInfo> fieldInfos,
            IReadOnlyCollection<RpcMethodGenerationInfo> rpcMethodInfos
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

            GenerateRpcSource(builder, indentLevel, rpcMethodInfos);

            while (indentLevel > 0)
            {
                indentLevel--;
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static void GenerateRpcSource(
            StringBuilder builder,
            int indentLevel,
            IReadOnlyCollection<RpcMethodGenerationInfo> rpcMethodInfos
        )
        {
            var serverRpcMethods = rpcMethodInfos.Where(info => info.IsServerRpc).OrderBy(info => info.MethodSymbol.Name, StringComparer.Ordinal).ToList();
            var clientRpcMethods = rpcMethodInfos.Where(info => info.IsClientRpc).OrderBy(info => info.MethodSymbol.Name, StringComparer.Ordinal).ToList();

            if (serverRpcMethods.Count > 0)
            {
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("public static bool __DEGF_RPC_InvokeServerRpc(object target, uint methodId, global::DE.Share.Rpc.RpcBinaryReader reader)");
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("{");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("var entity = (" + serverRpcMethods[0].MethodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")target;");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("switch (methodId)");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("{");
                foreach (var methodInfo in serverRpcMethods)
                {
                    GenerateRpcInvokeCase(builder, indentLevel + 2, methodInfo);
                }

                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("}");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("return false;");
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("}");
                builder.AppendLine();
            }

            if (clientRpcMethods.Count > 0)
            {
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("public static bool __DEGF_RPC_InvokeClientRpc(object target, uint methodId, global::DE.Share.Rpc.RpcBinaryReader reader)");
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("{");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("var entity = (" + clientRpcMethods[0].MethodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")target;");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("switch (methodId)");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("{");
                foreach (var methodInfo in clientRpcMethods)
                {
                    GenerateRpcInvokeCase(builder, indentLevel + 2, methodInfo);
                }

                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("}");
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.AppendLine("return false;");
                builder.Append(new string(' ', indentLevel * 4));
                builder.AppendLine("}");
                builder.AppendLine();

            }
        }

        private static void GenerateRpcInvokeCase(StringBuilder builder, int indentLevel, RpcMethodGenerationInfo methodInfo)
        {
            builder.Append(new string(' ', indentLevel * 4));
            builder.Append("case ");
            builder.Append(methodInfo.MethodId);
            builder.AppendLine("u:");
            builder.Append(new string(' ', indentLevel * 4));
            builder.AppendLine("{");
            var argumentNames = new List<string>();
            foreach (var parameter in methodInfo.MethodSymbol.Parameters)
            {
                var argumentName = "__rpc_arg_" + parameter.Ordinal;
                argumentNames.Add(argumentName);
                builder.Append(new string(' ', (indentLevel + 1) * 4));
                builder.Append("var ");
                builder.Append(argumentName);
                builder.Append(" = reader.");
                builder.Append(GetRpcReaderMethodName(parameter.Type));
                builder.AppendLine("();");
            }

            builder.Append(new string(' ', (indentLevel + 1) * 4));
            builder.Append("entity.");
            builder.Append(methodInfo.MethodSymbol.Name);
            builder.Append('(');
            builder.Append(string.Join(", ", argumentNames));
            builder.AppendLine(");");
            builder.Append(new string(' ', (indentLevel + 1) * 4));
            builder.AppendLine("return true;");
            builder.Append(new string(' ', indentLevel * 4));
            builder.AppendLine("}");
        }

        private static uint ComputeRpcMethodId(IMethodSymbol methodSymbol)
        {
            var signature = methodSymbol.Name + "("
                + string.Join(",", methodSymbol.Parameters.Select(parameter => GetRpcParameterTypeName(parameter.Type))) + ")";
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            var hash = offsetBasis;
            foreach (var character in signature)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash == 0 ? 1u : hash;
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

        private readonly struct RpcMethodGenerationInfo
        {
            public RpcMethodGenerationInfo(IMethodSymbol methodSymbol, bool isServerRpc, bool isClientRpc, uint methodId)
            {
                MethodSymbol = methodSymbol;
                IsServerRpc = isServerRpc;
                IsClientRpc = isClientRpc;
                MethodId = methodId;
            }

            public IMethodSymbol MethodSymbol { get; }
            public bool IsServerRpc { get; }
            public bool IsClientRpc { get; }
            public uint MethodId { get; }
        }

        private sealed class SyntaxReceiver : ISyntaxReceiver
        {
            public List<VariableDeclaratorSyntax> CandidateFields { get; } = new List<VariableDeclaratorSyntax>();
            public List<MethodDeclarationSyntax> CandidateMethods { get; } = new List<MethodDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is MethodDeclarationSyntax methodDeclaration && methodDeclaration.AttributeLists.Count > 0)
                {
                    CandidateMethods.Add(methodDeclaration);
                    return;
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
