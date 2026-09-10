namespace DE.Share.Data
{
    internal interface IDataTableState
    {
        bool IsLoading { get; }
        void ThrowIfUnavailable();
        void Detach();
    }
}
