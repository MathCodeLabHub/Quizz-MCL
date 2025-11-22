using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quizz.DataAccess;
using Quizz.Auth;
using System;

namespace Quizz.Functions
{
    public class Program
    {
        public static void Main()
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureServices((context, services) =>
                {
                    // Get configuration
                    var configuration = context.Configuration;
                    var connectionString = configuration["PostgresConnectionString"] 
                        ?? Environment.GetEnvironmentVariable("PostgresConnectionString");

                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException(
                            "PostgresConnectionString not found in configuration. " +
                            "Set it in local.settings.json or Azure App Settings.");
                    }

                    // Register application services
                    services.AddDbService(connectionString);
                    services.AddApiKeyAuthentication();

                    // Register AuthService for JWT authentication
                    services.AddSingleton<Quizz.Auth.AuthService>();

                    // Add application insights (optional)
                    services.AddApplicationInsightsTelemetryWorkerService();
                    services.ConfigureFunctionsApplicationInsights();
                })
                .Build();

            host.Run();
        }
    }
}
