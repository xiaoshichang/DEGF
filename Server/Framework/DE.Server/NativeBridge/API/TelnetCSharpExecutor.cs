using System;
using System.Collections;
using System.Text;
using DynamicExpresso;

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
                var value = Evaluate(code.Trim());
                return FormatValue(value);
            }
            catch (Exception exception)
            {
                return exception.ToString();
            }
        }

        private static object Evaluate(string code)
        {
            var runtimeState = ManagedRuntimeState.RequireCurrent();
            var interpreter = new Interpreter();

            interpreter.SetVariable(nameof(ManagedRuntimeState), runtimeState);
            interpreter.SetVariable("Runtime", runtimeState);

            if (runtimeState.GmCommandRuntimeState != null)
            {
                interpreter.SetVariable(nameof(GmCommandRuntimeState), runtimeState.GmCommandRuntimeState);
                interpreter.SetVariable("GM", runtimeState.GmCommandRuntimeState);
            }

            if (runtimeState.GateServerRuntimeState != null)
            {
                interpreter.SetVariable(nameof(GateServerRuntimeState), runtimeState.GateServerRuntimeState);
                interpreter.SetVariable("Gate", runtimeState.GateServerRuntimeState);
            }

            if (runtimeState.GameServerRuntimeState != null)
            {
                interpreter.SetVariable(nameof(GameServerRuntimeState), runtimeState.GameServerRuntimeState);
                interpreter.SetVariable("Game", runtimeState.GameServerRuntimeState);
                interpreter.SetVariable("Avatars", runtimeState.GameServerRuntimeState.Avatars);
                interpreter.SetVariable("Entities", runtimeState.GameServerRuntimeState.Entities);
                interpreter.SetVariable("Stubs", runtimeState.GameServerRuntimeState.StubInstances);
            }

            return interpreter.Eval(code);
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
