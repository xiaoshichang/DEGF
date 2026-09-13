using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DE.Server.Entities;
using DE.Share.Data;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

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
            var globals = new TelnetCSharpGlobals(runtimeState);
            var assemblies = CollectScriptAssemblies(runtimeState);
            var assemblyLoader = new InteractiveAssemblyLoader();
            foreach (var assembly in assemblies)
            {
                assemblyLoader.RegisterDependency(assembly);
            }

            var script = CSharpScript.Create<object>(
                code,
                CreateScriptOptions(assemblies),
                typeof(TelnetCSharpGlobals),
                assemblyLoader
            );
            return script
                .RunAsync(globals)
                .GetAwaiter()
                .GetResult()
                .ReturnValue;
        }

        private static List<Assembly> CollectScriptAssemblies(ManagedRuntimeState runtimeState)
        {
            var assemblies = new List<Assembly>
            {
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Dictionary<,>).Assembly,
                typeof(ManagedRuntimeState).Assembly,
                typeof(ServerEntity).Assembly,
                typeof(DataRuntime).Assembly,
            };

            if (runtimeState.GameplayAssembly != null)
            {
                assemblies.Add(runtimeState.GameplayAssembly);
            }

            assemblies.AddRange(runtimeState.PreloadedAssemblies);
            return assemblies.Distinct().ToList();
        }

        private static ScriptOptions CreateScriptOptions(IReadOnlyList<Assembly> assemblies)
        {
            return ScriptOptions.Default
                .WithReferences(assemblies)
                .WithImports(
                    "System",
                    "System.Collections.Generic",
                    "System.Linq",
                    "DE.Server.Entities",
                    "DE.Server.NativeBridge",
                    "DE.Share.Data"
                );
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

        public sealed class TelnetCSharpGlobals
        {
            public TelnetCSharpGlobals(ManagedRuntimeState runtimeState)
            {
                ManagedRuntimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
                Runtime = ManagedRuntimeState;
                GmCommandRuntimeState = ManagedRuntimeState.GmCommandRuntimeState;
                GM = GmCommandRuntimeState;
                GateServerRuntimeState = ManagedRuntimeState.GateServerRuntimeState;
                Gate = GateServerRuntimeState;
                GameServerRuntimeState = ManagedRuntimeState.GameServerRuntimeState;
                Game = GameServerRuntimeState;
            }

            public ManagedRuntimeState ManagedRuntimeState { get; }
            public ManagedRuntimeState Runtime { get; }
            public GmCommandRuntimeState GmCommandRuntimeState { get; }
            public GmCommandRuntimeState GM { get; }
            public GateServerRuntimeState GateServerRuntimeState { get; }
            public GateServerRuntimeState Gate { get; }
            public GameServerRuntimeState GameServerRuntimeState { get; }
            public GameServerRuntimeState Game { get; }

            public object Avatars
            {
                get
                {
                    return GameServerRuntimeState?.Avatars;
                }
            }

            public object Entities
            {
                get
                {
                    return GameServerRuntimeState?.Entities;
                }
            }

            public object Stubs
            {
                get
                {
                    return GameServerRuntimeState?.StubInstances;
                }
            }

            /// <summary>
            /// Gets the full statistics report for every data table currently loaded in this process.
            /// Enter <c>DataTableStatistics</c> in the Telnet session to print the report.
            /// </summary>
            public string DataTableStatistics
            {
                get
                {
                    return DataRuntime.PrintTableStatistics();
                }
            }
        }
    }
}
