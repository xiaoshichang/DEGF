using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DE.Server.NativeBridge
{
    public static class TelnetCSharpExecutor
    {
        public static string Execute(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return string.Empty;
            }

            try
            {
                var value = EvaluatePath(code.Trim());
                return FormatValue(value);
            }
            catch (Exception exception)
            {
                return exception.ToString();
            }
        }

        private static object EvaluatePath(string code)
        {
            var parts = code
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
            if (parts.Length == 0)
            {
                return string.Empty;
            }

            var value = ResolveRoot(parts[0]);
            for (var index = 1; index < parts.Length; ++index)
            {
                value = ResolveMember(value, parts[index]);
            }

            return value;
        }

        private static object ResolveRoot(string name)
        {
            var runtimeState = ManagedRuntimeState.RequireCurrent();
            if (string.Equals(name, nameof(ManagedRuntimeState), StringComparison.Ordinal))
            {
                return runtimeState;
            }

            if (string.Equals(name, nameof(GmCommandRuntimeState), StringComparison.Ordinal))
            {
                return runtimeState.GmCommandRuntimeState ?? throw new InvalidOperationException("GM command runtime state is not available on this node.");
            }

            if (string.Equals(name, nameof(GateServerRuntimeState), StringComparison.Ordinal))
            {
                return runtimeState.GateServerRuntimeState ?? throw new InvalidOperationException("Gate server runtime state is not available on this node.");
            }

            if (string.Equals(name, nameof(GameServerRuntimeState), StringComparison.Ordinal))
            {
                return runtimeState.GameServerRuntimeState ?? throw new InvalidOperationException("Game server runtime state is not available on this node.");
            }

            throw new InvalidOperationException($"Unknown C# telnet root: {name}.");
        }

        private static object ResolveMember(object target, string memberName)
        {
            if (target == null)
            {
                return null;
            }

            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                return property.GetValue(target);
            }

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(target);
            }

            var methodName = memberName.EndsWith("()", StringComparison.Ordinal)
                ? memberName.Substring(0, memberName.Length - 2)
                : memberName;
            var method = type
                .GetMethods(flags)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                    && candidate.GetParameters().Length == 0);
            if (method != null)
            {
                return method.Invoke(target, Array.Empty<object>());
            }

            throw new InvalidOperationException($"Member {memberName} not found on {type.FullName}.");
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is string text)
            {
                return text;
            }

            if (value is IEnumerable enumerable && !(value is IDictionary))
            {
                var builder = new StringBuilder();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= 32)
                    {
                        builder.AppendLine("...");
                        break;
                    }

                    builder.AppendLine(item?.ToString() ?? "null");
                    ++count;
                }

                return builder.Length == 0 ? "empty" : builder.ToString().TrimEnd();
            }

            return value.ToString() ?? string.Empty;
        }
    }
}
