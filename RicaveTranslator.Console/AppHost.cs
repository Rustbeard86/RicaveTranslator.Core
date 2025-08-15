using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Models;
using RicaveTranslator.Core.Services;

namespace RicaveTranslator.Console;

public static class AppHost
{
    public static IHost Create(string apiKey, bool verbose)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                // Set the base path to the application's running directory and add appsettings.json
                config.SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", false, true);
            })
            .ConfigureServices((hostContext, services) =>
            {
                // Bind the entire "AppSettings" section from the JSON to our AppSettings class
                var appSettings = new AppSettings();
                hostContext.Configuration.GetSection("AppSettings").Bind(appSettings);
                services.AddSingleton(appSettings);

                // Now, register the nested settings classes so they can be injected directly
                services.AddSingleton(provider => provider.GetRequiredService<AppSettings>().Paths);
                services.AddSingleton(provider => provider.GetRequiredService<AppSettings>().API);

                // Register HttpClient and GenerativeModel
                services.AddSingleton(new HttpClient
                    { Timeout = TimeSpan.FromMinutes(appSettings.API.ApiTimeoutMinutes) });
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

                // Register all the Core Services
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
                    provider.GetRequiredService<ManifestService>(),
                    provider.GetRequiredService<VerificationService>(),
                    provider.GetRequiredService<LanguageHelper>(),
                    verbose
                ));
            })
            .Build();
    }
}