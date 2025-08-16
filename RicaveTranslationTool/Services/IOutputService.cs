using Spectre.Console;

namespace RicaveTranslator.Core.Services
{
    /// <summary>
    /// Defines a service for handling console output. This interface allows for decoupling
    /// the core application logic from the specific console implementation (e.g., Spectre.Console).
    /// </summary>
    public interface IOutputService
    {
        /// <summary>
        /// Writes a line of text to the console.
        /// </summary>
        void WriteLine();

        /// <summary>
        /// Writes a markup-formatted string to the console.
        /// </summary>
        /// <param name="markup">The markup-formatted string.</param>
        void MarkupLine(string markup);

        /// <summary>
        /// Writes a table to the console.
        /// </summary>
        /// <param name="table">The table to write.</param>
        void WriteTable(Table table);

        /// <summary>
        /// Displays a progress UI and executes an async action.
        /// </summary>
        /// <param name="action">The async action to execute within the progress UI context.</param>
        Task Progress(Func<ProgressContext, Task> action);

        /// <summary>
        /// Displays a progress UI with specific columns and executes an async action.
        /// </summary>
        /// <param name="columns">The columns to display in the progress UI.</param>
        /// <param name="action">The async action to execute within the progress UI context.</param>
        Task Progress(ProgressColumn[] columns, Func<ProgressContext, Task> action);

        /// <summary>
        /// Prompts the user for input.
        /// </summary>
        /// <typeparam name="T">The expected type of the input.</typeparam>
        /// <param name="prompt">The prompt to display.</param>
        /// <returns>The user's input.</returns>
        T Prompt<T>(IPrompt<T> prompt);
    }
}
