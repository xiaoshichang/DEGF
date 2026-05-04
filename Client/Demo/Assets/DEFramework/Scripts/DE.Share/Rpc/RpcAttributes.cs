using System;

namespace DE.Share.Rpc
{
    public static class RpcMethodId
    {
        public static uint Compute(string methodName, params string[] parameterTypeNames)
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
