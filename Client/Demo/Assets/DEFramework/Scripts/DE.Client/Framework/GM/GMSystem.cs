using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Assets.Scripts.DE.Client.Framework
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class GMCommandAttribute : Attribute
    {
        public GMCommandAttribute(string desc = null, string alias = null)
        {
            Description = desc;
            CommandName = alias;
        }

        public string CommandName { get; }

        public string Description { get; set; }
    }

    internal sealed class GMCommandMetadata
    {
        public GMCommandMetadata(string commandName, GMCommandAttribute attribute, MethodInfo methodInfo)
        {
            CommandName = commandName;
            Attribute = attribute;
            MethodInfo = methodInfo;
            Parameters = methodInfo.GetParameters();
        }

        public string CommandName { get; }

        public GMCommandAttribute Attribute { get; }

        public MethodInfo MethodInfo { get; }

        public ParameterInfo[] Parameters { get; }
    }

    public class GMSystem
    {
        public static GMSystem Instance;
        private readonly Dictionary<string, GMCommandMetadata> _GMCommandMetadataByName = new Dictionary<string, GMCommandMetadata>(StringComparer.OrdinalIgnoreCase);

        public void Init(List<Assembly> assemblies)
        {
            _CollectGMCommandsByReflection(assemblies);
            if (UIManager.Instance != null)
            {
                UIManager.Instance.GMCommandDispatched += OnGMCommandDispatched;
            }
        }

        public void UnInit()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.GMCommandDispatched -= OnGMCommandDispatched;
            }

            _GMCommandMetadataByName.Clear();
        }

        private void _CollectGMCommandsByReflection(List<Assembly> assemblies)
        {
            _GMCommandMetadataByName.Clear();
            if (assemblies == null || assemblies.Count == 0)
            {
                DELogger.Warn("GMSystem", "No assemblies provided for GM command collection.");
                return;
            }

            foreach (var assembly in assemblies)
            {
                if (assembly == null)
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                    DELogger.Warn("GMSystem", $"Load types from assembly '{assembly.FullName}' partially failed: {exception.Message}");
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    foreach (var method in methods)
                    {
                        var attribute = method.GetCustomAttribute<GMCommandAttribute>(false);
                        if (attribute == null)
                        {
                            continue;
                        }

                        if (method.ContainsGenericParameters)
                        {
                            DELogger.Warn("GMSystem", $"Ignore generic GM command method '{type.FullName}.{method.Name}'.");
                            continue;
                        }

                        var commandName = string.IsNullOrWhiteSpace(attribute.CommandName)
                            ? method.Name
                            : attribute.CommandName.Trim();
                        if (string.IsNullOrWhiteSpace(commandName))
                        {
                            DELogger.Warn("GMSystem", $"Ignore GM command method '{type.FullName}.{method.Name}' because command name is empty.");
                            continue;
                        }

                        if (_GMCommandMetadataByName.ContainsKey(commandName))
                        {
                            DELogger.Warn("GMSystem", $"Duplicate GM command name '{commandName}' found on '{type.FullName}.{method.Name}'.");
                            continue;
                        }

                        _GMCommandMetadataByName.Add(commandName, new GMCommandMetadata(commandName, attribute, method));
                    }
                }
            }

            DELogger.Info("GMSystem", $"{_GMCommandMetadataByName.Count} GM command(s) collected.");
        }

        private void OnGMCommandDispatched(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                DELogger.Warn("GMSystem", "Received empty GM command.");
                return;
            }

            if (!_TryTokenizeCommand(command, out var tokens, out var tokenizeErrorMessage))
            {
                DELogger.Error("GMSystem", tokenizeErrorMessage);
                return;
            }

            if (tokens.Count == 0)
            {
                DELogger.Warn("GMSystem", "Received empty GM command.");
                return;
            }

            var commandName = tokens[0];
            if (_TryHandleBuiltinCommand(tokens))
            {
                return;
            }

            if (!_GMCommandMetadataByName.TryGetValue(commandName, out var gmCommandMetadata))
            {
                DELogger.Warn("GMSystem", $"GM command '{commandName}' not found.");
                return;
            }

            if (!_TryBuildInvocationArguments(gmCommandMetadata, tokens, out var invocationArguments, out var argumentErrorMessage))
            {
                DELogger.Error("GMSystem", argumentErrorMessage);
                return;
            }

            if (!_TryResolveInvocationTarget(gmCommandMetadata.MethodInfo, out var invocationTarget, out var targetErrorMessage))
            {
                DELogger.Error("GMSystem", targetErrorMessage);
                return;
            }

            try
            {
                var invocationResult = gmCommandMetadata.MethodInfo.Invoke(invocationTarget, invocationArguments);
                if (gmCommandMetadata.MethodInfo.ReturnType == typeof(void))
                {
                    DELogger.Info("GMSystem", $"GM command '{gmCommandMetadata.CommandName}' executed.");
                    return;
                }

                var resultMessage = invocationResult == null ? "null" : invocationResult.ToString();
                DELogger.Info("GMSystem", $"GM command '{gmCommandMetadata.CommandName}' executed. Result: {resultMessage}");
            }
            catch (TargetInvocationException exception)
            {
                var innerException = exception.InnerException ?? exception;
                DELogger.Error("GMSystem", $"Execute GM command '{gmCommandMetadata.CommandName}' failed: {innerException}");
            }
            catch (Exception exception)
            {
                DELogger.Error("GMSystem", $"Execute GM command '{gmCommandMetadata.CommandName}' failed: {exception}");
            }
        }

        private bool _TryBuildInvocationArguments(GMCommandMetadata gmCommandMetadata, List<string> tokens, out object[] invocationArguments, out string errorMessage)
        {
            var parameters = gmCommandMetadata.Parameters;
            var providedArgumentCount = tokens.Count - 1;
            var requiredArgumentCount = 0;
            foreach (var parameter in parameters)
            {
                if (!parameter.IsOptional)
                {
                    requiredArgumentCount++;
                }
            }

            if (providedArgumentCount < requiredArgumentCount || providedArgumentCount > parameters.Length)
            {
                invocationArguments = null;
                errorMessage = $"GM command '{gmCommandMetadata.CommandName}' argument count mismatch. Usage: {_BuildUsage(gmCommandMetadata)}";
                return false;
            }

            invocationArguments = new object[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                var tokenIndex = index + 1;
                if (tokenIndex >= tokens.Count)
                {
                    invocationArguments[index] = parameter.DefaultValue;
                    continue;
                }

                if (!_TryParseArgument(tokens[tokenIndex], parameter.ParameterType, out var parsedArgument, out var parseErrorMessage))
                {
                    errorMessage = $"GM command '{gmCommandMetadata.CommandName}' parse argument '{parameter.Name}' failed: {parseErrorMessage}. Usage: {_BuildUsage(gmCommandMetadata)}";
                    invocationArguments = null;
                    return false;
                }

                invocationArguments[index] = parsedArgument;
            }

            errorMessage = null;
            return true;
        }

        private bool _TryHandleBuiltinCommand(List<string> tokens)
        {
            var commandName = tokens[0];
            if (string.Equals(commandName, "help", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Count == 1)
                {
                    _LogAllCommandUsages();
                    return true;
                }

                var targetCommandName = tokens[1];
                if (!_GMCommandMetadataByName.TryGetValue(targetCommandName, out var gmCommandMetadata))
                {
                    DELogger.Warn("GMSystem", $"GM command '{targetCommandName}' not found.");
                    return true;
                }

                _LogSingleCommandUsage(gmCommandMetadata);
                return true;
            }

            if (!string.Equals(commandName, "search", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (tokens.Count < 2 || string.IsNullOrWhiteSpace(tokens[1]))
            {
                DELogger.Warn("GMSystem", "Usage: search <prefix>");
                return true;
            }

            _LogSearchResult(tokens[1]);
            return true;
        }

        private bool _TryParseArgument(string rawValue, Type parameterType, out object parsedArgument, out string errorMessage)
        {
            var targetType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
            if (string.Equals(rawValue, "null", StringComparison.OrdinalIgnoreCase) && Nullable.GetUnderlyingType(parameterType) != null)
            {
                parsedArgument = null;
                errorMessage = null;
                return true;
            }

            if (targetType == typeof(string))
            {
                parsedArgument = rawValue;
                errorMessage = null;
                return true;
            }

            if (targetType == typeof(bool))
            {
                if (string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawValue, "yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawValue, "on", StringComparison.OrdinalIgnoreCase))
                {
                    parsedArgument = true;
                    errorMessage = null;
                    return true;
                }

                if (string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawValue, "no", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawValue, "off", StringComparison.OrdinalIgnoreCase))
                {
                    parsedArgument = false;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Boolean value";
                return false;
            }

            if (targetType.IsEnum)
            {
                try
                {
                    parsedArgument = Enum.Parse(targetType, rawValue, true);
                    errorMessage = null;
                    return true;
                }
                catch (Exception)
                {
                    parsedArgument = null;
                    errorMessage = $"'{rawValue}' is not a valid {targetType.Name} value";
                    return false;
                }
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Int32 value";
                return false;
            }

            if (targetType == typeof(long))
            {
                if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Int64 value";
                return false;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Single value";
                return false;
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Double value";
                return false;
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Decimal value";
                return false;
            }

            if (targetType == typeof(short))
            {
                if (short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Int16 value";
                return false;
            }

            if (targetType == typeof(byte))
            {
                if (byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Byte value";
                return false;
            }

            if (targetType == typeof(Guid))
            {
                if (Guid.TryParse(rawValue, out var value))
                {
                    parsedArgument = value;
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Guid value";
                return false;
            }

            if (targetType == typeof(char))
            {
                if (rawValue.Length == 1)
                {
                    parsedArgument = rawValue[0];
                    errorMessage = null;
                    return true;
                }

                parsedArgument = null;
                errorMessage = $"'{rawValue}' is not a valid Char value";
                return false;
            }

            try
            {
                parsedArgument = Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
                errorMessage = null;
                return true;
            }
            catch (Exception)
            {
                parsedArgument = null;
                errorMessage = $"Unsupported parameter type '{targetType.FullName}'";
                return false;
            }
        }

        private bool _TryResolveInvocationTarget(MethodInfo methodInfo, out object invocationTarget, out string errorMessage)
        {
            if (methodInfo.IsStatic)
            {
                invocationTarget = null;
                errorMessage = null;
                return true;
            }

            var declaringType = methodInfo.DeclaringType;
            if (declaringType == null)
            {
                invocationTarget = null;
                errorMessage = $"GM command '{methodInfo.Name}' declaring type is null.";
                return false;
            }

            if (declaringType.IsInstanceOfType(this))
            {
                invocationTarget = this;
                errorMessage = null;
                return true;
            }

            var instanceProperty = declaringType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProperty != null && declaringType.IsAssignableFrom(instanceProperty.PropertyType))
            {
                var propertyValue = instanceProperty.GetValue(null, null);
                if (propertyValue != null && declaringType.IsInstanceOfType(propertyValue))
                {
                    invocationTarget = propertyValue;
                    errorMessage = null;
                    return true;
                }
            }

            var instanceField = declaringType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField != null && declaringType.IsAssignableFrom(instanceField.FieldType))
            {
                var fieldValue = instanceField.GetValue(null);
                if (fieldValue != null && declaringType.IsInstanceOfType(fieldValue))
                {
                    invocationTarget = fieldValue;
                    errorMessage = null;
                    return true;
                }
            }

            invocationTarget = null;
            errorMessage = $"GM command '{methodInfo.Name}' requires an instance of '{declaringType.FullName}', but no target object is available.";
            return false;
        }

        private bool _TryTokenizeCommand(string command, out List<string> tokens, out string errorMessage)
        {
            tokens = new List<string>();
            var currentTokenBuilder = new StringBuilder(command.Length);
            var inQuotes = false;
            var quoteCharacter = '\0';
            var escapeNextCharacter = false;

            for (var index = 0; index < command.Length; index++)
            {
                var currentCharacter = command[index];
                if (escapeNextCharacter)
                {
                    currentTokenBuilder.Append(currentCharacter);
                    escapeNextCharacter = false;
                    continue;
                }

                if (currentCharacter == '\\')
                {
                    escapeNextCharacter = true;
                    continue;
                }

                if (inQuotes)
                {
                    if (currentCharacter == quoteCharacter)
                    {
                        inQuotes = false;
                        quoteCharacter = '\0';
                    }
                    else
                    {
                        currentTokenBuilder.Append(currentCharacter);
                    }

                    continue;
                }

                if (currentCharacter == '"' || currentCharacter == '\'')
                {
                    inQuotes = true;
                    quoteCharacter = currentCharacter;
                    continue;
                }

                if (char.IsWhiteSpace(currentCharacter))
                {
                    if (currentTokenBuilder.Length > 0)
                    {
                        tokens.Add(currentTokenBuilder.ToString());
                        currentTokenBuilder.Clear();
                    }

                    continue;
                }

                currentTokenBuilder.Append(currentCharacter);
            }

            if (escapeNextCharacter)
            {
                currentTokenBuilder.Append('\\');
            }

            if (inQuotes)
            {
                errorMessage = $"GM command '{command}' has unclosed quotation mark.";
                tokens = null;
                return false;
            }

            if (currentTokenBuilder.Length > 0)
            {
                tokens.Add(currentTokenBuilder.ToString());
            }

            errorMessage = null;
            return true;
        }

        private string _BuildUsage(GMCommandMetadata gmCommandMetadata)
        {
            var usageBuilder = new StringBuilder(gmCommandMetadata.CommandName);
            foreach (var parameter in gmCommandMetadata.Parameters)
            {
                usageBuilder.Append(' ');
                usageBuilder.Append('<');
                usageBuilder.Append(parameter.ParameterType.Name);
                usageBuilder.Append(' ');
                usageBuilder.Append(parameter.Name);
                if (parameter.IsOptional)
                {
                    usageBuilder.Append(" = ");
                    usageBuilder.Append(parameter.DefaultValue ?? "null");
                }

                usageBuilder.Append('>');
            }

            return usageBuilder.ToString();
        }

        private void _LogAllCommandUsages()
        {
            if (_GMCommandMetadataByName.Count == 0)
            {
                DELogger.Info("GMSystem", "No GM commands registered.");
                return;
            }

            var commandMetadataList = new List<GMCommandMetadata>(_GMCommandMetadataByName.Values);
            commandMetadataList.Sort((left, right) => string.Compare(left.CommandName, right.CommandName, StringComparison.OrdinalIgnoreCase));

            DELogger.Info("GMSystem", "Registered GM commands:");
            foreach (var gmCommandMetadata in commandMetadataList)
            {
                var usage = _BuildUsage(gmCommandMetadata);
                if (string.IsNullOrWhiteSpace(gmCommandMetadata.Attribute.Description))
                {
                    DELogger.Info("GMSystem", usage);
                    continue;
                }

                DELogger.Info("GMSystem", usage + " - " + gmCommandMetadata.Attribute.Description);
            }

            DELogger.Info("GMSystem", "Use 'help <command>' to inspect a specific command.");
        }

        private void _LogSingleCommandUsage(GMCommandMetadata gmCommandMetadata)
        {
            var usage = _BuildUsage(gmCommandMetadata);
            DELogger.Info("GMSystem", usage);
            if (!string.IsNullOrWhiteSpace(gmCommandMetadata.Attribute.Description))
            {
                DELogger.Info("GMSystem", gmCommandMetadata.Attribute.Description);
            }
        }

        private void _LogSearchResult(string prefix)
        {
            var trimmedPrefix = prefix.Trim();
            var matchedCommandMetadataList = new List<GMCommandMetadata>();
            foreach (var gmCommandMetadata in _GMCommandMetadataByName.Values)
            {
                if (!gmCommandMetadata.CommandName.StartsWith(trimmedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matchedCommandMetadataList.Add(gmCommandMetadata);
            }

            if (matchedCommandMetadataList.Count == 0)
            {
                DELogger.Warn("GMSystem", $"No GM commands found for prefix '{trimmedPrefix}'.");
                return;
            }

            matchedCommandMetadataList.Sort((left, right) => string.Compare(left.CommandName, right.CommandName, StringComparison.OrdinalIgnoreCase));

            DELogger.Info("GMSystem", $"Search result for prefix '{trimmedPrefix}':");
            foreach (var gmCommandMetadata in matchedCommandMetadataList)
            {
                var usage = _BuildUsage(gmCommandMetadata);
                if (string.IsNullOrWhiteSpace(gmCommandMetadata.Attribute.Description))
                {
                    DELogger.Info("GMSystem", usage);
                    continue;
                }

                DELogger.Info("GMSystem", usage + " - " + gmCommandMetadata.Attribute.Description);
            }
        }
    }
}
