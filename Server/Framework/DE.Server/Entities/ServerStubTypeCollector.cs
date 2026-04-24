using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DE.Server.Entities
{
    public static class ServerStubTypeCollector
    {
        public static List<Type> CollectAllStubTypes(Assembly[] assemblies)
        {
            if (assemblies == null)
            {
                throw new ArgumentNullException(nameof(assemblies));
            }

            var stubBaseType = typeof(ServerStubEntity);
            var stubTypes = new List<Type>();
            var seenTypeKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var assembly in assemblies)
            {
                if (assembly == null)
                {
                    continue;
                }

                foreach (var type in assembly.GetTypes())
                {
                    if (type == null || type.IsAbstract || !stubBaseType.IsAssignableFrom(type))
                    {
                        continue;
                    }

                    var typeKey = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
                    if (seenTypeKeys.Add(typeKey))
                    {
                        stubTypes.Add(type);
                    }
                }
            }

            return stubTypes
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();
        }
    }
}
