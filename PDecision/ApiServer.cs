using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using PokerSuite.Shared.Constants;
using PokerSuite.Shared.Helpers;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace PDecision
{
    /// <summary>
    /// Wire format of the /api/health response. A named type with explicit JSON names, not an anonymous type:
    /// an anonymous type's properties are serialized by their (compiler-generated) name, which an obfuscator
    /// would rename, silently changing the API's JSON field names.
    /// </summary>
    internal class HealthResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("casino")]
        public string? Casino { get; set; }

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonPropertyName("handHistoryUploadToAzure")]
        public bool HandHistoryUploadToAzure { get; set; }

        [JsonPropertyName("handHistoryCasino")]
        public string? HandHistoryCasino { get; set; }

        [JsonPropertyName("handHistoryPlatform")]
        public string? HandHistoryPlatform { get; set; }

        // every casino the PC plays, for a client of one of its other poker sites
        [JsonPropertyName("casinos")]
        public List<HealthCasino>? Casinos { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        // true while a new PDecision waits on its staging port to take over the live one (see Api:TakeoverUrls)
        [JsonPropertyName("standby")]
        public bool? Standby { get; set; }
    }

    internal class HealthCasino
    {
        [JsonPropertyName("casino")]
        public string Casino { get; set; } = "";

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = "";
    }

    /// <summary>
    /// Options for <see cref="HealthResponse"/>: omits "error" when null, the same way
    /// [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] would, but as a plain runtime object
    /// instead of a custom attribute's named argument. An obfuscator that renames any property in this module
    /// can corrupt the metadata of that specific attribute shape (observed: Obfuscar 2.2 /
    /// System.Reflection.CustomAttributeFormatException: 'Condition' property specified was not found), so
    /// this sidesteps it entirely rather than depending on that attribute still working post-obfuscation.
    /// </summary>
    internal static class HealthResponseJson
    {
        public static readonly System.Text.Json.JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <summary>
    /// Wire format of a plain {"error": "CONFIGURE_LOCALLY"} response. Same reasoning as <see cref="HealthResponse"/>.
    /// </summary>
    internal class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// Settings from the "Api" section of appsettings.json.
    /// </summary>
    internal class ApiSettings
    {
        public string Urls { get; set; } = "http://127.0.0.1:5005";

        // Swap without downtime: when set (e.g. "http://127.0.0.1:5005"), the service runs on Urls (a staging port),
        // loads everything and waits; POST /api/takeover on the staging port then binds these addresses as soon as
        // the old PDecision has released them. Background work that would clash with it starts only then.
        public string TakeoverUrls { get; set; } = "";

        // when set, every request except /api/health must send it in the X-Api-Key header
        public string ApiKey { get; set; } = "";

        // legacy named pipes RequestDM-N / RequestHH-N, off by default
        public bool EnableNamedPipes { get; set; } = false;

        // human-like wait before each decision reply; switch off for simulations and tests
        public bool HumanLikeDelays { get; set; } = true;
        public static ApiSettings Load()
        {
            return SettingsLoader.Load<ApiSettings>("Api");
        }
    }

    /// <summary>
    /// Settings from the "HandHistory" section of appsettings.json.
    /// </summary>
    internal class HandHistorySettings
    {
        // false keeps hand histories out of the shared Azure pool, e.g. for tests
        public bool UploadToAzure { get; set; } = true;

        // when set, every saved hand is also written to LocalCopyDir/<casino>/<handId>_<hero>.txt
        public string LocalCopyDir { get; set; } = "";

        // download the hands of the customer's other machines into the local stats DBs every N minutes, 0 = off
        public int DownloadIntervalMinutes { get; set; } = 5;

        public static HandHistorySettings Load()
        {
            return SettingsLoader.Load<HandHistorySettings>("HandHistory");
        }
    }

    /// <summary>
    /// Settings from the "Database" section of appsettings.json.
    /// </summary>
    internal class DatabaseSettings
    {
        // Azure SQL pokerdb with the restricted login pdecision_client (sql/PDecisionClientUser.sql)
        public string ConnectionString { get; set; } = "";

        // re-read casino settings, bot accounts (with profiles) and winners every N minutes while running, 0 = only at startup
        public int RefreshIntervalMinutes { get; set; } = 10;

        public static DatabaseSettings Load()
        {
            return SettingsLoader.Load<DatabaseSettings>("Database");
        }
    }

    internal class MachineSettings
    {
        // One registered adapter of this PC (AA-BB-CC-DD-EE-FF) when the PC has several registrations, e.g. a
        // second PDecision for another casino: PDECISION_Machine__MacAddress. Empty: the lowest registered MachineId.
        public string MacAddress { get; set; } = "";

        public static MachineSettings Load()
        {
            return SettingsLoader.Load<MachineSettings>("Machine");
        }
    }

    internal static class SettingsLoader
    {
        /// <summary>
        /// Reads a section of appsettings.json. Environment variables override it, e.g. PDECISION_HandHistory__UploadToAzure=false.
        /// </summary>
        public static T Load<T>(string section) where T : new()
        {
            var settings = new T();
            new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables("PDECISION_")
                .Build()
                .GetSection(section)
                .Bind(settings);
            return settings;
        }
    }

    internal enum ServiceState
    {
        Initializing,
        Ready,
        Failed,
    }

    /// <summary>
    /// Status code and JSON body of one API reply.
    /// </summary>
    internal readonly record struct ApiResult(int StatusCode, string Body);

    /// <summary>
    /// REST API for bot clients: decisions and the hand-history feed.
    /// </summary>
    internal class ApiServer
    {
        private const string apiKeyHeader = "X-Api-Key";
        private readonly Logger logger = LogHelpers.GetStandardLogger(enServiceType.PDecision, Assembly.GetExecutingAssembly());
        private readonly Controller controller;
        private readonly ApiSettings settings;
        private WebApplication app;

        public ApiServer(Controller controller, ApiSettings settings)
        {
            this.controller = controller;
            this.settings = settings;
        }

        public async Task<bool> StartAsync(bool quiet = false)
        {
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
                builder.Logging.ClearProviders();
                builder.WebHost.UseUrls(settings.Urls);
                app = builder.Build();

                app.Use(async (context, next) =>
                {
                    if (!string.IsNullOrEmpty(settings.ApiKey) && context.Request.Path != "/api/health" &&
                        context.Request.Headers[apiKeyHeader] != settings.ApiKey)
                    {
                        await WriteAsync(context, new ApiResult(StatusCodes.Status401Unauthorized, Error("missing or wrong X-Api-Key header")));
                        return;
                    }
                    await next();
                });

                app.MapGet("/api/health", (HttpContext context) =>
                {
                    try
                    {
                        return WriteAsync(context, Health());
                    }
                    catch (Exception ex)
                    {
                        // builder.Logging.ClearProviders() above means ASP.NET Core would otherwise swallow this
                        // silently (blank 500, nothing logged anywhere)
                        logger.Error($"Exception in /api/health: {ex}");
                        throw;
                    }
                });

                if (controller.Standby)
                {
                    app.MapPost("/api/takeover", async (HttpContext context) =>
                    {
                        await WriteAsync(context, await controller.TakeoverAsync());
                    });
                }

                app.MapPost("/api/decision", async (HttpContext context) =>
                {
                    if (!IsReady(context, out var notReady))
                    {
                        await WriteAsync(context, notReady);
                        return;
                    }
                    string body = await ReadBodyAsync(context);
                    // a decision includes the human-like wait (Thread.Sleep), so it gets its own thread instead of a thread-pool thread
                    ApiResult result = await Task.Factory.StartNew(() => controller.GetDecision(body), TaskCreationOptions.LongRunning);
                    await WriteAsync(context, result);
                });

                app.MapPost("/api/handhistory", async (HttpContext context) =>
                {
                    if (!IsReady(context, out var notReady))
                    {
                        await WriteAsync(context, notReady);
                        return;
                    }
                    string body = await ReadBodyAsync(context);
                    await WriteAsync(context, controller.AddHandState(body));
                });

                app.MapPost("/api/handhistory/complete", async (HttpContext context) =>
                {
                    if (!IsReady(context, out var notReady))
                    {
                        await WriteAsync(context, notReady);
                        return;
                    }
                    string body = await ReadBodyAsync(context);
                    await WriteAsync(context, controller.CompleteHand(body));
                });

                await app.StartAsync();
                logger.Info($"REST API listening on {settings.Urls}, api key required: {!string.IsNullOrEmpty(settings.ApiKey)}");
                return true;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    logger.Error($"Failed to start REST API on {settings.Urls}: {ex}");
                }
                app = null;
                return false;
            }
        }

        public async Task StopAsync()
        {
            if (app != null)
            {
                await app.StopAsync();
            }
        }

        public static string Error(string message)
        {
            return System.Text.Json.JsonSerializer.Serialize(new ErrorResponse { Error = message });
        }

        private ApiResult Health()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            string status = controller.State.ToString().ToLowerInvariant();
            int code = controller.State == ServiceState.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
            var response = new HealthResponse
            {
                Status = status,
                Version = version,
                Casino = controller.Casino?.ToString(),
                Platform = controller.Platform is enPlatform platform ? MachineConfig.PlatformName(platform) : null,
                HandHistoryUploadToAzure = controller.HandHistoryUploadToAzure,
                HandHistoryCasino = controller.HandHistoryCasino?.ToString(),
                HandHistoryPlatform = controller.HandHistoryPlatform is enPlatform hhPlatform ? MachineConfig.PlatformName(hhPlatform) : null,
                Casinos = controller.Casinos?.Select(c => new HealthCasino { Casino = c.Casino.ToString(), Platform = MachineConfig.PlatformName(c.Platform) }).ToList(),
                Standby = controller.Standby ? true : null,
            };
            // while initializing this is the reason startup is still retrying (e.g. database not reachable yet)
            if (controller.State != ServiceState.Ready && !string.IsNullOrEmpty(controller.FailureReason))
            {
                response.Error = controller.FailureReason;
            }
            return new ApiResult(code, System.Text.Json.JsonSerializer.Serialize(response, HealthResponseJson.Options));
        }

        private bool IsReady(HttpContext context, out ApiResult notReady)
        {
            notReady = default;
            if (controller.State == ServiceState.Ready)
            {
                return true;
            }
            notReady = new ApiResult(StatusCodes.Status503ServiceUnavailable, Error("service is " + controller.State.ToString().ToLowerInvariant()));
            return false;
        }

        private static async Task<string> ReadBodyAsync(HttpContext context)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private static async Task WriteAsync(HttpContext context, ApiResult result)
        {
            context.Response.StatusCode = result.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(result.Body);
        }
    }
}
