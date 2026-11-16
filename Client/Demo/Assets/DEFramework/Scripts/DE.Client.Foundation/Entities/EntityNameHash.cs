using System;
using System.Text;

namespace DE.Client.Entities
{
    /// <summary>协议名称采用 UTF-8 FNV-1a 64 位哈希；大小写敏感，双端不得使用随机化的 string.GetHashCode。</summary>
    public static class EntityNameHash
    {
        public static ulong Compute(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (byte value in Encoding.UTF8.GetBytes(name))
                {
                    hash = (hash ^ value) * 1099511628211UL;
                }
                return hash;
            }
        }
    }
}
