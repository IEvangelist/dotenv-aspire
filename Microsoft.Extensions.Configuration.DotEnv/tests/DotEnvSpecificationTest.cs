// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Extensions.Configuration.DotEnv.Tests;
using Xunit;

namespace Microsoft.Extensions.Configuration.DotEnv.Test;

/// <summary>
/// Tests for .env file specification compliance according to https://github.com/env-lang/env/blob/main/env.md
/// </summary>
public class DotEnvSpecificationTest
{
    [Fact]
    public void StrictParsing_ThrowsErrorForInvalidLines()
    {
        var dotenv = """
            VALID_KEY=valid_value
            INVALID_LINE_NO_EQUALS
            ANOTHER_VALID_KEY=another_value
            """;

        var options = new DotEnvParseOptions
        {
            StrictParsing = true
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        
        var exception = Assert.Throws<DotEnvParseException>(() => dotenvProvider.Load());
        Assert.Equal("ENV001", exception.ErrorCode);
        Assert.Equal(2, exception.LineNumber);
        Assert.Contains("Invalid line format", exception.Message);
    }

    [Fact]
    public void StrictParsing_ValidatesKeyFormat()
    {
        var dotenv = """
            VALID_KEY=value
            123INVALID=value
            """;

        var options = new DotEnvParseOptions
        {
            StrictParsing = true
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        
        var exception = Assert.Throws<DotEnvParseException>(() => dotenvProvider.Load());
        Assert.Equal("ENV003", exception.ErrorCode);
        Assert.Equal(2, exception.LineNumber);
        Assert.Contains("Invalid key format", exception.Message);
    }

    [Fact]
    public void LineContinuation_ConcatenatesMultipleLines()
    {
        var dotenv = """
            LONG_MESSAGE=first line \
            second line \
            third line
            NORMAL_KEY=normal_value
            """;

        var options = new DotEnvParseOptions
        {
            StrictParsing = true
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        dotenvProvider.Load();

        Assert.Equal("first line \nsecond line \nthird line", dotenvProvider.Get("LONG_MESSAGE"));
        Assert.Equal("normal_value", dotenvProvider.Get("NORMAL_KEY"));
    }

    [Fact]
    public void LineContinuation_ThrowsErrorForCommentOnContinuationLine()
    {
        var dotenv = """
            SECRET=password\
            # This comment is not allowed
            """;

        var options = new DotEnvParseOptions
        {
            StrictParsing = true
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        
        var exception = Assert.Throws<DotEnvParseException>(() => dotenvProvider.Load());
        Assert.Equal("ENV005", exception.ErrorCode);
        Assert.Contains("Comments are not allowed on line continuation", exception.Message);
    }

    [Fact]
    public void LineContinuation_ThrowsErrorForDanglingContinuation()
    {
        var dotenv = """
            VALUE=first\
            """;

        var options = new DotEnvParseOptions
        {
            StrictParsing = true
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        
        var exception = Assert.Throws<DotEnvParseException>(() => dotenvProvider.Load());
        Assert.Equal("ENV005", exception.ErrorCode);
        Assert.Contains("Invalid line continuation at end of file", exception.Message);
    }

    [Fact]
    public void AlternativeComments_SupportsSemicolonAndDoubleSlash()
    {
        var dotenv = """
            # Hash comment
            KEY1=value1
            ; Semicolon comment
            KEY2=value2
            // Double slash comment
            KEY3=value3
            KEY4=value4 ; Inline semicolon comment
            KEY5=value5 // Inline double slash comment
            """;

        var options = new DotEnvParseOptions
        {
            EnableAlternativeComments = true
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        dotenvProvider.Load();

        Assert.Equal("value1", dotenvProvider.Get("KEY1"));
        Assert.Equal("value2", dotenvProvider.Get("KEY2"));
        Assert.Equal("value3", dotenvProvider.Get("KEY3"));
        Assert.Equal("value4", dotenvProvider.Get("KEY4"));
        Assert.Equal("value5", dotenvProvider.Get("KEY5"));
    }

    [Fact]
    public void DuplicateKeyBehavior_ThrowsError()
    {
        var dotenv = """
            KEY=value1
            KEY=value2
            """;

        var options = new DotEnvParseOptions
        {
            DuplicateKeyBehavior = DuplicateKeyBehavior.ThrowError
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        
        var exception = Assert.Throws<DotEnvParseException>(() => dotenvProvider.Load());
        Assert.Equal("ENV002", exception.ErrorCode);
        Assert.Contains("Duplicate key", exception.Message);
    }

    [Fact]
    public void DuplicateKeyBehavior_UseFirstValue()
    {
        var dotenv = """
            KEY=first_value
            KEY=second_value
            """;

        var options = new DotEnvParseOptions
        {
            DuplicateKeyBehavior = DuplicateKeyBehavior.UseFirstValue
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        dotenvProvider.Load();

        Assert.Equal("first_value", dotenvProvider.Get("KEY"));
    }

    [Fact]
    public void VariableExpansion_CanBeDisabledPerSpec()
    {
        Environment.SetEnvironmentVariable("TEST_VAR", "test_value");

        try
        {
            var dotenv = """
                UNEXPANDED=${TEST_VAR}
                ALSO_UNEXPANDED=$TEST_VAR
                """;

            var options = new DotEnvParseOptions
            {
                EnableVariableExpansion = false
            };

            var dotenvConfigSrc = new DotEnvStreamConfigurationSource
            {
                Stream = TestStreamHelpers.StringToStream(dotenv),
                ParseOptions = options
            };

            var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
            dotenvProvider.Load();

            Assert.Equal("${TEST_VAR}", dotenvProvider.Get("UNEXPANDED"));
            Assert.Equal("$TEST_VAR", dotenvProvider.Get("ALSO_UNEXPANDED"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_VAR", null);
        }
    }

    [Fact]
    public void VariableExpansion_CanBeEnabledPerSpec()
    {
        var value = "test_value";
        Environment.SetEnvironmentVariable("TEST_VAR", value);

        try
        {
            var dotenv = """
                UNEXPANDED=${TEST_VAR}
                ALSO_UNEXPANDED=$TEST_VAR
                """;

            var options = new DotEnvParseOptions
            {
                EnableVariableExpansion = true
            };

            var dotenvConfigSrc = new DotEnvStreamConfigurationSource
            {
                Stream = TestStreamHelpers.StringToStream(dotenv),
                ParseOptions = options
            };

            var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
            dotenvProvider.Load();

            Assert.Equal(value, dotenvProvider.Get("UNEXPANDED"));
            Assert.Equal(value, dotenvProvider.Get("ALSO_UNEXPANDED"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_VAR", null);
        }
    }

    [Fact]
    public void QuotedValues_PreserveWhitespace()
    {
        var dotenv = """
            UNQUOTED_VALUE=   trimmed   
            DOUBLE_QUOTED="  preserved  "
            SINGLE_QUOTED='  also preserved  '
            """;

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv)
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        dotenvProvider.Load();

        Assert.Equal("   trimmed", dotenvProvider.Get("UNQUOTED_VALUE"));
        Assert.Equal("  preserved  ", dotenvProvider.Get("DOUBLE_QUOTED"));
        Assert.Equal("  also preserved  ", dotenvProvider.Get("SINGLE_QUOTED"));
    }

    [Fact]
    public void ValidKeyFormats_AreAccepted()
    {
        var dotenv = """
            FOO=value1
            foo=value2
            FOO_BAR=value3
            _FOO=value4
            FOO123=value5
            FOO_123_BAR=value6
            """;

        var options = new DotEnvParseOptions
        {
            StrictParsing = true
        };

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv),
            ParseOptions = options
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        dotenvProvider.Load();

        // Note: keys are case insensitive, so these will all map to the same key
        Assert.Equal("value6", dotenvProvider.Get("FOO_123_BAR")); // Last value wins
        Assert.Equal("value4", dotenvProvider.Get("_FOO"));
    }

    [Fact]
    public void InvalidKeyFormats_ThrowErrorsInStrictMode()
    {
        var invalidKeys = new[]
        {
            "123FOO=value",     // Cannot start with number
            "FOO-BAR=value",    // Cannot contain hyphens
            ".FOO=value",       // Cannot start with period
            "FOO.BAR=value",    // Cannot contain periods
            "FOO BAR=value",    // Cannot contain spaces
        };

        var options = new DotEnvParseOptions
        {
            StrictParsing = true
        };

        foreach (var invalidKey in invalidKeys)
        {
            var dotenvConfigSrc = new DotEnvStreamConfigurationSource
            {
                Stream = TestStreamHelpers.StringToStream(invalidKey),
                ParseOptions = options
            };

            var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
            
            var exception = Assert.Throws<DotEnvParseException>(() => dotenvProvider.Load());
            Assert.Equal("ENV003", exception.ErrorCode);
        }
    }

    [Fact]
    public void InlineComments_AreHandledCorrectly()
    {
        var dotenv = """
            KEY1=value # This is a comment
            KEY2="value with # in quotes"
            KEY3='value with # in single quotes'
            KEY4=value# No space before hash
            """;

        var dotenvConfigSrc = new DotEnvStreamConfigurationSource
        {
            Stream = TestStreamHelpers.StringToStream(dotenv)
        };

        var dotenvProvider = new DotEnvStreamConfigurationProvider(dotenvConfigSrc);
        dotenvProvider.Load();

        Assert.Equal("value", dotenvProvider.Get("KEY1"));
        Assert.Equal("value with # in quotes", dotenvProvider.Get("KEY2"));
        Assert.Equal("value with # in single quotes", dotenvProvider.Get("KEY3"));
        Assert.Equal("value# No space before hash", dotenvProvider.Get("KEY4")); // No space, so # is part of value
    }
}
