using System;
using System.IO;
using System.Text;
using DE.Share.Data.DataDescribe;
using ExcelDataReader;

namespace DE.Share.Data.DataProvider
{
    public sealed class ExcelDataProvider : IDataProvider
    {
        public IDataTableReader OpenTable(string rootDirectory, DataTableDescribe describe)
        {
            if (describe == null)
            {
                throw new ArgumentNullException(nameof(describe));
            }
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("A data root directory is required.", nameof(rootDirectory));
            }
            string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, describe.SourceName.Replace('/', Path.DirectorySeparatorChar) + ".xlsx"));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root, comparison))
            {
                throw new ArgumentException("The data resource must stay within the root directory.", nameof(describe));
            }

            Stream stream = null;
            IExcelDataReader excel = null;
            try
            {
#if DE_SERVER
                // The library's configuration constructor requests cp1252 even for OpenXML.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                excel = ExcelReaderFactory.CreateOpenXmlReader(stream, new ExcelReaderConfiguration
                {
                    FallbackEncoding = Encoding.UTF8,
                    LeaveOpen = false
                });
                return new ExcelDataTableReader(excel, path, describe);
            }
            catch (Exception error)
            {
                if (excel != null)
                {
                    excel.Dispose();
                }
                else
                {
                    stream?.Dispose();
                }
                if (error is DataLoadException)
                {
                    throw;
                }
                throw new DataLoadException($"Could not open table '{describe.TableName}': {error.Message}", path, describe.SheetName, innerException: error);
            }
        }
    }
}
