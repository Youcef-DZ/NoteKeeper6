using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using HW6NoteKeeper.Database;
using HW6NoteKeeper.DataTransferObjects;
using HW6NoteKeeper.Models;
using HW6NoteKeeper.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;


namespace HW6NoteKeeper.Controllers
{
   /// <summary>
   /// Controller for managing notes.
   /// </summary>
   /// <remarks>
   /// Initializes a new instance of the <see cref="NotesController"/> class.
   /// </remarks>
   [ApiController]
   [Route("/")]
   [Produces("application/json")]
   public class NotesController(ILogger<NotesController> logger, DatabaseContext context, NoteSettings noteSettings, TelemetryClient telemetryClient, string storageConnectionString) : ControllerBase
   {
      private readonly ILogger _logger = logger;
      private const string GetNoteByIdRoute = "GetNoteByIdRoute";
      private readonly DatabaseContext _context = context;
      private readonly NoteSettings _noteSettings = noteSettings;
      private readonly TelemetryClient _telemetryClient = telemetryClient;
      private readonly QueueClient _queueClient = new(storageConnectionString, "attachment-zip-requests-ex1");
      private readonly TableClient _tableClient = new(storageConnectionString, "Jobs");
      private readonly BlobServiceClient blobServiceClient = new(storageConnectionString);

      private const string AddAttachmentRouteName = nameof(AddAttachmentRouteName);
      private const string CustomMetadata = nameof(CustomMetadata);

      /// <summary>
      /// Creates an attachments zip file for a specific note.
      /// </summary>
      /// <param name="noteId">The ID of the note.</param>
      /// <returns>An IActionResult representing the result of the creation operation.</returns>
      [ProducesResponseType(typeof(string), StatusCodes.Status201Created)]
      [ProducesResponseType(StatusCodes.Status400BadRequest)]
      [Route("notes/{noteId}/attachmentzipfiles/")]
      [HttpPost]
      public async Task<IActionResult> CreateAttachmentsZipFile(string noteId)
      {
         try
         {
            // Check if the note exists
            Note? noteEntity = (from c in _context.Notes where c.Id == noteId select c).SingleOrDefault();
            if (noteEntity == null)
            {
               TrackTraceToAppInsights("NotFound", $"The note {noteId} does not exist.");
               _logger.LogWarning("The note {noteId} does not exist.", noteId);

               return NotFound();
            }

            // Generate a unique zip file ID
            var message = new
            {
               noteId,
               zipFileId = $"{Guid.NewGuid()}.zip"
            };

            // Create the queue if it doesn't exist
            if (null != await _queueClient.CreateIfNotExistsAsync())
            {
               _logger.LogInformation("The queue was created.");
            }

            // Create the table if it does not exist
            await _tableClient.CreateIfNotExistsAsync();

            LogEntry logEntry = new()
            {
               Status = Status.Queued.ToString(),
               StatusDetails = $"Queued: Zip File Id: {message.zipFileId} NoteId: {noteId}",
               Timestamp = DateTime.UtcNow,
               RowKey = message.zipFileId,
               PartitionKey = noteId
            };

            // Send the message to the queue
            Response<SendReceipt> response = await _queueClient.SendMessageAsync(Base64Encode(JsonSerializer.Serialize(message)));
            if (response.GetRawResponse().Status == StatusCodes.Status201Created)
            {
               await _tableClient.AddEntityAsync(logEntry);
               var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
               var zipUrl = $"{baseUrl}/notes/{noteId}/attachmentzipfiles/{message.zipFileId}";

               // Return the URL in the response
               return Accepted(zipUrl, null);
            }

            return StatusCode(response.GetRawResponse().Status);
         }
         catch (RequestFailedException ex)
         {
            return StatusCode(ex.Status);
         }
      }


      /// <summary>
      /// Retrieves the archive by its ID for a specific note.
      /// </summary>
      /// <param name="noteId">The ID of the note.</param>
      /// <param name="zipFileId">The ID of the archive.</param>
      /// <returns>An IActionResult representing the result of the retrieval operation.</returns>
      [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
      [ProducesResponseType(StatusCodes.Status404NotFound)]
      [HttpGet]
      [Route("notes/{noteId}/attachmentzipfiles/{zipFileId}")]
      public async Task<IActionResult> GetArchiveById(string noteId, string zipFileId)
      {
         // Get the BlobContainerClient for the specified note
         BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(noteId + "-zip");

         // Set the access policy of the container to allow public access to blobs
         await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

         // Check if the container exists
         if (!await containerClient.ExistsAsync())
         {
            TrackTraceToAppInsights("NotFound", $"The Container {noteId}-zip does not exist.");
            return NotFound($"The Container {noteId}-zip does not exist.");
         }

         // Get the BlobClient for the specified zip file
         BlobClient blobClient = containerClient.GetBlobClient(zipFileId);

         // Check if the blob exists
         if (await blobClient.ExistsAsync())
         {
            // Get the properties of the blob
            BlobProperties properties = await blobClient.GetPropertiesAsync();

            // Download the blob content into a MemoryStream
            MemoryStream memoryStream = new();
            await blobClient.DownloadToAsync(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            // Create a FileContentResult and set the FileDownloadName to the zipFileId
            return new FileContentResult(memoryStream.ToArray(), properties.ContentType)
            {
               FileDownloadName = zipFileId
            };
         }
         else
         {
            TrackTraceToAppInsights("NotFound", $"The Container {noteId}-zip does not contain the blob named {zipFileId}");
            return NotFound($"The Container {noteId}-zip does not contain the blob named {zipFileId}");
         }
      }


      /// <summary>
      /// Deletes an attachment zip file for a specific note.
      /// </summary>
      /// <param name="noteId">The ID of the note.</param>
      /// <param name="zipFileId">The ID of the attachment zip file.</param>
      /// <returns>An IActionResult representing the result of the deletion operation.</returns>
      [ProducesDefaultResponseType]
      [HttpDelete]
      [Route("notes/{noteId}/attachmentzipfiles/{zipFileId}", Name = "DeleteAttachmentZipFile")]
      public async Task<IActionResult> DeleteAttachmentZipFile(string noteId, string zipFileId)
      {
         try
         {
            // Retrieve the blob service client and container client
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(noteId + "-zip");

            // Verify if the container exists
            if (!await containerClient.ExistsAsync())
            {
               TrackTraceToAppInsights("NotFound", $"The container {noteId}-zip does not exist.");
               _logger.LogWarning("The container {noteId}-zip does not exist.", noteId);

               return NotFound($"The container {noteId}-zip does not exist.");
            }

            // Retrieve the blob client for the specified attachment id
            BlobClient blobClient = containerClient.GetBlobClient(zipFileId);

            // Verify if the blob exists
            if (!await blobClient.ExistsAsync())
            {
               TrackTraceToAppInsights("NotFound", $"The attachment {zipFileId} does not exist.");
               _logger.LogWarning("The attachment {zipFileId} does not exist.", zipFileId);

               return NotFound($"The attachment {zipFileId} does not exist.");
            }

            // Retrieve the note entity
            Note? noteEntity = (from c in _context.Notes where c.Id == noteId select c).SingleOrDefault();
            if (noteEntity == null)
            {
               TrackTraceToAppInsights("NotFound", $"The note {noteId} does not exist.");
               _logger.LogWarning("The note {noteId} does not exist.", noteId);

               return NotFound();
            }

            // Delete the blob
            await blobClient.DeleteAsync();
            _logger.LogInformation("NoteId: {noteId}, zipFileId: {zipFileId} Deleted", noteId, zipFileId);

            // Return no content to indicate successful deletion
            return NoContent();
         }
         catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, zipFileId: {zipFileId}");
            _logger.LogWarning("NoteId: {noteId}, zipFileId: {zipFileId}. [{msg}]", noteId, zipFileId, ex.Message);

            return NotFound($"The attachment {zipFileId} does not exist.");
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, zipFileId: {zipFileId}");
            _logger.LogError("NoteId: {noteId}, zipFileId: {zipFileId}. [{msg}]", noteId, zipFileId, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
      }


      /// <summary>
      /// Retrieves the attachment zip files for a specific note.
      /// </summary>
      /// <param name="noteId">The ID of the note.</param>
      /// <returns>A list of attachment zip files for the note.</returns>
      [HttpGet(template: "notes/{noteId}/attachmentzipfiles/", Name = "GetNoteAttachmentsZipFiles")]
      [ProducesResponseType(statusCode: StatusCodes.Status200OK, type: typeof(FileResult))]
      public async Task<IActionResult> GetNoteAttachmentsZipFiles(string noteId)
      {
         try
         {
            Note? noteEntity = (from c in _context.Notes where c.Id == noteId select c).SingleOrDefault();
            if (noteEntity == null)
            {
               TrackTraceToAppInsights("NotFound", $"The note {noteId} does not exist.");
               _logger.LogWarning("The note {noteId} does not exist.", noteId);

               return NotFound();
            }

            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(noteId + "-zip");
            // Verify if the container exists
            if (!await containerClient.ExistsAsync())
            {
               TrackTraceToAppInsights("NotFound", $"The container {noteId}-zip does not exist.");
               _logger.LogWarning("The container {noteId}-zip does not exist.", noteId);

               return NotFound($"The container {noteId}-zip does not exist.");
            }

            List<BlobFile> attachmentZipFiles = [];

            await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
            {
               BlobFile file = new()
               {
                  AttachmentId = blobItem.Name,
                  ContentType = blobItem.Properties.ContentType,
                  Created = blobItem.Properties.CreatedOn,
                  LastModified = blobItem.Properties.LastModified,
                  Length = blobItem.Properties.ContentLength ?? 0
               };

               attachmentZipFiles.Add(file);
            }

            return Ok(attachmentZipFiles);
         }
         catch (RequestFailedException ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");
            _logger.LogError("NoteId: {noteId}. [{msg}]", noteId, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");
            _logger.LogError("NoteId: {noteId}. [{msg}]", noteId, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
      }

      /// <summary>
      /// Gets the archive status for a specific attachment zip file associated with a note.
      /// </summary>
      /// <param name="noteId">The unique identifier of the note.</param>
      /// <param name="zipFileId">The unique identifier of the attachment zip file.</param>
      /// <returns>An IActionResult containing an object with ZipFileId, TimeStamp, Status, and StatusDetails properties 
      /// if the note and zip file entry are found, 
      /// or a NotFound result if the note or zip file entry is not found, 
      /// or an InternalServerError if an exception occurs.</returns>
      [HttpGet(template: "notes/{noteId}/attachmentzipfiles/jobs/{zipFileId}", Name = "GetArchiveStatus")]
      [ProducesResponseType(statusCode: StatusCodes.Status200OK, type: typeof(FileResult))]
      public IActionResult GetArchiveStatus(string noteId, string zipFileId)
      {
         // Try to find the note entity based on the provided noteId
         try
         {
            Note? noteEntity = (from c in _context.Notes where c.Id == noteId select c).SingleOrDefault();

            // Check if the note exists
            if (noteEntity == null)
            {
               TrackTraceToAppInsights("NotFound", $"The note {noteId} does not exist.");
               _logger.LogWarning("The note {noteId} does not exist.", noteId);

               return NotFound();
            }

            // Use Table Client to get the log entry for the specific zipFileId within the note
            var queryResult = _tableClient.GetEntity<LogEntry>(partitionKey: noteId, rowKey: zipFileId);

            // Check if the log entry was found (status code 200 indicates success)
            if (queryResult.GetRawResponse().Status == StatusCodes.Status200OK)
            {
               // Create a new anonymous object with relevant properties from the logEntry
               var result = new
               {
                  ZipFileId = zipFileId,
                  TimeStamp = queryResult.Value.Timestamp,
                  Status = queryResult.Value.Status,
                  queryResult.Value.StatusDetails
               };

               // Return the data as an ObjectResult
               return new ObjectResult(result);
            }

            // Log entry not found, return NotFound
            return NotFound();
         }
         // Catch specific exception for failed requests (e.g., network errors)
         catch (RequestFailedException ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");
            _logger.LogError("NoteId: {noteId}. [{message}]", noteId, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
         // Catch any other exception
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");
            _logger.LogError("NoteId: {noteId}. [{message}]", noteId, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
      }


      /// <summary>
      /// Gets the archive status for all attachment zip files associated with a specific note.
      /// </summary>
      /// <param name="noteId">The unique identifier of the note.</param>
      /// <returns>An IActionResult containing a list of objects with ZipFileId, TimeStamp, Status, and StatusDetails properties, 
      /// or a NotFound result if the note or log entries are not found, 
      /// or an InternalServerError if an exception occurs.</returns>
      [HttpGet(template: "notes/{noteId}/attachmentzipfiles/jobs", Name = "GetAllArchiveStatus")]
      [ProducesResponseType(statusCode: StatusCodes.Status200OK, type: typeof(FileResult))]
      public async Task<IActionResult> GetAllArchiveStatusAsync(string noteId)
      {
         // Try to find the note entity based on the provided noteId
         try
         {
            Note? noteEntity = (from c in _context.Notes where c.Id == noteId select c).SingleOrDefault();

            // Check if the note exists
            if (noteEntity == null)
            {
               TrackTraceToAppInsights("NotFound", $"The note {noteId} does not exist.");
               _logger.LogWarning("The note {noteId} does not exist.", noteId);

               return NotFound();
            }

            // Initialize an empty list to store log entries
            List<LogEntryInput> logEntries = [];

            // Asynchronously iterate over log entries for the specific note
            await foreach (LogEntry logEntry in _tableClient.QueryAsync<LogEntry>(entity => entity.PartitionKey == noteId))
            {
               // Create a new LogEntryInput object
               var logEntryInput = new LogEntryInput
               {
                  // Map properties from logEntry to LogEntryInput
                  ZipFileId = logEntry.RowKey,
                  Timestamp = logEntry.Timestamp?.UtcDateTime, // Assuming you want UTC time
                  Status = logEntry.Status,
                  StatusDetails = logEntry.StatusDetails
               };

               // Add the processed logEntry data to the list
               logEntries.Add(logEntryInput);
            }

            // Check if any log entries were found
            if (logEntries.Count == 0)
            {
               return NotFound();
            }

            // Return the list of log entries as an ObjectResult
            return new ObjectResult(logEntries);
         }
         // Catch specific exception for failed requests (e.g., network errors)
         catch (RequestFailedException ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");
            _logger.LogError("NoteId: {noteId}. [{message}]", noteId, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
         // Catch any other exception
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");
            _logger.LogError("NoteId: {noteId}. [{message}]", noteId, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
      }


      /// <summary>
      /// Uploads a file
      /// </summary>
      /// <param name="attachmentId">Name of the public file.</param>
      /// <param name="noteId">Note Guid.</param>
      /// <param name="fileData">File to upload.</param>
      /// <returns>
      /// The name of the blob entry created
      /// </returns>
      /// <remarks>The noteId is the Id of the blob</remarks>
      [ProducesDefaultResponseType]
      [HttpPut]
      [Route("notes/{noteId}/attachments/{attachmentId}", Name = "AddAttachmentRoute")]
      public async Task<IActionResult> AddAttachment(string attachmentId, string noteId, IFormFile fileData)
      {
         try
         {
            Note? noteEntity = (from c in _context.Notes where c.Id == noteId select c).SingleOrDefault();
            if (noteEntity == null)
            {
               TrackTraceToAppInsights("NotFound", $"The note {noteId} does not exist.");

               return NotFound();
            }

            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(noteId);

            await containerClient.CreateIfNotExistsAsync();
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            //  verify that the maximum notes limit has not been reached
            bool canAddMore = await CanAddMoreAttachments(containerClient);
            if (!canAddMore)
            {
               ProblemDetails problemDetails = new()
               {
                  Status = StatusCodes.Status403Forbidden,
                  Title = "Attachments limit reached",
                  Detail = $"Attachment limit reached MaxAttachments: [{_noteSettings.MaxAttachments}]"
               };
               return StatusCode((int)HttpStatusCode.Forbidden, problemDetails);
            }

            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);
            bool fileExists = await blobClient.ExistsAsync();
            BlobProperties properties = new();

            using (Stream uploadedFileStream = fileData.OpenReadStream())
            {
               // Upload the blob
               await blobClient.UploadAsync(uploadedFileStream, new BlobUploadOptions
               {
                  HttpHeaders = new BlobHttpHeaders
                  {
                     ContentType = fileData.ContentType // Set ContentType based on the IFormFile's ContentType
                  }
               });

               // Set or update metadata
               properties = await blobClient.GetPropertiesAsync();
               if (properties.Metadata.ContainsKey("NoteId"))
               {
                  properties.Metadata["NoteId"] = noteId;
               }
               else
               {
                  properties.Metadata.Add("NoteId", noteId);
               }
               await blobClient.SetMetadataAsync(properties.Metadata);
            }

            if (fileExists)
            {
               TrackAttachmentEvent("AttachmentUpdated", attachmentId, properties.ContentLength);
               return NoContent();
            }
            else
            {
               TrackAttachmentEvent("AttachmentCreated", attachmentId, properties.ContentLength);
            }

            // Construct the full URL including the attachment file name
            var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
            var attachmentUrl = $"{baseUrl}/notes/{noteId}/attachments/{attachmentId}";

            // Return the URL in the response
            return Created(attachmentUrl, null);
         }
         catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
         {
            TrackExceptionToAppInsights(ex, $"The note {noteId} does not exist.");
            return NotFound($"The note {noteId} does not exist.");
         }
         catch (RequestFailedException ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, AttachmentId: {attachmentId}");
            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, AttachmentId: {attachmentId}");
            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }

      }


      /// <summary>
      /// Get an Attachment By Blob Id
      /// </summary>
      /// <param name="attachmentId">The file uploaded.</param>
      /// <param name="noteId">Name of the public file.</param>
      /// <returns>
      /// The file to download
      /// </returns>
      /// <summary>
      /// Gets the specified Attachment file by note ID.
      /// </summary>
      [HttpGet(template: "notes/{noteId}/attachments/{attachmentId}", Name = "GetAttachmentByIdRouteName")]
      [ProducesResponseType(statusCode: StatusCodes.Status200OK, type: typeof(FileResult))]
      public async Task<IActionResult> GetAttachmentById(string noteId, string attachmentId)
      {
         try
         {
            if (string.IsNullOrWhiteSpace(noteId) || string.IsNullOrWhiteSpace(attachmentId))
            {
               TrackTraceToAppInsights("BadRequest", "Both noteId and attachmentId must be provided.");
               return BadRequest("Both noteId and attachmentId must be provided.");
            }

            if (string.IsNullOrWhiteSpace(storageConnectionString))
            {
               TrackTraceToAppInsights("NotFound", "Storage connection string is not configured.");
               return new BadRequestObjectResult("Storage connection string is not configured.");
            }

            Note? noteEntity = (from c in _context.Notes where c.Id == noteId select c).SingleOrDefault();
            if (noteEntity == null)
            {
               TrackTraceToAppInsights("NotFound", $"The note {noteId} does not exist.");
               return NotFound($"The note {noteId} does not exist.");
            }

            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(noteId);

            await containerClient.CreateIfNotExistsAsync();
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            if (!(await containerClient.ExistsAsync()))
            {
               TrackTraceToAppInsights("NotFound", $"The Container {noteId} does not exist.");
               return NotFound($"The Container {noteId} does not exist.");
            }

            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);

            if (await blobClient.ExistsAsync())
            {
               BlobProperties properties = await blobClient.GetPropertiesAsync();

               MemoryStream memoryStream = new();
               await blobClient.DownloadToAsync(memoryStream);
               memoryStream.Seek(0, SeekOrigin.Begin);

               // Create a FileContentResult and set the FileDownloadName to the attachmentId
               return new FileContentResult(memoryStream.ToArray(), properties.ContentType)
               {
                  FileDownloadName = attachmentId
               };
            }
            else
            {
               TrackTraceToAppInsights("NotFound", $"The Container {noteId} does not contain the blob named {attachmentId}");
               return NotFound($"The Container {noteId} does not contain the blob named {attachmentId}");
            }
         }
         catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, AttachmentId: {attachmentId}");
            return NotFound($"The blob {attachmentId} does not exist.");
         }
         catch (RequestFailedException ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, AttachmentId: {attachmentId}");
            return new ObjectResult(ex.Message) { StatusCode = (int)HttpStatusCode.InternalServerError };
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, AttachmentId: {attachmentId}");
            return new ObjectResult(ex.Message) { StatusCode = (int)HttpStatusCode.InternalServerError };
         }

      }

      /// <summary>
      /// Deletes the specified attachment from the note based on the provided noteId and attachmentId.
      /// </summary>
      /// <param name="noteId">The ID of the note containing the attachment.</param>
      /// <param name="attachmentId">The ID of the attachment to be deleted.</param>
      /// <returns>
      /// No content if the attachment was successfully deleted.
      /// NotFound if the specified note or attachment does not exist.
      /// InternalServerError if an unexpected error occurred.
      /// </returns>
      [ProducesDefaultResponseType]
      [HttpDelete]
      [Route("notes/{noteId}/attachments/{attachmentId}", Name = "DeleteAttachmentRoute")]
      public async Task<IActionResult> DeleteAttachment(string noteId, string attachmentId)
      {
         try
         {
            // Retrieve the blob service client and container client
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(noteId);

            // Verify if the container exists
            if (!await containerClient.ExistsAsync())
            {
               TrackTraceToAppInsights("NotFound", $"The attachment {attachmentId} does not exist.");

               return NotFound($"The container {noteId} does not exist.");
            }

            // Retrieve the blob client for the specified attachment id
            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);

            // Verify if the blob exists
            if (!await blobClient.ExistsAsync())
            {
               TrackTraceToAppInsights("NotFound", $"The attachment {attachmentId} does not exist.");

               return NotFound($"The attachment {attachmentId} does not exist.");
            }

            // Delete the blob
            await blobClient.DeleteAsync();

            // Return no content to indicate successful deletion
            return NoContent();
         }
         catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, AttachmentId: {attachmentId}");

            return NotFound($"The attachment {attachmentId} does not exist.");
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}, AttachmentId: {attachmentId}");

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
      }


      /// <summary>
      /// Retrieves the attachments for the specified note based on the provided noteId.
      /// </summary>
      /// <param name="noteId">The ID of the note for which attachments are to be retrieved.</param>
      /// <returns>Returns the list of attachments for the specified note.</returns>
      [HttpGet(template: "notes/{noteId}/attachments/", Name = "GetNoteAttachments")]
      [ProducesResponseType(statusCode: StatusCodes.Status200OK, type: typeof(FileResult))]
      public async Task<IActionResult> GetNoteAttachments(string noteId)
      {
         try
         {
            if (string.IsNullOrWhiteSpace(noteId))
            {
               return BadRequest("The noteId must be provided");
            }

            if (string.IsNullOrWhiteSpace(storageConnectionString))
            {
               return BadRequest("Storage connection string is not configured.");
            }

            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(noteId);

            await containerClient.CreateIfNotExistsAsync();
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            if (!await containerClient.ExistsAsync())
            {
               return NotFound($"The container {noteId} does not exist.");
            }

            List<BlobFile> attachments = [];

            await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
            {
               BlobFile file = new()
               {
                  AttachmentId = blobItem.Name,
                  ContentType = blobItem.Properties.ContentType,
                  Created = blobItem.Properties.CreatedOn,
                  LastModified = blobItem.Properties.LastModified,
                  Length = blobItem.Properties.ContentLength ?? 0
               };

               attachments.Add(file);
            }

            return Ok(attachments);
         }
         catch (RequestFailedException ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {noteId}");

            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
         }
      }

      /// <summary>
      /// Retrieves all notes.
      /// </summary>
      /// <remarks>
      /// Retrieves all notes stored in the system.
      /// </remarks>
      /// <response code="200">If the notes are successfully retrieved.</response>
      /// <response code="400">If the request is invalid.</response>
      /// <response code="500">If an error occurs while processing the request.</response>
      [HttpGet]
      [ProducesResponseType(typeof(List<Note>), 200)]
      [ProducesResponseType(typeof(string), 400)]
      [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
      [Route("notes")]
      public IActionResult GetAllNotes()
      {
         Stopwatch stopwatch = Stopwatch.StartNew();

         try
         {
            IQueryable<Note> query = _context.Notes;
            query = _context.Notes;

            List<Note> notes = [.. query.Select(c => c)];

            return new ObjectResult(notes);
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, "No input payload for GetAllNotes method");

            return StatusCode((int)HttpStatusCode.InternalServerError);
         }
      }

      /// <summary>
      /// Retrieves a note by its ID.
      /// </summary>
      /// <remarks>
      /// Retrieves the note with the specified ID from the system.
      /// </remarks>
      /// <param name="id">The ID of the note to be retrieved.</param>
      /// <response code="200">If the note is successfully retrieved.</response>
      /// <response code="400">If the request is invalid.</response>
      /// <response code="404">If the note with the specified ID is not found.</response>
      /// <response code="500">If an error occurs while processing the request.</response>
      [HttpGet]
      [ProducesResponseType(typeof(NoteResult), 200)]
      [ProducesResponseType(typeof(string), 400)]
      [ProducesResponseType((int)HttpStatusCode.NotFound)]
      [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
      [Route("notes/{id}", Name = GetNoteByIdRoute)]
      public IActionResult GetNoteById(string id)
      {
         try
         {
            Note? note = _context.Notes.Where(c => c.Id == id).Select(c => c).SingleOrDefault();
            if (note == null)
            {
               return NotFound();
            }

            return new ObjectResult(new NoteResult(note));
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {id}");

            return StatusCode((int)HttpStatusCode.InternalServerError);
         }
      }

      /// <summary>
      /// Creates a new note.
      /// </summary>
      /// <remarks>
      /// Creates a new note using the provided payload.
      /// </remarks>
      /// <param name="noteCreatePayload">The payload containing the information for the new note.</param>
      /// <response code="201">If the note is successfully created.</response>
      /// <response code="400">If the request is invalid.</response>
      /// <response code="403">If the maximum note limit has been reached.</response>
      /// <response code="500">If an error occurs while processing the request.</response>
      [ProducesResponseType(typeof(NoteResult), (int)HttpStatusCode.Created)]
      [ProducesResponseType((int)HttpStatusCode.BadRequest)]
      [ProducesResponseType((int)HttpStatusCode.Forbidden)]
      [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
      [Route("notes")]
      [HttpPost]
      public IActionResult CreateNote([FromBody] NoteCreatePayload noteCreatePayload)
      {
         Note noteEntity = new();

         try
         {
            if (ModelState.IsValid)
            {
               // First verify that the maximum notes limit has not been reached
               if (!CanAddMoreNotes())
               {
                  ProblemDetails problemDetails = new()
                  {
                     Status = StatusCodes.Status403Forbidden,
                     Title = "Note limit reached",
                     Detail = $"Note limit reached MaxNote: [{_noteSettings.MaxNotes}]"
                  };
                  return StatusCode((int)HttpStatusCode.Forbidden, problemDetails);
               }

               noteEntity.Summary = noteCreatePayload.Summary;
               noteEntity.Details = noteCreatePayload.Details;
               noteEntity.CreatedDateUtc = DateTime.UtcNow;
               noteEntity.ModifiedDateUtc = null;

               // Tell entity framework to add the address entity
               _context.Notes.Add(noteEntity);

               int result = _context.SaveChanges();
            }
            else
            {
               TrackTraceToAppInsights("Invalid input payload for creating note", $"Summary: {noteCreatePayload.Summary}, Details: {noteCreatePayload.Details}");
               return BadRequest(ModelState);
            }
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteCreatePayload: {noteCreatePayload}");

            return StatusCode((int)HttpStatusCode.InternalServerError);
         }
         finally
         {
            if (noteEntity != null)
            {
               TrackEventToAppInsights("NoteCreated", noteEntity);
            }
            else
            {
               TrackTraceToAppInsights("NoteEntity is null in CreateNote method", "Unable to create note entity");
            }
         }

         return CreatedAtRoute(GetNoteByIdRoute, new { id = noteEntity.Id }, new NoteResult(noteEntity));
      }

      /// <summary>
      /// Updates a note by its ID.
      /// </summary>
      /// <remarks>
      /// Updates the note with the specified ID using the provided payload.
      /// </remarks>
      /// <param name="id">The ID of the note to be updated.</param>
      /// <param name="noteUpdatePayload">The payload containing the updated note information.</param>
      /// <response code="204">If the note is successfully updated.</response>
      /// <response code="201">If the note is successfully updated and a new resource is created.</response>
      /// <response code="400">If the request is invalid.</response>
      /// <response code="404">If the note with the specified ID is not found.</response>
      /// <response code="500">If an error occurs while processing the request.</response>
      [ProducesResponseType((int)HttpStatusCode.NoContent)]
      [ProducesResponseType(typeof(NoteResult), (int)HttpStatusCode.Created)]
      [ProducesResponseType((int)HttpStatusCode.BadRequest)]
      [ProducesResponseType((int)HttpStatusCode.NotFound)]
      [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
      [Route("notes/{id}")]
      [HttpPatch]
      public IActionResult UpdateNote(string id, [FromBody] NoteUpdatePayload noteUpdatePayload)
      {
         Note? noteEntity = new();
         try
         {
            if (ModelState.IsValid)
            {
               noteEntity = (from c in _context.Notes where c.Id == id select c).SingleOrDefault();
               bool saveToDB = false;
               if (noteEntity == null)
               {
                  return NotFound();
               }

               if (!string.IsNullOrWhiteSpace(noteUpdatePayload.Summary))
               {
                  noteEntity.Summary = noteUpdatePayload.Summary;
                  saveToDB = true;
               }

               if (!string.IsNullOrWhiteSpace(noteUpdatePayload.Details))
               {
                  noteEntity.Details = noteUpdatePayload.Details;
                  saveToDB = true;
               }

               if (saveToDB)
               {
                  noteEntity.ModifiedDateUtc = DateTime.UtcNow;
                  _context.SaveChanges();
               }
            }
            else
            {
               TrackTraceToAppInsights("Invalid input payload for creating note", $"Summary: {noteUpdatePayload.Summary}, Details: {noteUpdatePayload.Details}");
               return BadRequest(ModelState);
            }
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {id}, NoteUpdatePayload: {noteUpdatePayload}");

            return StatusCode((int)HttpStatusCode.InternalServerError);
         }
         finally
         {
            if (noteEntity != null)
            {
               TrackEventToAppInsights("NoteUpdated", noteEntity);
            }
            else
            {
               TrackTraceToAppInsights("NoteEntity is null in UpdateNote method", $"NoteId: {id}");
            }
         }

         return NoContent();
      }


      /// <summary>
      /// Deletes a note by its ID.
      /// </summary>
      /// <remarks>
      /// Deletes the note with the specified ID from the system.
      /// </remarks>
      /// <param name="id">The ID of the note to be deleted.</param>
      /// <response code="204">If the note is successfully deleted.</response>
      /// <response code="400">If the request is invalid.</response>
      /// <response code="404">If the note with the specified ID is not found.</response>
      /// <response code="500">If an error occurs while processing the request.</response>
      [ProducesResponseType((int)HttpStatusCode.NoContent)]
      [ProducesResponseType((int)HttpStatusCode.BadRequest)]
      [ProducesResponseType((int)HttpStatusCode.NotFound)]
      [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
      [Route("notes/{id}")]
      [HttpDelete]
      public async Task<IActionResult> DeleteNoteByIdAsync(string id)
      {
         try
         {
            // Retrieve the note from the database
            Note? dbNote = _context.Notes.FirstOrDefault(c => c.Id == id);

            if (dbNote == null)
            {
               return NotFound();
            }

            // Delete the associated attachments
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(id);
            if (containerClient.Exists())
            {
               try
               {
                  containerClient.Delete();
               }
               catch (Exception ex)
               {
                  // Log an error message if deletion fails
                  _logger.LogError($"Error deleting container {id}: {ex.Message}");
               }
            }
            else
            {
               _logger.LogInformation($"No attachments found for NoteId: {id}");
            }

            // Delete zip files the associated attachments
            containerClient = blobServiceClient.GetBlobContainerClient(id + "-zip");
            if (containerClient.Exists())
            {
               try
               {
                  containerClient.Delete();
               }
               catch (Exception ex)
               {
                  // Log an error message if deletion fails
                  _logger.LogError($"Error deleting container {id}-zip: {ex.Message}");
               }
            }
            else
            {
               _logger.LogInformation($"No zip files attachments found for NoteId: {id}");
            }

            // Delete all rows associated with the NoteId 
            bool entitiesFound = false;
            await foreach (LogEntry logEntry in _tableClient.QueryAsync<LogEntry>(entity => entity.PartitionKey == id))
            {
               entitiesFound = true;

               // Check for ongoing compression before deletion 
               if (logEntry?.Status?.ToString() == "InProgress")
               {
                  // Log a conflict and return a 409 status code
                  _logger.LogWarning($"Delete request for PartitionKey: {id} conflicts with ongoing note attachment compression.");
                  return Conflict(); 
               }

               try
               {
                  // Delete the entire entity using the retrieved logEntry object
                  _ = await _tableClient.DeleteEntityAsync(logEntry?.PartitionKey, logEntry?.RowKey);
               }
               catch (Exception ex)
               {
                  // Log an error message if deletion fails
                  _logger.LogError($"Error deleting entity from Azure Storage Job table: {ex.Message}");
               }
            }

            if (!entitiesFound)
            {
               // Log an informational message if no entities were found
               _logger.LogInformation($"No entities found in Azure Storage Job table for NoteId: {id}");
            }

            using var transaction = _context.Database.BeginTransaction();

            try
            {
               // Delete the note
               _context.Notes.Remove(dbNote);

               // Save changes
               _context.SaveChanges();
               transaction.Commit();
            }
            catch (Exception ex)
            {
               transaction.Rollback();
               TrackExceptionToAppInsights(ex, $"NoteId: {id}");
               _logger.LogError("NoteId: {noteId} [{msg}]", id, ex.Message);

               return StatusCode((int)HttpStatusCode.InternalServerError);
            }
         }
         catch (Exception ex)
         {
            TrackExceptionToAppInsights(ex, $"NoteId: {id}");
            _logger.LogError("NoteId: {noteId} [{msg}]", id, ex.Message);

            return StatusCode((int)HttpStatusCode.InternalServerError);
         }

         return NoContent();
      }

      /// <summary>
      /// Encodes the specified plain text using Base64 encoding.
      /// </summary>
      /// <param name="plainText">The plain text to encode.</param>
      /// <returns>The Base64 encoded string.</returns>
      private static string Base64Encode(string plainText)
      {
         var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
         return Convert.ToBase64String(plainTextBytes);
      }

      /// <summary>
      /// Determines whether this more notes can be added.
      /// </summary>
      /// <returns>
      ///   true if more notes can be added false if not
      /// </returns>
      private bool CanAddMoreNotes()
      {
         long totalNotes = (from c in _context.Notes select c).Count();

         return _noteSettings.MaxNotes > totalNotes;
      }

      /// <summary>
      /// Determines whether this more notes can be added.
      /// </summary>
      /// <returns>
      ///   true if more notes can be added false if not
      /// </returns>
      private async Task<bool> CanAddMoreAttachments(BlobContainerClient containerClient)
      {
         // Check the count of blobs in the container
         int blobCount = 0;
         await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
         {
            blobCount++;
         }

         return _noteSettings.MaxAttachments > blobCount;
      }

      /// <summary>
      /// Tracks an event to Application Insights with the provided event name and note entity properties and metrics.
      /// </summary>
      /// <param name="eventName">The name of the event to track.</param>
      /// <param name="attachmentid"></param>
      /// <param name="attachmentSize"></param>
      private void TrackAttachmentEvent(string eventName, string attachmentid, double attachmentSize)
      {
         _telemetryClient.TrackEvent(eventName,
             properties: new Dictionary<string, string>()
             {
               { "AttachmentId", attachmentid },
             },
             metrics: new Dictionary<string, double>()
             {
               { "AttachmentSize", attachmentSize }
             });
      }


      /// <summary>
      /// Tracks an event to Application Insights with the provided event name and note entity properties and metrics.
      /// </summary>
      /// <param name="eventName">The name of the event to track.</param>
      /// <param name="noteEntity">The note entity for which the event is being tracked.</param>
      private void TrackEventToAppInsights(string eventName, Note noteEntity)
      {
         _telemetryClient.TrackEvent(eventName,
             properties: new Dictionary<string, string>()
             {
               { "Summary", noteEntity.Summary ?? "" },
             },
             metrics: new Dictionary<string, double>()
             {
               { "SummaryLength", noteEntity.Summary?.Length ?? 0 },
               { "DetailsLength", noteEntity.Details?.Length ?? 0 }
             });
      }

      /// <summary>
      /// Tracks a trace to Application Insights with the provided error message and input payload.
      /// </summary>
      /// <param name="errorMessage">The error message to track.</param>
      /// <param name="inputPayload">The input payload associated with the trace.</param>
      private void TrackTraceToAppInsights(string errorMessage, string inputPayload)
      {
         _telemetryClient.TrackTrace(errorMessage,
             new Dictionary<string, string>
             {
               { "InputPayload", inputPayload }
             });
      }

      /// <summary>
      /// Tracks an exception to Application Insights with the provided exception and input payload.
      /// </summary>
      /// <param name="ex">The exception to track.</param>
      /// <param name="inputPayload">The input payload associated with the exception.</param>
      private void TrackExceptionToAppInsights(Exception ex, string inputPayload)
      {
         _telemetryClient.TrackException(ex, new Dictionary<string, string>
        {
            { "InputPayload", inputPayload }
        });
      }

   }
}