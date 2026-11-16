using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using FieldGenerationInfo = DE.Share.EntityPropertySG.EntityPropertyGenerator.FieldGenerationInfo;

namespace DE.Share.EntityPropertySG
{
    internal static partial class EntityPropertySerializationEmitter
    {
        private static readonly DiagnosticDescriptor HashCollisionDescriptor = new DiagnosticDescriptor(
            id: "DEEP008",
            title: "Entity property hashes must be unique within an inheritance chain",
            messageFormat: "Properties '{0}' and '{1}' have the same ID {2} in the inheritance chain of '{3}'",
            category: "EntityProperty",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static byte GetFlags(AttributeData attribute)
        {
            return attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value != null
                ? Convert.ToByte(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture)
                : (byte)0;
        }

        internal static bool TryGetCodec(ITypeSymbol type, out PropertyCodec codec)
        {
            bool nullable = type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var valueType = nullable ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
            bool isEnum = valueType.TypeKind == TypeKind.Enum;
            var rawType = isEnum ? ((INamedTypeSymbol)valueType).EnumUnderlyingType : valueType;
            codec = default;
            if (rawType == null || !TryGetPrimitive(rawType, out byte typeCode, out string method, out int size))
            {
                return false;
            }

            codec = new PropertyCodec(
                (byte)(typeCode | (isEnum ? 0x40 : 0) | (nullable ? 0x80 : 0)),
                method,
                size,
                valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                rawType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                isEnum,
                nullable);
            return true;
        }

        private static bool TryGetPrimitive(ITypeSymbol type, out byte code, out string method, out int size)
        {
            switch (type.SpecialType)
            {
            case SpecialType.System_Boolean: code = 1; method = "Boolean"; size = 1; return true;
            case SpecialType.System_Byte: code = 2; method = "Byte"; size = 1; return true;
            case SpecialType.System_SByte: code = 3; method = "SByte"; size = 1; return true;
            case SpecialType.System_Int16: code = 4; method = "Int16"; size = 2; return true;
            case SpecialType.System_UInt16: code = 5; method = "UInt16"; size = 2; return true;
            case SpecialType.System_Int32: code = 6; method = "Int32"; size = 4; return true;
            case SpecialType.System_UInt32: code = 7; method = "UInt32"; size = 4; return true;
            case SpecialType.System_Int64: code = 8; method = "Int64"; size = 8; return true;
            case SpecialType.System_UInt64: code = 9; method = "UInt64"; size = 8; return true;
            case SpecialType.System_Single: code = 10; method = "Single"; size = 4; return true;
            case SpecialType.System_Double: code = 11; method = "Double"; size = 8; return true;
            case SpecialType.System_String: code = 12; method = "String"; size = -1; return true;
            case SpecialType.System_Char: code = 14; method = "Char"; size = 2; return true;
            }

            switch (type.ToDisplayString())
            {
            case "System.Guid": code = 13; method = "Guid"; size = 16; return true;
            case "DE.Share.Foundation.Mathematics.Vector3": code = 16; method = "Vector3"; size = 12; return true;
            case "DE.Share.Foundation.Mathematics.Quaternion": code = 17; method = "Quaternion"; size = 16; return true;
            }

            code = 0;
            method = string.Empty;
            size = 0;
            return false;
        }

        private static void AppendWriteValue(StringBuilder builder, PropertyCodec codec, string value, string writer, int indent)
        {
            if (codec.IsNullable)
            {
                Line(builder, indent, writer + ".WriteByte(" + value + ".HasValue ? (byte)1 : (byte)0);");
                Line(builder, indent, "if (" + value + ".HasValue)");
                Line(builder, indent, "{");
                AppendWriteScalar(builder, codec, value + ".Value", writer, indent + 1);
                Line(builder, indent, "}");
            }
            else
            {
                AppendWriteScalar(builder, codec, value, writer, indent);
            }
        }

        private static void AppendWriteScalar(StringBuilder builder, PropertyCodec codec, string value, string writer, int indent)
        {
            string expression = codec.IsEnum ? "(" + codec.RawTypeName + ")" + value : value;
            Line(builder, indent, writer + ".Write" + codec.Method + "(" + expression + ");");
        }

        private static void AppendReadValue(StringBuilder builder, FieldGenerationInfo field, int indent)
        {
            var codec = field.Codec;
            if (codec.IsNullable)
            {
                Line(builder, indent, "if (!reader.TryReadByte(out byte marker) || marker > 1)");
                ReturnFalse(builder, indent);
                Line(builder, indent, field.TypeName + " value = default;");
                Line(builder, indent, "if (marker == 1)");
                Line(builder, indent, "{");
                Line(builder, indent + 1, "if (!reader.TryRead" + codec.Method + "(out " + codec.RawTypeName + " rawValue))");
                ReturnFalse(builder, indent + 1);
                Line(builder, indent + 1, "value = " + (codec.IsEnum ? "(" + codec.ValueTypeName + ")" : string.Empty) + "rawValue;");
                Line(builder, indent, "}");
                Line(builder, indent, "if (reader.Remaining != 0)");
                ReturnFalse(builder, indent);
            }
            else
            {
                string valueName = codec.IsEnum ? "rawValue" : "value";
                Line(builder, indent, "if (!reader.TryRead" + codec.Method + "(out " + codec.RawTypeName + " " + valueName
                    + ") || reader.Remaining != 0)");
                ReturnFalse(builder, indent);
                if (codec.IsEnum)
                {
                    Line(builder, indent, field.TypeName + " value = (" + codec.ValueTypeName + ")rawValue;");
                }
            }
        }

        internal static void AppendSetter(StringBuilder builder, INamedTypeSymbol type, FieldGenerationInfo field, int indent)
        {
            string? runtimeModule = EntityPropertySymbols.GetRuntimeModule(type);
            string module = "global::" + runtimeModule;
            string id = IdLiteral(field.PropertyName);
            bool server = runtimeModule == EntityPropertySymbols.ServerModule;
            bool component = EntityPropertySymbols.IsComponent(type, runtimeModule);
            bool identity = IsIdentityField(type, field);
            string target = component ? "OwnerEntity?." : string.Empty;
            string ownerArgument = component ? ", this" : string.Empty;
            string trackingCondition = component
                ? "OwnerEntity != null && OwnerEntity.IsPropertyChangeTrackingEnabled" : "IsPropertyChangeTrackingEnabled";
            byte mask = GetProfileMask(field.Flags);
            AppendUnchangedReturn(builder, field, indent);
            AppendSetterWriteValidation(builder, server, identity, target, id, ownerArgument, indent);
            Line(builder, indent, "OnBefore" + field.PropertyName + "Change(value);");
            AppendUnchangedReturn(builder, field, indent);
            AppendSetterWriteValidation(builder, server, identity, target, id, ownerArgument, indent);
            if (server && mask != 0)
            {
                Line(builder, indent, "int dirtyValueLength = -1;");
                Line(builder, indent, "if (" + trackingCondition + ")");
                Line(builder, indent, "{");
                Line(builder, indent + 1, "dirtyValueLength = " + GetWireSizeExpression(field, module, "value") + ";");
                Line(builder, indent + 1, target + "ValidateDirtyPropertyCapacity(" + id + ", dirtyValueLength" + ownerArgument + ");");
                Line(builder, indent, "}");
            }
            Line(builder, indent, field.FieldSymbol.Name + " = value;");
            if (server && mask != 0)
            {
                Line(builder, indent, "if (dirtyValueLength >= 0)");
                Line(builder, indent, "{");
                Line(builder, indent + 1, target + "MarkPropertyDirty(" + id + ",");
                Line(builder, indent + 2, "(" + module + ".EntitySerializationProfileMask)" + mask.ToString(CultureInfo.InvariantCulture)
                    + ", dirtyValueLength" + ownerArgument + ");");
                Line(builder, indent, "}");
            }
            Line(builder, indent, "OnAfter" + field.PropertyName + "Change(value);");
        }

        private static void AppendUnchangedReturn(StringBuilder builder, FieldGenerationInfo field, int indent)
        {
            Line(builder, indent, "if (global::System.Collections.Generic.EqualityComparer<" + field.TypeName
                + ">.Default.Equals(" + field.FieldSymbol.Name + ", value))");
            Line(builder, indent, "{");
            Line(builder, indent + 1, "return;");
            Line(builder, indent, "}");
        }

        private static void AppendSetterWriteValidation(
            StringBuilder builder, bool server, bool identity, string target, string id, string ownerArgument, int indent)
        {
            if (server)
            {
                Line(builder, indent, target + "ValidatePropertyWrite(" + id + ownerArgument + ");");
            }
            else if (identity)
            {
                Line(builder, indent, "if (IsInitialized)");
                Line(builder, indent, "{");
                Line(builder, indent + 1, "throw new global::System.InvalidOperationException("
                    + "\"The identity of an initialized entity cannot be changed.\");");
                Line(builder, indent, "}");
            }
        }

        private static byte GetProfileMask(byte flags)
        {
            return (byte)(((flags & (4 | 8)) != 0 ? 1 : 0) | ((flags & 8) != 0 ? 2 : 0)
                | ((flags & 2) == 0 && (flags & (1 | 4 | 8 | 16)) != 0 ? 4 : 0));
        }

        private static string ProfilePredicate(byte mask, string module, string value = "profile")
        {
            var profiles = new List<string>();
            if ((mask & 1) != 0)
            {
                profiles.Add(value + " == " + module + ".EntitySerializationProfile.Owner");
            }

            if ((mask & 2) != 0)
            {
                profiles.Add(value + " == " + module + ".EntitySerializationProfile.Observer");
            }

            if ((mask & 4) != 0)
            {
                profiles.Add(value + " == " + module + ".EntitySerializationProfile.Migration");
            }

            return profiles.Count == 0 ? "false" : string.Join(" || ", profiles);
        }

        private static void ReturnFalse(StringBuilder builder, int indent)
        {
            Line(builder, indent, "{");
            Line(builder, indent + 1, "return false;");
            Line(builder, indent, "}");
        }

        private static void Line(StringBuilder builder, int indent, string source)
        {
            builder.Append(' ', indent * 4).AppendLine(source);
        }

        private static ulong ComputeId(string name)
        {
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                foreach (byte value in Encoding.UTF8.GetBytes(name))
                {
                    hash = (hash ^ value) * 1099511628211UL;
                }
            }

            return hash;
        }

        private static string IdLiteral(string name)
        {
            return "0x" + ComputeId(name).ToString("X16", CultureInfo.InvariantCulture) + "UL";
        }

        internal static void ReportHashCollisions(
            GeneratorExecutionContext context, INamedTypeSymbol type, IReadOnlyList<FieldGenerationInfo> fields, IReadOnlyList<IPropertySymbol> nodes)
        {
            var hashes = new Dictionary<ulong, string>();
            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in current.GetMembers())
                {
                    if (member.IsStatic || !(member is IFieldSymbol || member is IPropertySymbol))
                    {
                        continue;
                    }

                    var attribute = EntityPropertySymbols.GetAttribute(member);
                    var componentAttribute = EntityPropertySymbols.GetComponentAttribute(member);
                    bool isLeaf = attribute != null;
                    bool isNode = componentAttribute != null;
                    if (!isLeaf && !isNode)
                    {
                        continue;
                    }

                    string name = member is IFieldSymbol && member.Name.StartsWith("__", StringComparison.Ordinal)
                        ? member.Name.Substring(2) : member.Name;
                    if (names.Add(name))
                    {
                        AddHash(context, type, name, type.Locations.FirstOrDefault(), hashes);
                    }
                }
            }

            foreach (var field in fields)
            {
                AddHash(context, type, field.PropertyName, field.FieldSymbol.Locations.FirstOrDefault(), hashes);
            }

            foreach (var node in nodes)
            {
                AddHash(context, type, node.Name, node.Locations.FirstOrDefault(), hashes);
            }
        }

        private static void AddHash(
            GeneratorExecutionContext context, INamedTypeSymbol type, string name, Location? location, Dictionary<ulong, string> hashes)
        {
            ulong hash = ComputeId(name);
            if (hashes.TryGetValue(hash, out string previous))
            {
                context.ReportDiagnostic(Diagnostic.Create(HashCollisionDescriptor, location, previous, name, IdLiteral(name), type.ToDisplayString()));
            }
            else
            {
                hashes.Add(hash, name);
            }
        }

        internal readonly struct PropertyCodec
        {
            internal PropertyCodec(byte typeCode, string method, int fixedSize, string valueTypeName, string rawTypeName, bool isEnum, bool isNullable)
            {
                TypeCode = typeCode;
                Method = method;
                FixedSize = fixedSize;
                ValueTypeName = valueTypeName;
                RawTypeName = rawTypeName;
                IsEnum = isEnum;
                IsNullable = isNullable;
            }

            internal byte TypeCode { get; }
            internal string Method { get; }
            internal int FixedSize { get; }
            internal string ValueTypeName { get; }
            internal string RawTypeName { get; }
            internal bool IsEnum { get; }
            internal bool IsNullable { get; }
            internal string CodeLiteral => "0x" + TypeCode.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
