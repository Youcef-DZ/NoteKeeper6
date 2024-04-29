using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Queues.Models;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using HW6AzureFunctions.CustomSettings;
using System.Text.Json;
using HW6NoteKeeper.Models;

namespace HW6AzureFunctions
{
   /// <summary>
   /// Initializes the compress with the injected dependencies
   /// </summary>
   /// <param name="storageSettings">The settings needed to access storage</param>
   /// <param name="logger">The logger</param>
   public class Function1(IStorageSettings storageSettings,
                            ILogger<Function1> logger)
   {
      private readonly IStorageSettings _storageSettings = storageSettings;
      private readonly ILogger _logger = logger;

      [Function(nameof(Function1))]
      public async Task Run([QueueTrigger("attachment-zip-requests-ex1", Connection = "StorageConnectionString")] QueueMessage message)
      {
         _logger.LogInformation($"C# Queue trigger function processed: {message.MessageText}");
         using JsonDocument document = JsonDocument.Parse(message.MessageText);
         string noteId = document.RootElement.GetProperty("noteId").GetString()!;
         string zipFileId = document.RootElement.GetProperty("zipFileId").GetString()!;
         TableClient _tableClient = new(_storageSettings.ConnectionString, "Jobs");

         try
         {
            BlobContainerClient sourceClient = new(_storageSettings.ConnectionString, noteId);

            _logger.LogWarning("Queued Message containerToCompress: [{containerToCompress}]", noteId);
            // Create the table if it does not exist
            await _tableClient.CreateIfNotExistsAsync();

            LogEntry logEntry = new()
            {
               Status = Status.InProgress.ToString(),
               StatusDetails = $"InProgress: Zip File Id: {zipFileId} NoteId: {noteId}",
               Timestamp = DateTime.UtcNow,
               RowKey = zipFileId,
               PartitionKey = noteId
            };
            await _tableClient.UpsertEntityAsync(logEntry);

            if (!await sourceClient.ExistsAsync())
            {
               _logger.LogError("\tThe note [{noteId}] can't be found for the requested compression operation.", noteId);
               return;
            }

            BlobContainerClient blobContainerClient = GetBlobContainerClient(noteId + "-zip");
            await blobContainerClient.CreateIfNotExistsAsync();
            await blobContainerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            BlobClient targetClient = blobContainerClient.GetBlobClient(zipFileId);


            // Retrieve a list of all the blobs to compress
            Azure.AsyncPageable<BlobItem> blobs = sourceClient.GetBlobsAsync();

            // Create a memory stream to that will contain all of the compressed blobs
            using MemoryStream archiveMemoryStream = new();
            {
               // Create the ZipArchive that will be used to compress all of the blobs
               using ZipArchive zipArchive = new(archiveMemoryStream, ZipArchiveMode.Update, leaveOpen: true);
               {
                  // Loop through all of the blobs and compress them
                  await foreach (var blobPage in blobs.AsPages())
                  {
                     foreach (BlobItem? blobItem in blobPage.Values)
                     {
                        _logger.LogWarning("\tProcessing BlobItem: [{blobItem.Name}]", blobItem.Name);

                        // Get a blob client for the blob to be compressed
                        BlobClient? blobClientToCompress = sourceClient.GetBlobClient(blobItem.Name);
                        using BlobDownloadInfo blobDownloadInfo = await blobClientToCompress.DownloadAsync();
                        {

                           // Create an entry in the zip archive for the blob
                           ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry(blobItem.Name);

                           // Open the archive entry's stream so the content of the blob an be written to it
                           using Stream writer = zipArchiveEntry.Open();
                           // Copy the blob to the zip archive entry so it will be compressed and
                           // stored in the zipArchive
                           await blobDownloadInfo.Content.CopyToAsync(writer);
                           _logger.LogWarning("\t\tCopied to zipArchiveEntry BlobItem: [{blobItem.Name}]", blobItem.Name);

                           // Flush the writer so the content is written to the zip archive entry
                           writer.Flush();
                        }
                     }
                  }
               }

               // Note:
               // Dispose of the zip archive so the memory stream can be used to upload the compressed data to the blob
               // Note: The "using ZipArchive zipArchive = new ZipArchive(archiveMemoryStream, ZipArchiveMode.Update, leaveOpen: true);"
               // does not cause the data to be written to the memory stream until the zipArchive is disposed by explicitly 
               // calling zipArchive.Dispose() 
               zipArchive.Dispose();

               // Reset the stream to the beginning so all of the content in the stream can be
               // written to the blob
               archiveMemoryStream.Position = 0;

               // Upload the compressed data to the blob in azure storage
               await targetClient.UploadAsync(archiveMemoryStream, new BlobHttpHeaders() { ContentType = "application/zip" });
               logEntry = new()
               {
                  Status = Status.Completed.ToString(),
                  StatusDetails = $"Completed: ZipFileId: {zipFileId} containerId: {noteId}-zip",
                  Timestamp = DateTime.UtcNow,
                  RowKey = zipFileId,
                  PartitionKey = noteId
               };
               await _tableClient.UpsertEntityAsync(logEntry);
               _logger.LogWarning("\tUploadAsync BlobItem: [{targeBlobId}]", zipFileId);
            }
         }
         catch (Exception ex)
         {
            LogEntry logEntry = new()
            {
               Status = Status.Failed.ToString(),
               StatusDetails = $"Failed: ZipFileId: {zipFileId} NoteId: {noteId}",
               Timestamp = DateTime.UtcNow,
               RowKey = zipFileId,
               PartitionKey = noteId
            };
            await _tableClient.UpsertEntityAsync(logEntry);
            _logger.LogError(ex.Message);
         }
      }

      /// <summary>
      /// Get the blob client for the fileName and containerName provided
      /// </summary>
      /// <param name="fileName">The file name, which is the blob id.</param>
      /// <param name="containerName">The container name</param>
      /// <returns>A blob client representing the fileName and containerName specified</returns>
      private BlobClient GetBlobClient(string fileName, string containerName)
      {
         BlobContainerClient blobContainerClient = GetBlobContainerClient(containerName);

         return blobContainerClient.GetBlobClient(fileName);
      }

      /// <summary>
      /// Get the container client for the storage account
      /// </summary>
      /// <param name="containerName">The container in Azure Storage</param>
      /// <returns>A BlobContainerClient that is connected the the storage account specified in the AzureWebJobsStorage connection string
      /// for the containerName specified</returns>
      private BlobContainerClient GetBlobContainerClient(string containerName)
      {
         BlobContainerClient blobContainerClient = new(_storageSettings.ConnectionString, containerName);
         return blobContainerClient;
      }

   }
}
