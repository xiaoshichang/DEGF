using System;
using System.Runtime.CompilerServices;
using DE.Share.Data.DataDescribe;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    internal static class DataTableFactory<TTable> where TTable : class, IDataTable
    {
        private static DataTableDescribe _Describe;
        private static Func<IDataProvider, string, TTable> _Loader;

        internal static DataTableDescribe Describe
        {
            get
            {
                EnsureInitialized();
                return _Describe;
            }
        }

        internal static void Initialize(DataTableDescribe describe, Func<IDataProvider, string, TTable> loader)
        {
            if (describe == null)
            {
                throw new ArgumentNullException(nameof(describe));
            }
            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }
            if (_Loader != null)
            {
                throw new InvalidOperationException($"The factory for table '{typeof(TTable).FullName}' is already initialized.");
            }
            _Describe = describe;
            _Loader = loader;
        }

        internal static TTable Load(IDataProvider provider, string rootDirectory)
        {
            EnsureInitialized();
            return _Loader(provider, rootDirectory);
        }

        private static void EnsureInitialized()
        {
            if (_Loader != null)
            {
                return;
            }
            RuntimeHelpers.RunClassConstructor(typeof(TTable).TypeHandle);
            if (_Loader == null)
            {
                throw new InvalidOperationException($"Table type '{typeof(TTable).FullName}' has no generated data table factory.");
            }
        }
    }
}
