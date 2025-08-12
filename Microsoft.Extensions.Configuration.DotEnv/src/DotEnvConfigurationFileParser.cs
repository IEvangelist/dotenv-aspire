using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Extensions.Configuration.DotEnv;

/// <summary>
/// Exception thrown when a .env file contains invalid syntax according to the .env specification.
/// </summary>
public class DotEnvParseException : Exception
{
    public int LineNumber { get; }
    public string ErrorCode { get; }

    public DotEnvParseException(string errorCode, int lineNumber, string message) 
        : base($"{errorCode} at line {lineNumber}: {message}")
    {
        ErrorCode = errorCode;
        LineNumber = lineNumber;
    }
}

/// <summary>
/// Options for configuring .env file parsing behavior.
/// </summary>
public class DotEnvParseOptions
{
    /// <summary>
    /// Whether to enable variable expansion (${VAR} or $VAR syntax). Default is true for backward compatibility.
    /// </summary>
    public bool EnableVariableExpansion { get; set; } = true;

    /// <summary>
    /// Whether to support alternative comment formats (; and //). Default is false.
    /// </summary>
    public bool EnableAlternativeComments { get; set; } = false;

    /// <summary>
    /// How to handle duplicate keys. Default is UseLastValue.
    /// </summary>
    public DuplicateKeyBehavior DuplicateKeyBehavior { get; set; } = DuplicateKeyBehavior.UseLastValue;

    /// <summary>
    /// Whether to enforce strict parsing according to the .env specification. 
    /// When false, invalid lines are ignored for backward compatibility. Default is false.
    /// </summary>
    public bool StrictParsing { get; set; } = false;
}

/// <summary>
/// Defines how to handle duplicate keys in .env files.
/// </summary>
public enum DuplicateKeyBehavior
{
    /// <summary>Use the last value encountered (most common behavior).</summary>
    UseLastValue,
    /// <summary>Use the first value encountered.</summary>
    UseFirstValue,
    /// <summary>Throw an error when duplicates are found.</summary>
    ThrowError
}

internal sealed partial class DotEnvConfigurationFileParser
{
    private DotEnvConfigurationFileParser() { }

    public static Dictionary<string, string?> Parse(Stream input) => ParseStream(input, new DotEnvParseOptions());

    public static Dictionary<string, string?> Parse(Stream input, DotEnvParseOptions options) => ParseStream(input, options);

    private static Dictionary<string, string?> ParseStream(Stream input, DotEnvParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(input, Encoding.UTF8);
        
        int lineNumber = 0;
        string? currentLine = null;
        var lineBuilder = new StringBuilder();
        bool inContinuation = false;

        while (!reader.EndOfStream)
        {
            var rawLine = reader.ReadLine();
            if (rawLine is null)
            {
                break;
            }

            lineNumber++;

            // Handle line continuation from previous line
            if (inContinuation)
            {
                // Check for comments on continuation lines (not allowed)
                if (ContainsComment(rawLine, options))
                {
                    throw new DotEnvParseException("ENV005", lineNumber, "Comments are not allowed on line continuation");
                }

                lineBuilder.Append('\n').Append(rawLine);
                
                // Check if this line also ends with continuation
                if (rawLine.TrimEnd().EndsWith('\\') && !rawLine.TrimEnd().EndsWith("\\\\"))
                {
                    // Remove the trailing backslash and continue
                    var trimmed = rawLine.TrimEnd();
                    lineBuilder.Length -= rawLine.Length - trimmed.Length + 1; // Remove backslash
                    continue;
                }
                else
                {
                    // End of continuation
                    currentLine = lineBuilder.ToString();
                    lineBuilder.Clear();
                    inContinuation = false;
                }
            }
            else
            {
                // Check for line continuation
                var trimmed = rawLine.TrimEnd();
                if (trimmed.EndsWith('\\') && !trimmed.EndsWith("\\\\"))
                {
                    // Start line continuation
                    lineBuilder.Append(trimmed[..^1]); // Remove trailing backslash
                    inContinuation = true;
                    continue;
                }
                else
                {
                    currentLine = rawLine;
                }
            }

            // Process the complete line
            ProcessLine(currentLine, lineNumber, data, seenKeys, options);
        }

        // Check for dangling continuation at EOF
        if (inContinuation)
        {
            throw new DotEnvParseException("ENV005", lineNumber, "Invalid line continuation at end of file");
        }

        return data;
    }

    private static void ProcessLine(string line, int lineNumber, Dictionary<string, string?> data, 
        HashSet<string> seenKeys, DotEnvParseOptions options)
    {
        // Trim leading and trailing whitespace as per spec
        line = line.Trim();

        // Skip empty lines
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        // Skip comments
        if (IsComment(line, options))
        {
            return;
        }

        // Must be a key/value pair - find the first = separator
        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            if (options.StrictParsing)
            {
                throw new DotEnvParseException("ENV001", lineNumber, "Invalid line format - missing assignment operator");
            }
            else
            {
                // Ignore invalid lines for backward compatibility
                return;
            }
        }

        if (separatorIndex == line.Length - 1)
        {
            // KEY= format is valid (empty value)
        }

        var key = line[..separatorIndex].Trim();
        
        // Validate key format
        if (options.StrictParsing)
        {
            ValidateKey(key, lineNumber);
        }
        else if (string.IsNullOrWhiteSpace(key))
        {
            // For backward compatibility, ignore lines with empty keys
            return;
        }

        var value = separatorIndex == line.Length - 1 ? "" : line[(separatorIndex + 1)..];

        // Handle duplicate keys
        if (!HandleDuplicateKey(key, seenKeys, options, lineNumber))
        {
            return; // Skip this key-value pair
        }

        // Process the value
        var processedValue = ProcessValue(value, data, options, lineNumber);

        // Store the key-value pair - ProcessValue already handles null conversion properly
        data[key] = processedValue;
        seenKeys.Add(key);
    }

    private static bool IsComment(string line, DotEnvParseOptions options)
    {
        if (line.StartsWith('#'))
        {
            return true;
        }

        if (options.EnableAlternativeComments)
        {
            if (line.StartsWith(';') || line.StartsWith("//"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsComment(string line, DotEnvParseOptions options)
    {
        if (line.Contains('#'))
        {
            return true;
        }

        if (options.EnableAlternativeComments)
        {
            if (line.Contains(';') || line.Contains("//"))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateKey(string key, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new DotEnvParseException("ENV003", lineNumber, "Key cannot be empty");
        }

        // Check for multi-line key (newlines)
        if (key.Contains('\n') || key.Contains('\r'))
        {
            throw new DotEnvParseException("ENV006", lineNumber, "Keys cannot span multiple lines");
        }

        // Validate key format: must start with letter or underscore, contain only letters, numbers, underscores
        if (!KeyFormatPattern().IsMatch(key))
        {
            throw new DotEnvParseException("ENV003", lineNumber, 
                "Invalid key format - must start with letter or underscore and contain only letters, numbers, and underscores");
        }
    }

    private static bool HandleDuplicateKey(string key, HashSet<string> seenKeys, DotEnvParseOptions options, int lineNumber)
    {
        if (seenKeys.Contains(key))
        {
            switch (options.DuplicateKeyBehavior)
            {
                case DuplicateKeyBehavior.ThrowError:
                    throw new DotEnvParseException("ENV002", lineNumber, $"Duplicate key '{key}'");
                case DuplicateKeyBehavior.UseFirstValue:
                    // Skip processing this value
                    return false;
                case DuplicateKeyBehavior.UseLastValue:
                    // Continue with processing (will overwrite)
                    return true;
            }
        }
        return true;
    }

    private static string? ProcessValue(string? value, Dictionary<string, string?> data, DotEnvParseOptions options, int lineNumber)
    {
        if (value is null)
        {
            return null;
        }

        // Handle empty unquoted value (should be null for backward compatibility)
        if (string.IsNullOrEmpty(value.Trim()))
        {
            return null;
        }

        // Don't trim here - whitespace handling depends on quoting
        
        // Handle double-quoted values
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            // Check for unclosed quotes in multi-line context
            if (!IsValidQuotedString(value, '"'))
            {
                throw new DotEnvParseException("ENV004", lineNumber, "Unclosed double quote");
            }

            value = value[1..^1]; // Remove quotes

            // Unescape common escape sequences within double quotes
            value = UnescapeDoubleQuotedString(value);

            // Expand environment variables in double-quoted values (if enabled)
            if (options.EnableVariableExpansion)
            {
                value = ExpandEnvironmentVariables(value, data);
            }

            return value; // Return empty string if quotes were empty
        }

        // Handle single-quoted values
        if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
        {
            // Check for unclosed quotes
            if (!IsValidQuotedString(value, '\''))
            {
                throw new DotEnvParseException("ENV004", lineNumber, "Unclosed single quote");
            }

            // Single quotes preserve literal values - no expansion or unescaping
            return value[1..^1]; // Return empty string if quotes were empty
        }

        // Handle unquoted values
        value = ProcessUnquotedValue(value, options);

        // Return null for empty unquoted values
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // Expand environment variables in unquoted values (if enabled)
        if (options.EnableVariableExpansion)
        {
            value = ExpandEnvironmentVariables(value, data);
        }

        return value;
    }

    private static bool IsValidQuotedString(string value, char quoteChar)
    {
        // For multi-line quoted strings, ensure proper opening and closing
        // This is a simplified check - for full multi-line support we'd need more complex parsing
        int quoteCount = 0;
        bool escaped = false;

        foreach (char c in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == quoteChar)
            {
                quoteCount++;
            }
        }

        // Should have even number of quotes (pairs)
        return quoteCount >= 2 && quoteCount % 2 == 0;
    }

    private static string UnescapeDoubleQuotedString(string value)
    {
        return value.Replace("\\\"", "\"")
                   .Replace("\\n", "\n")
                   .Replace("\\r", "\r")
                   .Replace("\\t", "\t")
                   .Replace("\\\\", "\\");
    }

    private static string ProcessUnquotedValue(string value, DotEnvParseOptions options)
    {
        // Remove inline comments from unquoted values
        int commentIndex = FindInlineCommentIndex(value, options);
        if (commentIndex >= 0)
        {
            value = value[..commentIndex];
        }

        // Trim trailing whitespace for unquoted values
        return value.TrimEnd();
    }

    private static int FindInlineCommentIndex(string value, DotEnvParseOptions options)
    {
        // Find the first comment character that's preceded by whitespace
        int hashIndex = value.IndexOf(" #");
        int result = hashIndex;

        if (options.EnableAlternativeComments)
        {
            int semicolonIndex = value.IndexOf(" ;");
            int slashIndex = value.IndexOf(" //");

            if (semicolonIndex >= 0 && (result < 0 || semicolonIndex < result))
            {
                result = semicolonIndex;
            }

            if (slashIndex >= 0 && (result < 0 || slashIndex < result))
            {
                result = slashIndex;
            }
        }

        return result;
    }

    private static string? ExpandEnvironmentVariables(string? value, Dictionary<string, string?> data)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Match ${VAR_NAME} or $VAR_NAME patterns
        var variablePattern = VariableNamePattern();
        
        return variablePattern.Replace(value, match =>
        {
            // Get variable name from either ${VAR} or $VAR format
            string varName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            
            // First, try to get the value from the internal data dictionary
            if (data.TryGetValue(varName, out string? internalValue))
            {
                return internalValue ?? string.Empty;
            }
            
            // Fall back to system environment variable
            string? envValue = Environment.GetEnvironmentVariable(varName);
            
            // Return the environment variable value or the original match if not found
            return envValue ?? match.Value;
        });
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex KeyFormatPattern();

    [GeneratedRegex(@"\$\{([^}]+)\}|\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled)]
    private static partial Regex VariableNamePattern();
}
