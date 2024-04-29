using Azure;
using Azure.Data.Tables;

namespace HW6NoteKeeper.Models
{
   /// <summary>
   /// Represents the possible status values for a log entry.
   /// </summary>
   public enum Status
   {
      /// <summary>
      /// The log entry is queued for processing.
      /// </summary>
      Queued,

      /// <summary>
      /// The log entry is currently being processed.
      /// </summary>
      InProgress,

      /// <summary>
      /// The log entry processing has been completed successfully.
      /// </summary>
      Completed,

      /// <summary>
      /// The log entry processing failed.
      /// </summary>
      Failed
   }

   /// <summary>
   /// Represents the input data for a log entry.
   /// </summary>
   public class LogEntryInput
   {
      /// <summary>
      /// Initializes a new instance of the <see cref="LogEntryInput"/> class.
      /// </summary>
      public LogEntryInput()
      {
      }

      /// <summary>
      /// Gets or sets the unique identifier of the zip file entry.
      /// </summary>
      public string? ZipFileId { get; set; }

      /// <summary>
      /// Gets or sets the timestamp of the log entry (nullable).
      /// </summary>
      public DateTimeOffset? Timestamp { get; set; }

      /// <summary>
      /// Gets or sets the status of the log entry.
      /// </summary>
      public string? Status { get; set; }

      /// <summary>
      /// Gets or sets the status details associated with the log entry.
      /// </summary>
      public string? StatusDetails { get; set; }
   }

   /// <summary>
   /// Describes the full result payload for an external log entry
   /// </summary>
   /// <summary>
   /// Represents a log entry in the Azure Table storage.
   /// </summary>
   public class LogEntry : ITableEntity
   {
      /// <summary>
      /// Initializes a new instance of the <see cref="LogEntry"/> class.
      /// </summary>
      public LogEntry()
      {
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="LogEntry"/> class from a <see cref="LogEntryInput"/> object.
      /// </summary>
      /// <param name="logEntryInput">The input data for the log entry.</param>
      public LogEntry(LogEntryInput logEntryInput)
      {
         RowKey = logEntryInput.ZipFileId;
         Timestamp = logEntryInput.Timestamp;
         Status = logEntryInput.Status;
         StatusDetails = logEntryInput.StatusDetails;
      }

      /// <summary>
      /// Gets or sets the status of the log entry.
      /// </summary>
      public string? Status { get; set; }

      /// <summary>
      /// Gets or sets the status details associated with the log entry.
      /// </summary>
      public string? StatusDetails { get; set; }

      /// <summary>
      /// Gets or sets the partition key for the log entry in Azure Table storage (likely the note ID).
      /// </summary>
      public string? PartitionKey { get; set; }

      /// <summary>
      /// Gets or sets the row key for the log entry in Azure Table storage (likely the zip file ID).
      /// </summary>
      public string? RowKey { get; set; }

      /// <summary>
      /// Gets or sets the timestamp for the log entry.
      /// </summary>
      public DateTimeOffset? Timestamp { get; set; }

      /// <summary>
      /// Gets or sets the ETag for the log entry (used for optimistic concurrency control in Azure Table storage).
      /// </summary>
      public ETag ETag { get; set; }
   }

}
