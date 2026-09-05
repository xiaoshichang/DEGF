using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DE.Share.EntityRpcSG
{
    [Generator]
    public sealed class EntityRpcGenerator : ISourceGenerator
    {
        private const string ServerRpcAttributeFullName = "DE.Share.Rpc.ServerRpcAttribute";
        private const string ClientRpcAttributeFullName = "DE.Share.Rpc.ClientRpcAttribute";

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

            var serverRpcAttributeSymbol = context.Compilation.GetTypeByMetadataName(ServerRpcAttributeFullName);
            var clientRpcAttributeSymbol = context.Compilation.GetTypeByMetadataName(ClientRpcAttributeFullName);
            if (serverRpcAttributeSymbol == null && clientRpcAttributeSymbol == null)
            {
                return;
            }

            var rpcMethodsByType = new Dictionary<INamedTypeSymbol, List<RpcMethodGenerationInfo>>(SymbolEqualityComparer.Default);
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

            foreach (var typeSymbol in rpcMethodsByType.Keys.OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal))
            {
                var source = GenerateSource(typeSymbol, rpcMethodsByType[typeSymbol]);
                context.AddSource(
                    SanitizeHintName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) + ".EntityRpc.g.cs",
                    SourceText.From(source, Encoding.UTF8)
                );
            }
        }

        private static bool HasAttribute(IMethodSymbol methodSymbol, INamedTypeSymbol attributeSymbol)
        {
            return methodSymbol
                .GetAttributes()
                .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
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
                || typeSymbol.SpecialType == SpecialType.System_UInt64
                || typeSymbol.TypeKind == TypeKind.Enum
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
            case SpecialType.System_UInt64:
                return "ReadUInt64";
            default:
                if (typeSymbol.TypeKind == TypeKind.Enum)
                {
                    return "ReadInt32";
                }

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
            case SpecialType.System_UInt64:
                return "ulong";
            default:
                if (typeSymbol.TypeKind == TypeKind.Enum)
                {
                    return typeSymbol.ToDisplayString();
                }

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

        private static string GenerateSource(
            INamedTypeSymbol typeSymbol,
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

            GenerateRpcDispatcher(builder, indentLevel, serverRpcMethods, "Server");
            GenerateRpcDispatcher(builder, indentLevel, clientRpcMethods, "Client");
        }

        private static void GenerateRpcDispatcher(
            StringBuilder builder,
            int indentLevel,
            IReadOnlyList<RpcMethodGenerationInfo> rpcMethods,
            string rpcDirection
        )
        {
            if (rpcMethods.Count == 0)
            {
                return;
            }

            builder.Append(new string(' ', indentLevel * 4));
            builder.Append("public static bool __DEGF_RPC_Invoke");
            builder.Append(rpcDirection);
            builder.AppendLine("Rpc(object target, uint methodId, global::DE.Share.Rpc.RpcBinaryReader reader)");
            builder.Append(new string(' ', indentLevel * 4));
            builder.AppendLine("{");
            builder.Append(new string(' ', (indentLevel + 1) * 4));
            builder.AppendLine("var entity = (" + rpcMethods[0].MethodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")target;");
            builder.Append(new string(' ', (indentLevel + 1) * 4));
            builder.AppendLine("switch (methodId)");
            builder.Append(new string(' ', (indentLevel + 1) * 4));
            builder.AppendLine("{");
            foreach (var methodInfo in rpcMethods)
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
                builder.Append(" = ");
                if (parameter.Type.TypeKind == TypeKind.Enum)
                {
                    builder.Append('(');
                    builder.Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    builder.Append(')');
                }

                builder.Append("reader.");
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
            public List<MethodDeclarationSyntax> CandidateMethods { get; } = new List<MethodDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is MethodDeclarationSyntax methodDeclaration && methodDeclaration.AttributeLists.Count > 0)
                {
                    CandidateMethods.Add(methodDeclaration);
                }
            }
        }
    }
}
