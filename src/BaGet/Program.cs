using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Web;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
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
            var logPath = Path.Combine(baseDirectory, "@logs", "baget-.log");

            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var host = CreateHostBuilder(args, logger)
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration((_, config) => config.SetBasePath(AppContext.BaseDirectory))
                .Build();

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

            app.OnExecuteAsync(async cancellationToken =>
            {
                // Do not run migrations if not requested
                if (bool.TryParse(Environment.GetEnvironmentVariable("RUN_MIGRATIONS"), out var runMigrations) && runMigrations)
                {
                    await host.RunMigrationsAsync(cancellationToken);
                }
                // Start the HTTP server and listen for incoming requests
                await host.RunAsync(cancellationToken);
            });

            // Process command-line arguments and execute specific commands
            await app.ExecuteAsync(args);

            // Log unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = (Exception)e.ExceptionObject;
                Log.Fatal(ex, "Unhandled exception occured");
                Log.CloseAndFlush();
            };
        }

        public static IHostBuilder CreateHostBuilder(string[] args, ILogger logger)
        {
            return Host
                .CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, config) =>
                {
                    // If BAGET_CONFIG_ROOT is not set, the configuration system defaults to looking
                    // for appsettings.json in the directory where the application is executed
                    // Configure this path as a VOLUME mount inside Docker container
                    // -v /home/service-baget/config:/home/baget/config:z,ro
                    // and put appsettings.json to the host config directory
                    var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");
                    if (!string.IsNullOrEmpty(root)) config.SetBasePath(root);
                })
                .UseSerilog(logger)
                .ConfigureWebHostDefaults(web =>
                {
                    web.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 30000000);
                    web.UseStartup<Startup>();
                });
        }
    }
}
