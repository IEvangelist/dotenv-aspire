// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

namespace Microsoft.Extensions.Configuration.DotEnv;

/// <summary>
/// Represents a <c>.env</c> file as an <see cref="IConfigurationSource"/>.
/// </summary>
public sealed class DotEnvConfigurationSource : FileConfigurationSource
{
    /// <summary>
    /// Gets or sets the parsing options for the .env file.
    /// </summary>
    public DotEnvParseOptions ParseOptions { get; set; } = new();

    /// <summary>
    /// Builds the <see cref="DotEnvConfigurationProvider"/> for this source.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/>.</param>
    /// <returns>A <see cref="DotEnvConfigurationProvider"/>.</returns>
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);

        var fileInfo = FileProvider?.GetFileInfo(Path ?? string.Empty);
        if (fileInfo is null || !fileInfo.Exists)
        {
            if (Optional)
            {
                return new DotEnvConfigurationProvider(this);
            }

            throw new FileNotFoundException($"The .env file '{Path}' was not found.", Path);
        }

        return new DotEnvConfigurationProvider(this);
    }
}
