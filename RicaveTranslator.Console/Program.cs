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

        // Show title unless running install-completion
        if (args.Length == 0 || (args.Length > 0 && args[0] != "install-completion"))
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[bold aqua]--- Ricave Game XML Translator ---[/]");
        }

        if (args.Length == 0)
        {
            var tempHost = AppHost.Create();
            var jobService = tempHost.Services.GetRequiredService<JobService>();
            jobService.PrintUsageInstructions();
            return 0;
        }

        var rootCommand = SetupCommandLine();

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .UseSuggestDirective()
            .Build();

        return await parser.InvokeAsync(args);
    }

    private static RootCommand SetupCommandLine()
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

        newCmd.SetHandler((lang, verbose, alwaysCreate) => Run(CommandType.New, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        allCmd.SetHandler((verbose, alwaysCreate) => Run(CommandType.All, [], verbose, alwaysCreate), verboseOption,
            alwaysCreateInfoFileOption);
        syncAllCmd.SetHandler((verbose, alwaysCreate) => Run(CommandType.SyncAll, [], verbose, alwaysCreate),
            verboseOption, alwaysCreateInfoFileOption);
        generateManifestCmd.SetHandler(
            (lang, verbose, alwaysCreate) => Run(CommandType.GenerateManifest, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        fixCmd.SetHandler((lang, verbose, alwaysCreate) => Run(CommandType.Fix, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        debugFixCmd.SetHandler((lang, verbose, alwaysCreate) => Run(CommandType.DebugFix, lang, verbose, alwaysCreate),
            langCodesOption, verboseOption, alwaysCreateInfoFileOption);
        resumeCmd.SetHandler((verbose, alwaysCreate) => Run(CommandType.Resume, [], verbose, alwaysCreate),
            verboseOption, alwaysCreateInfoFileOption);

        installCompletionCommand.SetHandler(InstallCompletion);

        // --- NEW: Handler for the Set Key Command ---
        setKeyCommand.SetHandler(() =>
        {
            var apiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("Please enter your [yellow]Gemini API key[/]:")
                    .PromptStyle("blue")
                    .Secret());

            var apiKeyManager = new ApiKeyManager();
            apiKeyManager.SaveKey(apiKey);
            AnsiConsole.MarkupLine("[bold green]✓ Success![/] Your API key has been saved securely.");
        });

        return new RootCommand("Ricave Game XML Translator")
        {
            newCmd, allCmd, syncAllCmd, generateManifestCmd, fixCmd, debugFixCmd, resumeCmd, installCompletionCommand,
            setKeyCommand
        };
    }

    private static void InstallCompletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine(
                "[red]Error: Automatic installation is currently only supported on Windows for PowerShell.[/]");
            AnsiConsole.MarkupLine("For other shells, please run the '[grey]suggest[/]' directive manually.");
            return;
        }

        try
        {
            AnsiConsole.MarkupLine("[cyan]Attempting to install PowerShell tab completion...[/]");

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                AnsiConsole.MarkupLine("[red]Error: Could not determine the application path.[/]");
                return;
            }

            var profilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WindowsPowerShell\\Microsoft.PowerShell_profile.ps1");

            var profileDir = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrEmpty(profileDir)) Directory.CreateDirectory(profileDir);

            var registrationCommand =
                $"Register-ArgumentCompleter -CommandName '{Path.GetFileName(exePath)}' -ScriptBlock ([scriptblock]::Create(\". '{exePath}' [suggest]\"))";

            AnsiConsole.MarkupLine($"[grey]Profile path: {profilePath}[/]");

            File.AppendAllText(profilePath, Environment.NewLine + registrationCommand + Environment.NewLine);

            AnsiConsole.MarkupLine(
                "[bold green]✓ Success![/] PowerShell completion script has been added to your profile.");
            AnsiConsole.MarkupLine(
                "[yellow]Please restart your PowerShell terminal for the changes to take effect.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]An error occurred during installation:[/]");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
        }
    }

    private static async Task Run(CommandType command, string[] langCodes, bool verbose, bool alwaysCreateInfoFile)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var overallSuccess = true;

        // ReSharper disable once AccessToDisposedClosure
        void CancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold orange1]Cancellation requested. Finishing current job...[/]");
            cancellationTokenSource.Cancel();
            eventArgs.Cancel = true;
        }

        try
        {
            var host = AppHost.Create();

            // --- NEW: API Key validation logic ---
            var apiKeyManager = host.Services.GetRequiredService<ApiKeyManager>();
            var apiKey = apiKeyManager.LoadKey() ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

            if (string.IsNullOrEmpty(apiKey))
            {
                AnsiConsole.MarkupLine("[bold red]Error: Gemini API key not found.[/]");
                AnsiConsole.MarkupLine(
                    "Please set it using the '[yellow]set-key[/]' command or the '[yellow]GEMINI_API_KEY[/]' environment variable.");
                return;
            }

            var appSettings = host.Services.GetRequiredService<AppSettings>();
            appSettings.AlwaysCreateInfoFile = alwaysCreateInfoFile;
            if (!ValidateConfiguration(appSettings.Paths)) return;
            var jobService = host.Services.GetRequiredService<JobService>();
            var jobs = await jobService.GetJobsFromArgsAsync(command, langCodes);
            if (jobs == null || jobs.Count == 0) return;
            var processor = host.Services.GetRequiredService<TranslationProcessor>();
            System.Console.CancelKeyPress += CancelHandler;
            AnsiConsole.WriteLine();
            if (jobs.Count > 1)
                AnsiConsole.MarkupLine("[grey]Press Ctrl+C to gracefully cancel after the current job completes.[/]");
            var overallFileResults = new List<(string Language, string File, string Status, string? Error)>();
            foreach (var job in jobs)
            {
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[bold orange1]Bulk operation cancelled. Aborting remaining jobs.[/]");
                    break;
                }

                if (jobs.Count > 1)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[bold aqua]--- Processing Job '[yellow]{job.JobId}[/]' ---[/]");
                }

                var jobResults = await processor.ProcessJobAsync(job, verbose, cancellationTokenSource.Token);
                overallFileResults.AddRange(jobResults);
                if (job.IsComplete())
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine(
                        $"[bold green]Job '[yellow]{job.JobId}[/]' completed successfully. Cleaning up state file.[/]");
                    job.Delete();
                }
                else
                {
                    overallSuccess = false;
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine(
                        $"[bold yellow]Job '[yellow]{job.JobId}[/]' finished with some errors. Run with 'resume' to try again.[/]");
                }
            }

            PrintFinalSummary(overallFileResults, overallSuccess, jobs.Count > 1);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold red]An unexpected error occurred:[/]");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
        }
        finally
        {
            System.Console.CancelKeyPress -= CancelHandler;
            cancellationTokenSource.Dispose();
        }
    }

    private static void PrintFinalSummary(List<(string Language, string File, string Status, string? Error)> results,
        bool success, bool isBulk)
    {
        var totalFiles = results.Count;
        var totalSuccess = results.Count(r => r.Status == "Success");
        var totalFail = results.Count(r => r.Status == "Failed");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold]Total: [green]{totalSuccess} succeeded[/], [red]{totalFail} failed[/], [yellow]{totalFiles} processed[/].[/]");

        if (totalFail > 0)
            foreach (var group in results.Where(r => r.Status == "Failed").GroupBy(r => r.Language))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]Failures for {group.Key}:[/]");
                foreach (var failResult in group)
                    AnsiConsole.MarkupLine($"[red]    {failResult.File}: {failResult.Error}[/]");
            }

        if (isBulk)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(success
                ? "[bold green]All jobs in the bulk operation completed successfully.[/]"
                : "[bold yellow]Bulk operation finished with some errors.[/]");
        }
    }

    private static bool ValidateConfiguration(PathSettings paths)
    {
        var fullTemplatePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, paths.TemplateBasePath));
        var fullLanguagesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, paths.LanguagesBasePath));

        if (Directory.Exists(fullTemplatePath) && Directory.Exists(fullLanguagesPath))
            return true;

        AnsiConsole.MarkupLine("[bold red]Error: One or both directory paths in appsettings.json do not exist.[/]");
        AnsiConsole.MarkupLine($"[red]Template Path Checked: {fullTemplatePath}[/]");
        AnsiConsole.MarkupLine($"[red]Languages Path Checked: {fullLanguagesPath}[/]");
        return false;
    }
}