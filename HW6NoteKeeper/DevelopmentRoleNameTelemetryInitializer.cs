using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace HW6NoteKeeper
{
    /// <summary>
    /// Initializes the telemetry with the development role name.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="DevelopmentRoleNameTelemetryInitializer"/> class.
    /// </remarks>
    /// <param name="env">The web host environment.</param>
    public class DevelopmentRoleNameTelemetryInitializer(IWebHostEnvironment env) : ITelemetryInitializer
    {
        private readonly IWebHostEnvironment _env = env;

        /// <summary>
        /// Initializes the telemetry.
        /// </summary>
        /// <param name="telemetry">The telemetry to initialize.</param>
        public void Initialize(ITelemetry telemetry)
        {
            if (_env.IsDevelopment())
            {
                // Set the role name to the machine name if running in development environment
                telemetry.Context.Cloud.RoleName = Environment.MachineName;
            }
        }
    }

}
