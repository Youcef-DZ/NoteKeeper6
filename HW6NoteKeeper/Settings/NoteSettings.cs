namespace HW6NoteKeeper.Settings
{
    /// <summary>
    /// Defines the notes limit settings
    /// </summary>
    public class NoteSettings
    {
        /// <summary>
        /// Gets or sets the maximum number of notes.
        /// </summary>
        /// <value>
        /// The maximum number of notes.
        /// </value>
        /// <remarks>Setting a default max notes if non provided in config</remarks>
        public int MaxNotes { get; set; } = 10;

        /// <summary>
        /// Gets or sets the maximum number of attachments.
        /// </summary>
        /// <value>
        /// The maximum number of attachments.
        /// </value>
        /// <remarks>Setting a default max attachments if non provided in config</remarks>
        public int MaxAttachments { get; set; } = 3;
    }
}
