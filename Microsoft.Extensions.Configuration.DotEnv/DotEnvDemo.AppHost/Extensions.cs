using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration.DotEnv;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public static class Extensions
{
    /// <summary>
    /// Adds all resolved environment variables to the resource that
    /// correspond to the specified <c>.env</c> file.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="dotEnvPath">The path to the .env file.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<T> WithEnvironment<T>(
        this IResourceBuilder<T> builder,
        string dotEnvPath)
        where T : IResourceWithEnvironment
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotEnvPath, "Path to .env file cannot be null or empty.");

        builder.WithEnvironment(context =>
        {
            var loggerService = context.ExecutionContext
                .ServiceProvider
                .GetRequiredService<ResourceLoggerService>();

            var logger = loggerService.GetLogger(context.Resource);

            var config = builder.ApplicationBuilder.Configuration;
            if (config is null)
            {
                throw new InvalidOperationException(
                    "IConfigurationManager is not registered in the service provider.");
            }

            config.AddDotEnvFile(path: dotEnvPath);

            if (config is IConfigurationRoot root)
            {
                var provider = root.Providers.OfType<DotEnvConfigurationProvider>().FirstOrDefault();
                if (provider is null)
                {
                    logger.LogWarning(
                        "No DotEnvConfigurationProvider found for the specified .env file at '{Path}'.",
                        dotEnvPath);

                    return;
                }

                foreach (var key in provider.GetChildKeys([], null))
                {
                    if (!provider.TryGet(key, out var value) || value is null)
                    {
                        continue;
                    }

                    // Add the environment variable to the context
                    context.EnvironmentVariables[key] = value;
                }
            }
        });

        return builder;
    }
}
