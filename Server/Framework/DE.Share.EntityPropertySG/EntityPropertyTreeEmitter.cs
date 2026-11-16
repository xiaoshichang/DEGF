using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using FieldGenerationInfo = DE.Share.EntityPropertySG.EntityPropertyGenerator.FieldGenerationInfo;

namespace DE.Share.EntityPropertySG
{
    internal static partial class EntityPropertySerializationEmitter
    {
        internal static void Append(
            StringBuilder builder, INamedTypeSymbol type, IReadOnlyList<FieldGenerationInfo> fields,
            IReadOnlyList<IPropertySymbol> nodes, int indent)
        {
            string module = "global::" + EntityPropertySymbols.GetRuntimeModule(type);
            bool root = EntityPropertySymbols.IsRoot(type);
            string modifier = root ? "virtual" : "override";
            AppendWriteSnapshot(builder, fields, nodes, module, root, modifier, indent);
            AppendWriteProperty(builder, fields, module, root, modifier, indent);
            AppendReadProperty(builder, type, fields, nodes, module, root, modifier, indent);
            AppendValidateSnapshot(builder, type, fields, nodes, module, root, modifier, indent);
            AppendHasProperties(builder, fields, nodes, module, root, modifier, indent);
            AppendValidateTree(builder, nodes, module, root, modifier, indent);
            AppendComponentPropertyRoute(builder, nodes, module, root, modifier, indent);
        }

        private static void AppendWriteSnapshot(
            StringBuilder builder, IReadOnlyList<FieldGenerationInfo> fields, IReadOnlyList<IPropertySymbol> nodes,
            string module, bool root, string modifier, int indent)
        {
            Line(builder, indent, "public " + modifier + " void WriteSnapshot(");
            Line(builder, indent + 1, module + ".EntityPropertyWriter writer, " + module + ".EntitySerializationProfile profile)");
            Line(builder, indent, "{");
            if (!root)
            {
                Line(builder, indent + 1, "base.WriteSnapshot(writer, profile);");
            }

            foreach (var field in fields)
            {
                byte mask = GetProfileMask(field.Flags);
                if (mask == 0)
                {
                    continue;
                }

                Line(builder, indent + 1, "if (" + ProfilePredicate(mask, module) + ")");
                Line(builder, indent + 1, "{");
                AppendWriteCurrentField(builder, field, module, indent + 2);
                Line(builder, indent + 1, "}");
            }

            foreach (var node in nodes)
            {
                Line(builder, indent + 1, module + ".EntitySerializer.WriteChildNode(writer, " + IdLiteral(node.Name) + ",");
                Line(builder, indent + 2, NodeValue(node, module) + ", profile);");
            }

            Line(builder, indent, "}");
            builder.AppendLine();
        }

        private static void AppendWriteProperty(
            StringBuilder builder, IReadOnlyList<FieldGenerationInfo> fields, string module, bool root, string modifier, int indent)
        {
            Line(builder, indent, "public " + modifier + " bool WriteProperty(");
            Line(builder, indent + 1, "ulong propertyId, " + module + ".EntityPropertyWriter writer, " + module
                + ".EntitySerializationProfile profile)");
            Line(builder, indent, "{");
            Line(builder, indent + 1, "switch (propertyId)");
            Line(builder, indent + 1, "{");
            foreach (var field in fields)
            {
                Line(builder, indent + 1, "case " + IdLiteral(field.PropertyName) + ":");
                Line(builder, indent + 1, "{");
                byte mask = GetProfileMask(field.Flags);
                if (mask != 0)
                {
                    Line(builder, indent + 2, "if (!(" + ProfilePredicate(mask, module) + "))");
                    ReturnFalse(builder, indent + 2);
                    AppendWriteCurrentField(builder, field, module, indent + 2);
                    Line(builder, indent + 2, "return true;");
                }
                else
                {
                    Line(builder, indent + 2, "return false;");
                }
                Line(builder, indent + 1, "}");
            }
            Line(builder, indent + 1, "default:");
            Line(builder, indent + 2, root ? "return false;" : "return base.WriteProperty(propertyId, writer, profile);");
            Line(builder, indent + 1, "}");
            Line(builder, indent, "}");
            builder.AppendLine();
        }

        private static void AppendWriteCurrentField(StringBuilder builder, FieldGenerationInfo field, string module, int indent)
        {
            Line(builder, indent, "var value = " + field.FieldSymbol.Name + ";");
            Line(builder, indent, "writer.WritePropertyHeader(" + IdLiteral(field.PropertyName)
                + ", " + field.Codec.CodeLiteral + ", " + GetWireSizeExpression(field, module, "value") + ");");
            AppendWriteValue(builder, field.Codec, "value", "writer", indent);
        }

        private static string GetWireSizeExpression(FieldGenerationInfo field, string module, string value)
        {
            if (field.Codec.IsNullable)
            {
                return value + ".HasValue ? " + (field.Codec.FixedSize + 1).ToString(CultureInfo.InvariantCulture) + " : 1";
            }
            return field.Codec.FixedSize < 0 ? module + ".EntityPropertyWriter.GetStringWireSize(" + value + ")"
                : field.Codec.FixedSize.ToString(CultureInfo.InvariantCulture);
        }

        private static void AppendReadProperty(
            StringBuilder builder, INamedTypeSymbol type, IReadOnlyList<FieldGenerationInfo> fields, IReadOnlyList<IPropertySymbol> nodes,
            string module, bool root, string modifier, int indent)
        {
            Line(builder, indent, "public " + modifier + " bool ReadProperty(");
            Line(builder, indent + 1, "ulong propertyId, byte wireTag, ref " + module + ".EntityPropertyReader reader,");
            Line(builder, indent + 1, module + ".EntityPropertyReadContext context)");
            Line(builder, indent, "{");
            Line(builder, indent + 1, "switch (propertyId)");
            Line(builder, indent + 1, "{");
            foreach (var field in fields)
            {
                AppendReadField(builder, type, field, module, indent + 1);
            }

            foreach (var node in nodes)
            {
                Line(builder, indent + 1, "case " + IdLiteral(node.Name) + ":");
                Line(builder, indent + 1, "{");
                Line(builder, indent + 2, "var child = this.@" + node.Name + ";");
                Line(builder, indent + 2, "if (wireTag != 0 || child == null)");
                ReturnFalse(builder, indent + 2);
                Line(builder, indent + 2, "return " + module + ".EntitySerializer.ReadNodeBody(");
                Line(builder, indent + 3, "(" + module + ".IEntityPropertyNode)child, ref reader, context);");
                Line(builder, indent + 1, "}");
            }

            Line(builder, indent + 1, "default:");
            if (root)
            {
                Line(builder, indent + 2, "if (context.Profile == " + module + ".EntitySerializationProfile.Migration)");
                ReturnFalse(builder, indent + 2);
                Line(builder, indent + 2, "reader.SkipRemaining();");
                Line(builder, indent + 2, "return true;");
            }
            else
            {
                Line(builder, indent + 2, "return base.ReadProperty(propertyId, wireTag, ref reader, context);");
            }

            Line(builder, indent + 1, "}");
            Line(builder, indent, "}");
            builder.AppendLine();
        }

        private static void AppendReadField(StringBuilder builder, INamedTypeSymbol type, FieldGenerationInfo field, string module, int indent)
        {
            Line(builder, indent, "case " + IdLiteral(field.PropertyName) + ":");
            Line(builder, indent, "{");
            byte mask = GetProfileMask(field.Flags);
            if (mask == 0)
            {
                Line(builder, indent + 1, "return false;");
                Line(builder, indent, "}");
                return;
            }

            Line(builder, indent + 1, "if (wireTag != " + field.Codec.CodeLiteral + " || !(" + ProfilePredicate(mask, module, "context.Profile") + "))");
            ReturnFalse(builder, indent + 1);
            bool identity = IsIdentityField(type, field);
            if (identity)
            {
                Line(builder, indent + 1, "if (context.Mode == " + module + ".EntitySerializationMode.Patch)");
                ReturnFalse(builder, indent + 1);
            }

            AppendReadValue(builder, field, indent + 1);
            if (identity)
            {
                Line(builder, indent + 1, "if (value == global::System.Guid.Empty");
                Line(builder, indent + 2, "|| (context.ExpectedEntityId.HasValue && value != context.ExpectedEntityId.Value))");
                ReturnFalse(builder, indent + 1);
            }

            Line(builder, indent + 1, "context.MarkRecognized();");
            Line(builder, indent + 1, "if (context.Apply)");
            Line(builder, indent + 1, "{");
            Line(builder, indent + 2, field.FieldSymbol.Name + " = value;");
            Line(builder, indent + 1, "}");
            Line(builder, indent + 1, "return true;");
            Line(builder, indent, "}");
        }

        private static void AppendValidateSnapshot(
            StringBuilder builder, INamedTypeSymbol type, IReadOnlyList<FieldGenerationInfo> fields, IReadOnlyList<IPropertySymbol> nodes,
            string module, bool root, string modifier, int indent)
        {
            Line(builder, indent, "public " + modifier + " bool ValidateSnapshot(");
            Line(builder, indent + 1, "global::System.Collections.Generic.IReadOnlyCollection<ulong> seen, " + module
                + ".EntitySerializationProfile profile)");
            Line(builder, indent, "{");
            if (!root)
            {
                Line(builder, indent + 1, "if (!base.ValidateSnapshot(seen, profile))");
                ReturnFalse(builder, indent + 1);
            }

            foreach (var field in fields)
            {
                byte mask = GetRequiredMask(type, field);
                if (mask != 0)
                {
                    Line(builder, indent + 1, "if ((" + ProfilePredicate(mask, module) + ")");
                    Line(builder, indent + 2, "&& !global::System.Linq.Enumerable.Contains(seen, " + IdLiteral(field.PropertyName) + "))");
                    ReturnFalse(builder, indent + 1);
                }
            }

            foreach (var node in nodes)
            {
                Line(builder, indent + 1, "if (this.@" + node.Name + " == null");
                Line(builder, indent + 2, "|| (profile == " + module + ".EntitySerializationProfile.Migration");
                Line(builder, indent + 3, "&& " + NodeValue(node, module) + ".HasSnapshotProperties(profile)");
                Line(builder, indent + 3, "&& !global::System.Linq.Enumerable.Contains(seen, " + IdLiteral(node.Name) + ")))");
                ReturnFalse(builder, indent + 1);
            }

            Line(builder, indent + 1, "return true;");
            Line(builder, indent, "}");
            builder.AppendLine();
        }

        private static void AppendHasProperties(
            StringBuilder builder, IReadOnlyList<FieldGenerationInfo> fields, IReadOnlyList<IPropertySymbol> nodes,
            string module, bool root, string modifier, int indent)
        {
            const string name = "HasSnapshotProperties";
            Line(builder, indent, "public " + modifier + " bool " + name + "(" + module + ".EntitySerializationProfile profile)");
            Line(builder, indent, "{");
            var predicates = new List<string>();
            if (!root)
            {
                predicates.Add("base." + name + "(profile)");
            }

            byte mask = 0;
            foreach (var field in fields)
            {
                mask |= GetProfileMask(field.Flags);
            }

            if (mask != 0)
            {
                predicates.Add("(" + ProfilePredicate(mask, module) + ")");
            }

            foreach (var node in nodes)
            {
                predicates.Add("(this.@" + node.Name + " != null && " + NodeValue(node, module) + "." + name + "(profile))");
            }

            if (predicates.Count == 0)
            {
                Line(builder, indent + 1, "return false;");
            }
            else
            {
                Line(builder, indent + 1, "return " + string.Join("\n" + new string(' ', (indent + 2) * 4) + "|| ", predicates) + ";");
            }

            Line(builder, indent, "}");
            builder.AppendLine();
        }

        private static void AppendValidateTree(
            StringBuilder builder, IReadOnlyList<IPropertySymbol> nodes, string module, bool root, string modifier, int indent)
        {
            Line(builder, indent, "public " + modifier + " void ValidatePropertyTree()");
            Line(builder, indent, "{");
            if (!root)
            {
                Line(builder, indent + 1, "base.ValidatePropertyTree();");
            }

            foreach (var node in nodes)
            {
                Line(builder, indent + 1, module + ".EntitySerializer.ValidateComponentBinding(this, this.@" + node.Name
                    + ", " + IdLiteral(node.Name) + ");");
            }

            Line(builder, indent, "}");
            builder.AppendLine();
        }

        private static void AppendComponentPropertyRoute(
            StringBuilder builder, IReadOnlyList<IPropertySymbol> nodes, string module, bool root, string modifier, int indent)
        {
            Line(builder, indent, "public " + modifier + " bool TryGetComponentPropertyHash(");
            Line(builder, indent + 1, module + ".IEntityPropertyNode component, out ulong propertyHash)");
            Line(builder, indent, "{");
            foreach (var node in nodes)
            {
                Line(builder, indent + 1, "if (this.@" + node.Name
                    + " != null && global::System.Object.ReferenceEquals(component, this.@" + node.Name + "))");
                Line(builder, indent + 1, "{");
                Line(builder, indent + 2, "propertyHash = " + IdLiteral(node.Name) + ";");
                Line(builder, indent + 2, "return true;");
                Line(builder, indent + 1, "}");
            }

            if (root)
            {
                Line(builder, indent + 1, "propertyHash = 0;");
                Line(builder, indent + 1, "return false;");
            }
            else
            {
                Line(builder, indent + 1, "return base.TryGetComponentPropertyHash(component, out propertyHash);");
            }
            Line(builder, indent, "}");
            builder.AppendLine();
        }

        private static byte GetRequiredMask(INamedTypeSymbol type, FieldGenerationInfo field)
        {
            byte mask = GetProfileMask(field.Flags);
            return IsIdentityField(type, field) ? mask : (byte)(mask & 4);
        }

        private static bool IsIdentityField(INamedTypeSymbol type, FieldGenerationInfo field)
        {
            return EntityPropertySymbols.IsRoot(type) && EntityPropertySymbols.IsEntity(type, EntityPropertySymbols.GetRuntimeModule(type))
                && field.PropertyName == "Guid";
        }

        private static string NodeValue(IPropertySymbol node, string module)
        {
            return "((" + module + ".IEntityPropertyNode)this.@" + node.Name + ")";
        }
    }
}
