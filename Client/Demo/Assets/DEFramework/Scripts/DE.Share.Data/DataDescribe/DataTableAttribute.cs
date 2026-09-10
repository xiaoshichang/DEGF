using System;

namespace DE.Share.Data
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DataTableAttribute : Attribute
    {
        public DataTableAttribute(string tableName)
        {
            TableName = tableName;
        }

        /// <summary>
        /// 数据表的逻辑名称，例如 SpaceData；在同一数据运行时内必须唯一。
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// 数据文件相对于数据根目录的路径，不含扩展名，例如 World/SpaceData。
        /// 未指定时，SG 使用 <see cref="TableName"/>；文件扩展名由 Provider 决定。
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Excel 工作表名称，区分大小写；未指定时，SG 使用 <see cref="TableName"/>。
        /// </summary>
        public string Sheet { get; set; }

        public DataLoadPolicy Load { get; set; } = DataLoadPolicy.Full;
        public DataCachePolicy Cache { get; set; } = DataCachePolicy.KeepAlive;
        public int CacheCapacity { get; set; } = 1024;
    }
}
