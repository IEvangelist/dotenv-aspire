# Microsoft.Extensions.Configuration.DotEnv

Lightweight .env provider for .NET's configuration system. It loads key/value pairs from a .env file (or stream) into `IConfiguration`, with optional variable expansion, comment handling, duplicate-key behavior, and strict parsing.

Based on the [Environment Variables File Format Specification](https://github.com/env-lang/env/blob/main/env.md).

## What's here

- Library: `Microsoft.Extensions.Configuration.DotEnv/src`
- Tests: `Microsoft.Extensions.Configuration.DotEnv/tests`
- Demos: `Microsoft.Extensions.Configuration.DotEnv/DotEnvDemo.Api`, `Microsoft.Extensions.Configuration.DotEnv/DotEnvDemo.AppHost`

## Quick start

Add the provider to your configuration in `Program.cs` (or wherever you build configuration):

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.DotEnv;

var builder = WebApplication.CreateBuilder(args);

// Loads ./.env (optional by default)
builder.Configuration.AddDotEnvFile();

// Or specify path/behavior
builder.Configuration.AddDotEnvFile(path: ".env", optional: true, reloadOnChange: true);

// With parse options
var options = new DotEnvParseOptions
{
    EnableVariableExpansion = true,
    EnableAlternativeComments = false,
    DuplicateKeyBehavior = DuplicateKeyBehavior.UseLastValue,
    StrictParsing = false,
};
builder.Configuration.AddDotEnvFile(provider: null, path: ".env", optional: true, reloadOnChange: true, parseOptions: options);

// From a stream
// builder.Configuration.AddDotEnvStream(stream);
```

## .env syntax supported

- Format: `KEY=VALUE`
- Comments: lines starting with `#` (and optionally `;` or `//` when `EnableAlternativeComments` is true)
- Quoting:
  - Double quotes: supports escapes (`\n`, `\r`, `\t`, `\"`, `\\`) and variable expansion
  - Single quotes: literal, no expansion
  - Unquoted: trailing spaces trimmed; inline comments allowed after a space (`KEY=value # comment`)
- Variable expansion (when enabled): `${VAR}` or `$VAR` using previously-defined keys or environment variables
- Line continuation: end a line with a single `\` to continue on the next line
- Duplicate keys: configurable via `DuplicateKeyBehavior`

## Build and test (local repo)

```powershell
# Build library
dotnet build .\Microsoft.Extensions.Configuration.DotEnv\src\Microsoft.Extensions.Configuration.DotEnv.csproj

# Run tests
dotnet test .\Microsoft.Extensions.Configuration.DotEnv\tests\Microsoft.Extensions.Configuration.DotEnv.Tests.csproj

# Run demo API
dotnet run --project .\Microsoft.Extensions.Configuration.DotEnv\DotEnvDemo.Api\DotEnvDemo.Api.csproj
```

## Notes

- `AddDotEnvFile()` defaults to looking for `./.env` relative to the app base path and treats it as optional.
- Use `reloadOnChange: true` to watch for changes when a suitable file provider is available.
- Stream-based APIs (`AddDotEnvStream`) are useful for in-memory or embedded sources.