namespace HW6AzureFunctions.CustomSettings
{
    /// <summary>
    /// Defines the storage settings used to access Azure Storage
    /// </summary>
    public class StorageSettings : IStorageSettings
    {
        /// <summary>
        /// Stores the Azure Storage connection string
        /// </summary>
        public string? ConnectionString { get; set; }
    }
}
