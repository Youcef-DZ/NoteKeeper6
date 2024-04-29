
namespace HW6NoteKeeper
{
    /// <summary>
    /// Defines the limits associated with entities
    /// </summary>
    public class LimitSettings
    {
        /// <summary>
        /// Gets or sets the default visibility in seconds.
        /// </summary>
        /// <value>
        /// The default visibility in seconds.
        /// </value>
        public int DefaultVisibilityInSeconds { get; set; }

        /// <summary>
        /// Gets or sets the default time to live in seconds.
        /// </summary>
        /// <value>
        /// The default time to live in seconds.
        /// </value>
        public int DefaultTimeToLiveInSeconds { get; set; }
    }
}
