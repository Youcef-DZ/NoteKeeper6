namespace HW6AzureFunctions.CustomSettings
{
    /// <summary>
    /// Defines the storage settings used to access Azure Storage
    /// </summary>
    public interface IStorageSettings
    {
        /// <summary>
        /// Implementer shall implement the connection string property to
        /// store the Azure Storage connection string
        /// </summary>
        public string? ConnectionString { get; set; }
    }
}
