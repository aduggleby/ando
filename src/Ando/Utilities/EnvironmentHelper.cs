// =============================================================================
// EnvironmentHelper.cs
//
// Summary: Shared utilities for environment variable access.
//
// This class extracts common environment variable patterns used across multiple
// operations classes, eliminating duplication and providing consistent behavior.
//
// Design Decisions:
// - Static class since these are pure utility functions with no state
// - Throws InvalidOperationException for missing required variables (consistent with VarsContext)
// - Provides optional description parameter for clearer error messages
//
// Interactive Prompting (GetRequiredOrPrompt):
// - Prompts user via Console if environment variable is not set
// - Supports secret input (hidden characters) for tokens/passwords
// - Sets the environment variable for the CURRENT PROCESS only after prompting
// - Child processes inherit the variable, but it is NOT persisted to the shell
// - When the process exits, prompted values are lost
// - This is intentional: credentials should not be stored automatically
// =============================================================================

namespace Ando.Utilities;

/// <summary>
/// Shared utilities for environment variable access.
/// Provides consistent error handling for required environment variables.
/// </summary>
public static class EnvironmentHelper
{
    /// <summary>
    /// Gets a required environment variable, throwing if not set.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <param name="description">Optional description for clearer error messages.</param>
    /// <exception cref="InvalidOperationException">Thrown when the variable is not set.</exception>
    public static string GetRequired(string name, string? description = null)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            var message = description != null
                ? $"Required environment variable '{name}' ({description}) is not set."
                : $"Required environment variable '{name}' is not set.";
            throw new InvalidOperationException(message);
        }
        return value;
    }

    /// <summary>
    /// Gets an environment variable value, or null if not set.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    public static string? Get(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    /// <summary>
    /// Gets an environment variable with a default value if not set.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <param name="defaultValue">Value to return if not set.</param>
    public static string GetOrDefault(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    /// <summary>
    /// Gets a required environment variable, prompting the user if not set.
    /// After prompting, sets the environment variable for the current process
    /// so subsequent calls and child processes can use it.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <param name="description">Description shown in the prompt.</param>
    /// <param name="isSecret">If true, hides input (for tokens/passwords).</param>
    /// <returns>The environment variable value.</returns>
    public static string GetRequiredOrPrompt(string name, string description, bool isSecret = false)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Prompt the user for the value.
        Console.Write($"{description} ({name}): ");

        if (isSecret)
        {
            value = ReadSecretLine();
        }
        else
        {
            value = Console.ReadLine();
        }

        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Value for '{name}' ({description}) is required.");
        }

        // Set the environment variable for this process and child processes.
        Environment.SetEnvironmentVariable(name, value);

        return value;
    }

    /// <summary>
    /// Reads a line from console showing asterisks instead of actual characters.
    /// </summary>
    private static string ReadSecretLine()
    {
        var value = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace && value.Length > 0)
            {
                value.Length--;
                // Erase the asterisk: move back, overwrite with space, move back again.
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        return value.ToString();
    }
}
