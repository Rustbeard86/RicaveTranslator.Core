using RicaveTranslator.Core.Services;
using Spectre.Console;

namespace RicaveTranslator.Console.Services
{
    /// <summary>
    /// Implements the <see cref="IOutputService"/> using Spectre.Console's <see cref="AnsiConsole"/>.
    /// This class acts as a wrapper, directing all output calls to the Spectre.Console library.
    /// </summary>
    public class AnsiConsoleOutputService : IOutputService
    {
        /// <inheritdoc />
        public void WriteLine() => AnsiConsole.WriteLine();

        /// <inheritdoc />
        public void MarkupLine(string markup) => AnsiConsole.MarkupLine(markup);

        /// <inheritdoc />
        public void WriteTable(Table table) => AnsiConsole.Write(table);

        /// <inheritdoc />
        public Task Progress(Func<ProgressContext, Task> action) => AnsiConsole.Progress().StartAsync(action);

        /// <inheritdoc />
        public Task Progress(ProgressColumn[] columns, Func<ProgressContext, Task> action) =>
            AnsiConsole.Progress().Columns(columns).StartAsync(action);

        /// <inheritdoc />
        public T Prompt<T>(IPrompt<T> prompt) => AnsiConsole.Prompt(prompt);
    }
}
