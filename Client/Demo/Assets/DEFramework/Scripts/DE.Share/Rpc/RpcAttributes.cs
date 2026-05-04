using System;

namespace DE.Share.Rpc
{
    public static class RpcMethodId
    {
        public static uint Compute(string methodName, params object[] args)
        {
            return ComputeByParameterTypeNames(methodName, GetParameterTypeNames(args));
        }

        public static uint ComputeByParameterTypeNames(string methodName, params string[] parameterTypeNames)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                throw new ArgumentException("RPC method name cannot be empty.", nameof(methodName));
            }

            string signature = methodName + "(" + string.Join(",", parameterTypeNames ?? Array.Empty<string>()) + ")";
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offsetBasis;
            foreach (char character in signature)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash == 0 ? 1u : hash;
        }

        private static string[] GetParameterTypeNames(object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] typeNames = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                object arg = args[i];
                if (arg == null || arg is string)
                {
                    typeNames[i] = "string";
                    continue;
                }

                if (arg is int)
                {
                    typeNames[i] = "int";
                    continue;
                }

                if (arg is EntityProxy)
                {
                    typeNames[i] = "DE.Share.Rpc.EntityProxy";
                    continue;
                }

                throw new NotSupportedException("Unsupported RPC argument type: " + arg.GetType().FullName);
            }

            return typeNames;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class ServerRpcAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class ClientRpcAttribute : Attribute
    {
    }
}
