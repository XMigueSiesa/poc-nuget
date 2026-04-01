using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pos.SharedKernel.Observability;

public static class LoggingExtensions
{
    /// <summary>
    /// Configures structured JSON logging for production (Cloud Run captures stdout).
    /// In development uses the default colored console formatter.
    /// </summary>
    public static IHostBuilder ConfigureProductionLogging(this IHostBuilder host)
    {
        return host.ConfigureLogging((context, logging) =>
        {
            logging.ClearProviders();

            if (context.HostingEnvironment.IsProduction())
            {
                // JSON formatter — each log line is a single JSON object.
                // Cloud Logging (Cloud Run) parses this automatically.
                logging.AddJsonConsole(opts =>
                {
                    opts.IncludeScopes = true;
                    opts.TimestampFormat = "o"; // ISO 8601
                    opts.UseUtcTimestamp = true;
                });
            }
            else
            {
                logging.AddSimpleConsole(opts =>
                {
                    opts.IncludeScopes = true;
                    opts.TimestampFormat = "HH:mm:ss ";
                });
            }
        });
    }
}
