using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Interfaces;
using RicaveTranslator.Core.Models;
using RicaveTranslator.Core.Services;

namespace RicaveTranslator.Console;

public class SecretsAnchor
{
    // This class is just a placeholder for the UserSecretsId attribute.
}

public static class AppHost
{
    public static IHost Create()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", false, true);

                if (IsDevelopment()) config.AddUserSecrets<SecretsAnchor>();
            })
            .ConfigureServices((hostContext, services) =>
            {
                var appSettings = new AppSettings();
                hostContext.Configuration.GetSection("AppSettings").Bind(appSettings);
                services.AddSingleton(appSettings);
                services.AddSingleton(provider => provider.GetRequiredService<AppSettings>().Paths);
                services.AddSingleton(provider => provider.GetRequiredService<AppSettings>().Api);

                // Register UI and platform-specific services first
                services.AddSingleton<IUserNotifier, SpectreNotifier>();

                // --- PLATFORM SWITCHING LOGIC ---
                if (OperatingSystem.IsWindows())
                    services.AddSingleton<IApiKeyStore, WindowsApiKeyStore>();
                else if (OperatingSystem.IsMacOS())
                    services.AddSingleton<IApiKeyStore, MacOsKeychainStore>();
                else if (OperatingSystem.IsLinux())
                    services.AddSingleton<IApiKeyStore, LinuxKeyringStore>();
                else
                    // Use the new insecure fallback for any other OS
                    services.AddSingleton<IApiKeyStore, EncryptedFileApiKeyStore>();

                services.AddSingleton<ApiKeyManager>();

                // Build a temporary service provider to resolve the ApiKeyManager
                var serviceProvider = services.BuildServiceProvider();
                var apiKeyManager = serviceProvider.GetRequiredService<ApiKeyManager>();
                var apiKey = apiKeyManager.LoadKey() ?? hostContext.Configuration["GeminiApiKey"];

                // Register HttpClient as a singleton with the configured timeout.
                services.AddSingleton(new HttpClient
                    { Timeout = TimeSpan.FromMinutes(appSettings.Api.ApiTimeoutMinutes) });

                // Only register GenerativeModel if apiKey is not null or empty
                if (!string.IsNullOrWhiteSpace(apiKey))
                    services.AddSingleton(provider =>
                    {
                        var apiSettings = provider.GetRequiredService<ApiSettings>();
                        var httpClient = provider.GetRequiredService<HttpClient>();
                        var safetySettings = new List<SafetySetting>
                        {
                            new()
                            {
                                Category = HarmCategory.HARM_CATEGORY_HARASSMENT,
                                Threshold = HarmBlockThreshold.BLOCK_NONE
                            },
                            new()
                            {
                                Category = HarmCategory.HARM_CATEGORY_HATE_SPEECH,
                                Threshold = HarmBlockThreshold.BLOCK_NONE
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

                // Register Core Services
                services.AddSingleton<LanguageHelper>();
                services.AddSingleton<FileProcessingService>();
                services.AddSingleton<TranslationService>();
                services.AddSingleton<ManifestService>();
                services.AddSingleton<NodeTranslationService>();
                services.AddSingleton<VerificationService>();
                services.AddSingleton<TranslationProcessor>();
                services.AddSingleton<JobService>();
                services.AddSingleton<LanguageProcessor>();
            })
            .Build();
    }

    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}