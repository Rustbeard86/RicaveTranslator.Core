using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Models;
using RicaveTranslator.Core.Services;

namespace RicaveTranslator.Console;

public class SecretsAnchor
{
} // This class is used to anchor the user secrets in the development environment.

public static class AppHost
{
    public static IHost Create()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((hostContext, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", false, true);

                // Load user secrets in Development environment
                if (hostContext.HostingEnvironment.IsDevelopment()) config.AddUserSecrets<SecretsAnchor>();
            })
            .ConfigureServices((hostContext, services) =>
            {
                var appSettings = new AppSettings();
                hostContext.Configuration.GetSection("AppSettings").Bind(appSettings);

                // Get the API key from configuration (supports secrets.json)
                var apiKey = hostContext.Configuration["GeminiApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                    throw new InvalidOperationException(
                        "GeminiApiKey is not set. Use 'dotnet user-secrets set \"GeminiApiKey\" \"your_key\"' to configure it.");

                services.AddSingleton(appSettings);
                services.AddSingleton(provider => provider.GetRequiredService<AppSettings>().Paths);
                services.AddSingleton(provider => provider.GetRequiredService<AppSettings>().Api);

                services.AddSingleton(new HttpClient
                    { Timeout = TimeSpan.FromMinutes(appSettings.Api.ApiTimeoutMinutes) });
                services.AddSingleton(provider =>
                {
                    var apiSettings = provider.GetRequiredService<ApiSettings>();
                    var httpClient = provider.GetRequiredService<HttpClient>();
                    var safetySettings = new List<SafetySetting>
                    {
                        new()
                        {
                            Category = HarmCategory.HARM_CATEGORY_HARASSMENT, Threshold = HarmBlockThreshold.BLOCK_NONE
                        },
                        new()
                        {
                            Category = HarmCategory.HARM_CATEGORY_HATE_SPEECH, Threshold = HarmBlockThreshold.BLOCK_NONE
                        },
                        new()
                        {
                            Category = HarmCategory.HARM_CATEGORY_SEXUALLY_EXPLICIT,
                            Threshold = HarmBlockThreshold.BLOCK_NONE
                        },
                        new()
                        {
                            Category = HarmCategory.HARM_CATEGORY_DANGEROUS_CONTENT,
                            Threshold = HarmBlockThreshold.BLOCK_NONE
                        }
                    };
                    return new GenerativeModel(apiKey, apiSettings.ModelName, safetySettings: safetySettings,
                        httpClient: httpClient);
                });

                services.AddSingleton<LanguageHelper>();
                services.AddSingleton<FileProcessingService>();
                services.AddSingleton<TranslationService>();
                services.AddSingleton<ManifestService>();
                services.AddSingleton<NodeTranslationService>();
                services.AddSingleton<VerificationService>();
                services.AddSingleton<TranslationProcessor>();
                services.AddSingleton<JobService>();

                services.AddSingleton(provider => new LanguageProcessor(
                    provider.GetRequiredService<AppSettings>(),
                    provider.GetRequiredService<PathSettings>(),
                    provider.GetRequiredService<ApiSettings>(),
                    provider.GetRequiredService<GenerativeModel>(),
                    provider.GetRequiredService<FileProcessingService>(),
                    provider.GetRequiredService<ManifestService>(),
                    provider.GetRequiredService<VerificationService>(),
                    provider.GetRequiredService<LanguageHelper>()
                ));
            })
            .Build();
    }
}