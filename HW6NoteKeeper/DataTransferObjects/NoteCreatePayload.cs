using Swashbuckle.AspNetCore.Filters;
using System.ComponentModel.DataAnnotations;

namespace HW6NoteKeeper.DataTransferObjects
{
    /// <summary>
    /// Represents the payload for creating a new note.
    /// </summary>
    /// <remarks>
    /// This payload is used when creating a new note in the system.
    /// </remarks>
    public class NoteCreatePayload
    {
        /// <summary>
        /// Gets or sets the summary of the note.
        /// </summary>
        [Required]
        [StringLength(60, MinimumLength = 1)]
        public string? Summary { get; set; }

        /// <summary>
        /// Gets or sets the details of the note.
        /// </summary>
        [Required]
        [StringLength(1024, MinimumLength = 1)]
        public string? Details { get; set; }
    }

    /// <summary>
    /// Provides examples for the NoteCreatePayload class.
    /// </summary>
    public class NoteCreatePayloadExample : IExamplesProvider<NoteCreatePayload>
    {
        /// <summary>
        /// Gets the example payload for NoteCreatePayload.
        /// </summary>
        /// <returns>
        /// An example instance of NoteCreatePayload.
        /// </returns>
        public NoteCreatePayload GetExamples()
        {
            return new NoteCreatePayload
            {
                Summary = "<a summary of the note>",
                Details = "<the details of the note>"
            };
        }
    }
}
