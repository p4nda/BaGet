using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Web;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BaGet
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12;

            var baseDirectory = AppContext.BaseDirectory;
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var containerConfigDir = Environment.GetEnvironmentVariable("BAGET_CONF_DIR");
            var containerLogsDir = Environment.GetEnvironmentVariable("BAGET_LOGS_DIR");
            var logPath = containerLogsDir != null
                ? Path.Combine(containerLogsDir, "baget-.log")
                : Path.Combine(baseDirectory, "@logs", "baget-.log");

            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var host = CreateHostBuilder(args, logger, environment, baseDirectory, containerConfigDir).Build();

            var config = host.Services.GetService(typeof(IConfiguration)) as IConfiguration;
            if (config == null) throw new ArgumentNullException(nameof(config));
            LogConfigInfo(logger, config);

            if (!host.ValidateStartupOptions()) return;

            var app = new CommandLineApplication
            {
                Name = "baget",
                Description = "A light-weight NuGet service",
            };

            app.HelpOption(inherited: true);

            app.Command("import", import =>
            {
                import.Command("downloads", downloads =>
                {
                    downloads.OnExecuteAsync(async cancellationToken =>
                    {
                        using var scope = host.Services.CreateScope();
                        var importer = scope.ServiceProvider.GetRequiredService<DownloadsImporter>();
                        await importer.ImportAsync(cancellationToken);
                    });
                });
            });

            app.Option("--urls", "The URLs that BaGet should bind to.", CommandOptionType.SingleValue);
            app.Option("--ENVIRONMENT", "Development | Production", CommandOptionType.SingleValue);
            app.Option("--contentRoot", "AppContext.BaseDirectory", CommandOptionType.SingleValue);
            app.Option("--applicationName", "BaGet", CommandOptionType.SingleValue);

            app.OnExecuteAsync(async cancellationToken =>
            {
                // Do not run migrations if not requested
                if (bool.TryParse(config[nameof(BaGetOptions.RunMigrationsAtStartup)], out var runMigrations) && runMigrations)
                {
                    await host.RunMigrationsAsync(cancellationToken);
                }
                // Start the HTTP server and listen for incoming requests
                await host.RunAsync(cancellationToken);
            });

            // Process command-line arguments and execute specific commands
            await app.ExecuteAsync(args);

            // Log unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = (Exception)e.ExceptionObject;
                Log.Fatal(ex, "Unhandled exception occured");
                Log.CloseAndFlush();
            };
        }

        private static void LogConfigInfo(ILogger logger, IConfiguration config)
        {
            logger.Information("Database type: {DatabaseType}", config["Database:Type"]);
            logger.Information("Storage path: {StoragePath}", config["Storage:Path"]);
        }

        public static IHostBuilder CreateHostBuilder(string[] args, ILogger logger, string environment, string baseDirectory, string configDir)
        {
            environment ??= "Development";
            baseDirectory ??= AppContext.BaseDirectory;

            return Host
                .CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, config) =>
                {
                    // Read settings from the mounted conf volume when running inside container
                    ReadSettingsFromDirectory(logger, config, args, environment, baseDirectory, configDir);

                    // Optionally load secrets from files in the conventional path
                    config.AddKeyPerFile("/run/secrets", optional: true);
                })
                .UseSerilog(logger)
                .UseContentRoot(baseDirectory)
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseUrls("http://*:8080", "https://*:8081");
                    web.ConfigureKestrel(options =>
                    {
                        // Fix: ASPNETCORE_Kestrel__Certificates__Default__Path and/or
                        // X509Certificate2 is not working inside Linux container:
                        // We can mount cert directory with PFX as a container volume to /app/cert
                        options.ConfigureHttpsDefaults(httpsOptions =>
                        {
                            httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls13 |
                                                        System.Security.Authentication.SslProtocols.Tls12;
                            httpsOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                            httpsOptions.ServerCertificateSelector = (_, _) =>
                            {
                                var pfxFilePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cert", "baget.pfx"));
                                logger.Information("Using TLS cert from '{PfxFilePath}'", pfxFilePath);
                                if (!File.Exists(pfxFilePath))
                                    throw new ApplicationException("PFX certificate not found");
                                return ReadX509Certificate2(pfxFilePath, string.Empty);
                            };
                        });
                        options.Limits.MaxRequestBodySize = 314572800; // 300 MB
                    });
                    web.UseStartup<Startup>();
                });
        }

        private static void ReadSettingsFromDirectory(ILogger logger, IConfigurationBuilder config, string[] args, string environment, string baseDirectory, string configDir)
        {
            if (logger == null) return; // Tests

            baseDirectory ??= AppContext.BaseDirectory;

            if (string.IsNullOrWhiteSpace(configDir))
            {
                logger.Information("Config directory: '{ConfigDirectory}'", baseDirectory);
            }
            else
            {
                config.Sources.Clear(); // Clear the default ones from CreateDefaultBuilder
                config.SetBasePath(configDir) // Does not set base path for following json configs, using Path.Combine
                    .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(configDir, $"appsettings.{environment}.json"), optional: false, reloadOnChange: false);
                config.AddEnvironmentVariables(); // ENV will override configs

                logger.Information("Config directory: '{ConfigDirectory}'", configDir);
            }
        }

        /// <summary>
        /// Read byte contents of PFX file
        /// </summary>
        /// <param name="pfxFilePath">Full path to the PFX certificates file</param>
        /// <param name="password">Can be empty string</param>
        /// <returns>X509Certificate2</returns>
        private static X509Certificate2 ReadX509Certificate2(string pfxFilePath, string password)
        {
            var certBytes = File.ReadAllBytes(pfxFilePath);
            return new X509Certificate2(certBytes, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
        }
    }
}
