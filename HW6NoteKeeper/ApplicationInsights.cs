namespace HW6NoteKeeper
{
    /// <summary>
    /// Application insights settings
    /// </summary>
    public class ApplicationInsights
    {
        /// <summary>
        /// The API Authentication key for quick pulse telemetry
        /// </summary>
        public string? AuthenticationApiKey { get; set; }

        /// <summary>
        /// Adaptive sampling reduces the amount of logging but not all entries are logged, this reduces expense
        /// </summary>
        /// <value>True is enabled and some entries will be missed, false is not enabled all items are logged increasing expense</value>
        public bool EnableAdaptiveSampling { get; set; } = true;

    }
}
