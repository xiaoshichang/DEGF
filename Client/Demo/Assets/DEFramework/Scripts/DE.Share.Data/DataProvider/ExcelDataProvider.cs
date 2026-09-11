using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using DE.Share.Data.DataDescribe;
using ExcelDataReader;

namespace DE.Share.Data.DataProvider
{
    public sealed class ExcelDataProvider : IDataProvider
    {
        private const string CleanupExceptionKey = "DE.Share.Data.ExcelCleanupException";
        private string boundRoot;
        private DataTableDescribe boundDescribe;
        private Stream stream;
        private ExcelDataTableReader tableReader;
        private ExceptionDispatchInfo openFailure;
        private bool disposed;

        public IDataTableReader OpenTable(string rootDirectory, DataTableDescribe describe)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExcelDataProvider));
            }
            if (describe == null)
            {
                throw new ArgumentNullException(nameof(describe));
            }
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("A data root directory is required.", nameof(rootDirectory));
            }
            string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (boundDescribe != null)
            {
                if (!ReferenceEquals(boundDescribe, describe) || !string.Equals(boundRoot, root, comparison))
                {
                    throw new InvalidOperationException("The data provider is already bound to another table or root directory.");
                }
                openFailure?.Throw();
                return tableReader;
            }
            string path = Path.GetFullPath(Path.Combine(root, describe.SourceName.Replace('/', Path.DirectorySeparatorChar) + ".xlsx"));
            if (!path.StartsWith(root, comparison))
            {
                throw new ArgumentException("The data resource must stay within the root directory.", nameof(describe));
            }

            boundRoot = root;
            boundDescribe = describe;
            Stream openedStream = null;
            IExcelDataReader excel = null;
            try
            {
#if DE_SERVER
                // The library's configuration constructor requests cp1252 even for OpenXML.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                openedStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                excel = ExcelReaderFactory.CreateOpenXmlReader(openedStream, new ExcelReaderConfiguration
                {
                    FallbackEncoding = Encoding.UTF8,
                    LeaveOpen = true
                });
                tableReader = new ExcelDataTableReader(excel, path, describe);
                stream = openedStream;
                return tableReader;
            }
            catch (Exception error)
            {
                Exception failure = error is DataLoadException ? error :
                    new DataLoadException($"Could not open table '{describe.TableName}': {error.Message}", path, describe.SheetName, innerException: error);
                openFailure = ExceptionDispatchInfo.Capture(failure);
                DisposeResource(excel, failure);
                DisposeResource(openedStream, failure);
                openFailure.Throw();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            var ownedReader = tableReader;
            var ownedStream = stream;
            tableReader = null;
            stream = null;
            Exception failure = DisposeResource(ownedReader, null);
            failure = DisposeResource(ownedStream, failure);
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static Exception DisposeResource(IDisposable resource, Exception failure)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception error)
            {
                if (failure == null)
                {
                    return error;
                }
                var previousCleanup = failure.Data[CleanupExceptionKey] as Exception;
                failure.Data[CleanupExceptionKey] = previousCleanup == null ? error :
                    new AggregateException(previousCleanup, error);
            }
            return failure;
        }
    }
}
