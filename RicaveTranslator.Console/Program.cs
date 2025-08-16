using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RicaveTranslator.Core.Models;
using RicaveTranslator.Core.Services;
using Spectre.Console;

namespace RicaveTranslator.Console;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        System.Console.OutputEncoding = Encoding.UTF8;
        var host = AppHost.Create();
        var outputService = host.Services.GetRequiredService<IOutputService>();

        // Show title unless running install-completion
        if (args.Length == 0 || (args.Length > 0 && args[0] != "install-completion"))
        {
            AnsiConsole.Clear();
            outputService.MarkupLine("[bold aqua]--- Ricave Game XML Translator ---[/]");
        }

        if (args.Length == 0)
        {
            var jobService = host.Services.GetRequiredService<JobService>();
            jobService.PrintUsageInstructions();
            return 0;
        }

        var rootCommand = SetupCommandLine(host.Services);

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .UseSuggestDirective()
            .Build();

        return await parser.InvokeAsync(args);
    }

    private static RootCommand SetupCommandLine(IServiceProvider services)
    {
        var verboseOption = new Option<bool>(["--verbose", "-v"], "Enables verbose output for file-level details");
        var alwaysCreateInfoFileOption = new Option<bool>("--always-create-info-file", "Always recreate Info.xml");
        var langCodesOption = new Option<string[]>("--lang", "Language codes for the operation")
            { Arity = ArgumentArity.ZeroOrMore };

        var newCmd = new Command("new", "Translates languages from scratch")
            { langCodesOption, verboseOption, alwaysCreateInfoFileOption };
        var allCmd = new Command("all", "Translates all supported languages")
            { verboseOption, alwaysCreateInfoFileOption };
        var syncAllCmd = new Command("sync-all", "Translates new content and fixes all languages")
            { verboseOption, alwaysCreateInfoFileOption };
        var generateManifestCmd = new Command("generate-manifest", "Generates source hashes")
            { langCodesOption, verboseOption, alwaysCreateInfoFileOption };
        var fixCmd = new Command("fix", "Verifies and fixes translations")
            { langCodesOption, verboseOption, alwaysCreateInfoFileOption };
        var debugFixCmd = new Command("debug-fix", "Reports file issues without modifying them")
            { langCodesOption, verboseOption, alwaysCreateInfoFileOption };
        var resumeCmd = new Command("resume", "Resumes the last failed job")
            { verboseOption, alwaysCreateInfoFileOption };

        var installCompletionCommand = new Command("install-completion", "Installs tab completion for PowerShell.");

        // --- NEW: Set Key Command ---
        var setKeyCommand = new Command("set-key", "Saves your Gemini API key securely for future use.");

        newCmd.SetHandler((lang, verbose, alwaysCreate) => Run(services, CommandType.New, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        allCmd.SetHandler((verbose, alwaysCreate) => Run(services, CommandType.All, [], verbose, alwaysCreate), verboseOption,
            alwaysCreateInfoFileOption);
        syncAllCmd.SetHandler((verbose, alwaysCreate) => Run(services, CommandType.SyncAll, [], verbose, alwaysCreate),
            verboseOption, alwaysCreateInfoFileOption);
        generateManifestCmd.SetHandler(
            (lang, verbose, alwaysCreate) => Run(services, CommandType.GenerateManifest, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        fixCmd.SetHandler((lang, verbose, alwaysCreate) => Run(services, CommandType.Fix, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        debugFixCmd.SetHandler((lang, verbose, alwaysCreate) => Run(services, CommandType.DebugFix, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        resumeCmd.SetHandler((verbose, alwaysCreate) => Run(services, CommandType.Resume, [], verbose, alwaysCreate),
            verboseOption, alwaysCreateInfoFileOption);

        installCompletionCommand.SetHandler(() => InstallCompletion(services.GetRequiredService<IOutputService>()));

        // --- NEW: Handler for the Set Key Command ---
        setKeyCommand.SetHandler(() =>
        {
            var outputService = services.GetRequiredService<IOutputService>();
            var apiKey = outputService.Prompt(
                new TextPrompt<string>("Please enter your [yellow]Gemini API key[/]:")
                    .PromptStyle("blue")
                    .Secret());

            var apiKeyManager = services.GetRequiredService<ApiKeyManager>();
            apiKeyManager.SaveKey(apiKey);
            outputService.MarkupLine("[bold green]✓ Success![/] Your API key has been saved securely.");
        });

        return new RootCommand("Ricave Game XML Translator")
        {
            newCmd, allCmd, syncAllCmd, generateManifestCmd, fixCmd, debugFixCmd, resumeCmd, installCompletionCommand,
            setKeyCommand
        };
    }

    private static void InstallCompletion(IOutputService outputService)
    {
        if (!OperatingSystem.IsWindows())
        {
            outputService.MarkupLine(
                "[red]Error: Automatic installation is currently only supported on Windows for PowerShell.[/]");
            outputService.MarkupLine("For other shells, please run the '[grey]suggest[/]' directive manually.");
            return;
        }

        try
        {
            outputService.MarkupLine("[cyan]Attempting to install PowerShell tab completion...[/]");

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                outputService.MarkupLine("[red]Error: Could not determine the application path.[/]");
                return;
            }

            var profilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WindowsPowerShell\\Microsoft.PowerShell_profile.ps1");

            var profileDir = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrEmpty(profileDir)) Directory.CreateDirectory(profileDir);

            var registrationCommand =
                $"Register-ArgumentCompleter -CommandName '{Path.GetFileName(exePath)}' -ScriptBlock ([scriptblock]::Create(\". '{exePath}' [suggest]\"))";

            outputService.MarkupLine($"[grey]Profile path: {profilePath}[/]");

            File.AppendAllText(profilePath, Environment.NewLine + registrationCommand + Environment.NewLine);

            outputService.MarkupLine(
                "[bold green]✓ Success![/] PowerShell completion script has been added to your profile.");
            outputService.MarkupLine(
                "[yellow]Please restart your PowerShell terminal for the changes to take effect.[/]");
        }
        catch (Exception ex)
        {
            outputService.MarkupLine("[bold red]An error occurred during installation:[/]");
            outputService.WriteException(ex, ExceptionFormats.ShortenPaths);
        }
    }

    private static async Task Run(IServiceProvider services, CommandType command, string[] langCodes, bool verbose, bool alwaysCreateInfoFile)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var overallSuccess = true;
        var outputService = services.GetRequiredService<IOutputService>();

        // ReSharper disable once AccessToDisposedClosure
        void CancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
        {
            outputService.WriteLine();
            outputService.MarkupLine("[bold orange1]Cancellation requested. Finishing current job...[/]");
            cancellationTokenSource.Cancel();
            eventArgs.Cancel = true;
        }

        try
        {
            // --- NEW: API Key validation logic ---
            var apiKeyManager = services.GetRequiredService<ApiKeyManager>();
            var apiKey = apiKeyManager.LoadKey() ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

            if (string.IsNullOrEmpty(apiKey))
            {
                outputService.MarkupLine("[bold red]Error: Gemini API key not found.[/]");
                outputService.MarkupLine(
                    "Please set it using the '[yellow]set-key[/]' command or the '[yellow]GEMINI_API_KEY[/]' environment variable.");
                return;
            }

            var appSettings = services.GetRequiredService<AppSettings>();
            appSettings.AlwaysCreateInfoFile = alwaysCreateInfoFile;
            if (!ValidateConfiguration(appSettings.Paths, outputService)) return;
            var jobService = services.GetRequiredService<JobService>();
            var jobs = await jobService.GetJobsFromArgsAsync(command, langCodes);
            if (jobs == null || jobs.Count == 0) return;
            var processor = services.GetRequiredService<TranslationProcessor>();
            System.Console.CancelKeyPress += CancelHandler;
            outputService.WriteLine();
            if (jobs.Count > 1)
                outputService.MarkupLine("[grey]Press Ctrl+C to gracefully cancel after the current job completes.[/]");
            var overallFileResults = new List<(string Language, string File, string Status, string? Error)>();
            foreach (var job in jobs)
            {
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    outputService.WriteLine();
                    outputService.MarkupLine("[bold orange1]Bulk operation cancelled. Aborting remaining jobs.[/]");
                    break;
                }

                if (jobs.Count > 1)
                {
                    outputService.WriteLine();
                    outputService.MarkupLine($"[bold aqua]--- Processing Job '[yellow]{job.JobId}[/]' ---[/]");
                }

                var jobResults = await processor.ProcessJobAsync(job, verbose, cancellationTokenSource.Token);
                overallFileResults.AddRange(jobResults);
                if (job.IsComplete())
                {
                    outputService.WriteLine();
                    outputService.MarkupLine(
                        $"[bold green]Job '[yellow]{job.JobId}[/]' completed successfully. Cleaning up state file.[/]");
                    job.Delete();
                }
                else
                {
                    overallSuccess = false;
                    outputService.WriteLine();
                    outputService.MarkupLine(
                        $"[bold yellow]Job '[yellow]{job.JobId}[/]' finished with some errors. Run with 'resume' to try again.[/]");
                }
            }

            PrintFinalSummary(overallFileResults, overallSuccess, jobs.Count > 1, outputService);
        }
        catch (Exception ex)
        {
            outputService.WriteLine();
            outputService.MarkupLine("[bold red]An unexpected error occurred:[/]");
            outputService.WriteException(ex, ExceptionFormats.ShortenPaths);
        }
        finally
        {
            System.Console.CancelKeyPress -= CancelHandler;
            cancellationTokenSource.Dispose();
        }
    }

    private static void PrintFinalSummary(List<(string Language, string File, string Status, string? Error)> results,
        bool success, bool isBulk, IOutputService outputService)
    {
        var totalFiles = results.Count;
        var totalSuccess = results.Count(r => r.Status == "Success");
        var totalFail = results.Count(r => r.Status == "Failed");

        outputService.WriteLine();
        outputService.MarkupLine(
            $"[bold]Total: [green]{totalSuccess} succeeded[/], [red]{totalFail} failed[/], [yellow]{totalFiles} processed[/].[/]");

        if (totalFail > 0)
            foreach (var group in results.Where(r => r.Status == "Failed").GroupBy(r => r.Language))
            {
                outputService.WriteLine();
                outputService.MarkupLine($"[red]Failures for {group.Key}:[/]");
                foreach (var failResult in group)
                    outputService.MarkupLine($"[red]    {failResult.File}: {failResult.Error}[/]");
            }

        if (isBulk)
        {
            outputService.WriteLine();
            outputService.MarkupLine(success
                ? "[bold green]All jobs in the bulk operation completed successfully.[/]"
                : "[bold yellow]Bulk operation finished with some errors.[/]");
        }
    }

    private static bool ValidateConfiguration(PathSettings paths, IOutputService outputService)
    {
        var fullTemplatePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, paths.TemplateBasePath));
        var fullLanguagesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, paths.LanguagesBasePath));

        if (Directory.Exists(fullTemplatePath) && Directory.Exists(fullLanguagesPath))
            return true;

        outputService.MarkupLine("[bold red]Error: One or both directory paths in appsettings.json do not exist.[/]");
        outputService.MarkupLine($"[red]Template Path Checked: {fullTemplatePath}[/]");
        outputService.MarkupLine($"[red]Languages Path Checked: {fullLanguagesPath}[/]");
        return false;
    }
}