using System;

namespace DE.Share.Rpc
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class ServerRpcAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class ClientRpcAttribute : Attribute
    {
    }
}
