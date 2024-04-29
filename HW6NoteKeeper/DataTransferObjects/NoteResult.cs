using HW6NoteKeeper.Models;

namespace HW6NoteKeeper.DataTransferObjects
{
    /// <summary>
    /// Defines the public facing note attributes
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NoteResult"/> class using a Note as input.
    /// </remarks>
    /// <param name="note">The note.</param>
    public class NoteResult(Note note)
    {
        /// <summary>
        /// Gets or sets the note identifier.
        /// </summary>
        /// <value>The identifier.</value>
        public string Id { get; set; } = note.Id ?? "-1";

        /// <summary>
        /// Gets or sets the note's summary.
        /// </summary>
        /// <value>The name.</value>
        public string Summary { get; set; } = note.Summary ?? "-1";

        /// <summary>
        /// Gets or sets the note's details.
        /// </summary>
        /// <value>The details.</value>
        public string Details { get; set; } = note.Details ?? "-1";

        /// <summary>
        /// Gets or sets the created date.
        /// </summary>
        /// <value>The age.</value>
        public DateTimeOffset CreatedDateUtc { get; set; } = note.CreatedDateUtc;

        /// <summary>
        /// Gets or sets the modified date.
        /// </summary>
        /// <value>The age.</value>
        public DateTimeOffset? ModifiedDateUtc { get; set; } = note.ModifiedDateUtc;
    }

}
