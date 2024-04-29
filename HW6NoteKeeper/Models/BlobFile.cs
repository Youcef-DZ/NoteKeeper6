namespace HW6NoteKeeper.Models
{
   public class BlobFile
   {
      /// <summary>
      /// Gets or sets the blob name.
      /// </summary>
      /// <value>The blob's name.</value>
      public string? AttachmentId { get; set; }
      /// <summary>
      /// Gets or sets the blob type.
      /// </summary>
      /// <value>The blob's type.</value>
      public string? ContentType { get; set; }

      /// <summary>
      /// The date time the blob was created
      /// </summary>
      public DateTimeOffset? Created { get; set; }

      /// <summary>
      /// The date time the blob was last modified
      /// </summary>
      public DateTimeOffset? LastModified { get; set; }

      /// <summary>
      /// The length of the blob
      /// </summary>
      public long Length { get; set; }
   }
}

