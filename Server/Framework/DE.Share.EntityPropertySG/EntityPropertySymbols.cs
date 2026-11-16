using Microsoft.CodeAnalysis;

namespace DE.Share.EntityPropertySG
{
    internal static class EntityPropertySymbols
    {
        internal const string SharedModule = "DE.Share.Entities";
        internal const string ServerModule = "DE.Server.Entities";
        internal const string ClientModule = "DE.Client.Entities";

        internal static AttributeData? GetAttribute(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (IsPropertyAttribute(attribute.AttributeClass))
                {
                    return attribute;
                }
            }

            return null;
        }

        internal static AttributeData? GetComponentAttribute(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (IsComponentAttribute(attribute.AttributeClass))
                {
                    return attribute;
                }
            }

            return null;
        }

        internal static bool IsComponentAttribute(INamedTypeSymbol? attributeType)
        {
            return attributeType != null && attributeType.Name == "EntityComponentAttribute"
                && attributeType.ContainingNamespace.ToDisplayString() == SharedModule;
        }

        internal static bool IsPropertyAttribute(INamedTypeSymbol? attributeType)
        {
            return attributeType != null && attributeType.Name == "EntityPropertyAttribute"
                && attributeType.ContainingNamespace.ToDisplayString() == SharedModule;
        }

        internal static bool IsEntityOrComponent(INamedTypeSymbol type, string? module)
        {
            return IsEntity(type, module) || IsComponent(type, module);
        }

        internal static string? GetRuntimeModule(INamedTypeSymbol type)
        {
            return IsEntityOrComponent(type, ServerModule) ? ServerModule : IsEntityOrComponent(type, ClientModule) ? ClientModule : null;
        }

        internal static bool IsRoot(INamedTypeSymbol type)
        {
            var fullName = type.ToDisplayString();
            return fullName == ServerModule + ".ServerEntity" || fullName == ServerModule + ".ServerEntityComponent"
                || fullName == ClientModule + ".ClientEntity" || fullName == ClientModule + ".ClientEntityComponent";
        }

        internal static bool IsEntity(INamedTypeSymbol type, string? module)
        {
            return module != null && InheritsFrom(type, module + (module == ServerModule ? ".ServerEntity" : ".ClientEntity"));
        }

        internal static bool IsComponent(INamedTypeSymbol type, string? module)
        {
            return module != null
                && InheritsFrom(type, module + (module == ServerModule ? ".ServerEntityComponent" : ".ClientEntityComponent"));
        }

        internal static bool InheritsFrom(INamedTypeSymbol type, string fullName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.ToDisplayString() == fullName)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
