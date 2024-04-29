using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HW6NoteKeeper.Models
{
    /// <summary>
    /// Represents a note entity.
    /// </summary>
    public class Note
    {
        /// <summary>
        /// Unique identifier for the note.
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string? Id { get; set; }

        /// <summary>
        /// Summary of the note.
        /// </summary>
        [Required]
        [StringLength(60, MinimumLength = 1)]
        public string? Summary { get; set; }

        /// <summary>
        /// Details of the note.
        /// </summary>
        [Required]
        [StringLength(1024, MinimumLength = 1)]
        public string? Details { get; set; }

        /// <summary>
        /// Creation date and time of the note in UTC.
        /// </summary>
        public DateTimeOffset CreatedDateUtc { get; set; }

        /// <summary>
        /// Modification date and time of the note in UTC.
        /// </summary>
        public DateTimeOffset? ModifiedDateUtc { get; set; }
    }
}
