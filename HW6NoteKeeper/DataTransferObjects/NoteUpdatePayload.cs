using Swashbuckle.AspNetCore.Filters;
using System.ComponentModel.DataAnnotations;

namespace HW6NoteKeeper.DataTransferObjects
{
    public class NoteUpdatePayload
    {
        /// <summary>
        /// Gets or sets the summary of the note.
        /// </summary>
        [StringLength(60, MinimumLength = 1)]
        public string? Summary { get; set; }

        /// <summary>
        /// Gets or sets the details of the note.
        /// </summary>
        [StringLength(1024, MinimumLength = 1)]
        public string? Details { get; set; }
    }

    /// <summary>
    /// Provides examples for the NoteUpdatePayload class.
    /// </summary>
    public class NoteUpdatePayloadExample : IExamplesProvider<NoteUpdatePayload>
    {
        /// <summary>
        /// Gets the example payload for NoteUpdatePayload.
        /// </summary>
        /// <returns>
        /// An example instance of NoteUpdatePayload.
        /// </returns>
        public NoteUpdatePayload GetExamples()
        {
            return new NoteUpdatePayload
            {
                Summary = "<a summary of the note>",
                Details = "<the details of the note>"
            };
        }
    }
}
