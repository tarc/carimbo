using Carimbo.Llm;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Carimbo.Api.Tests.EvalEndpointTests;

namespace Carimbo.Api.Tests;

/// <summary>
/// The response cache's configuration (LLM-04, D-19, D-27): on by default in Development and Eval, relocatable and
/// switchable by configuration, and impossible to turn on anywhere else.
/// </summary>
public class CacheConfigurationTests
{
    private const string DefaultDirectory = ".cache/llm";

    // ---------------------------------------------------------------- on by default in Development and Eval

    [Theory]
    [InlineData("Development")]
    [InlineData("Eval")]
    public async Task The_cache_defaults_to_read_write_at_the_default_directory_under_the_content_root(string environment)
    {
        // The test host runs with the cache off unless a cache key is passed, so name the mode and leave the directory unset.
        var existedBefore = Directory.Exists(Path.GetFullPath(DefaultDirectory));

        await using var host = await TestHost.StartAsync(
            environment, Key, new ScriptedGateway(), new Dictionary<string, string?> { ["Llm:Cache:Mode"] = "read_write" });

        var expected = Path.GetFullPath(DefaultDirectory, host.App.Environment.ContentRootPath);
        var settings = host.App.Services.GetRequiredService<LlmCacheSettings>();
        Assert.Equal(expected, settings.Directory);
        Assert.Equal(LlmCacheMode.ReadWrite, settings.DefaultMode);
        Assert.Contains(host.Logs.Messages, m => m.Contains($"response cache: read_write at {expected}"));

        // Starting never creates the directory; the store does that on the first stored answer.
        Assert.Equal(Path.GetFullPath(DefaultDirectory), expected);
        Assert.Equal(existedBefore, Directory.Exists(expected));
    }

    [Fact]
    public async Task A_directory_alone_defaults_the_mode_to_read_write()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development",
                Key,
                CountingGateway(calls),
                new Dictionary<string, string?> { ["Llm:Cache:Directory"] = directory.FullName });

            await host.PostAsync(Body(), Key);
            await host.PostAsync(Body(), Key);

            Assert.Equal(1, calls.Value);
            Assert.Single(directory.GetFiles());
            Assert.Equal(LlmCacheMode.ReadWrite, host.App.Services.GetRequiredService<LlmCacheSettings>().DefaultMode);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_relative_directory_resolves_against_the_content_root()
    {
        var relative = $"rel-cache-{Guid.NewGuid():N}";
        string? resolved = null;
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(relative, "read_write"));
            resolved = Path.GetFullPath(relative, host.App.Environment.ContentRootPath);

            await host.PostAsync(Body(), Key);

            Assert.Equal(resolved, host.App.Services.GetRequiredService<LlmCacheSettings>().Directory);
            Assert.Single(new DirectoryInfo(resolved).GetFiles());
        }
        finally
        {
            if (resolved is not null && Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_configured_mode_replaces_the_default_mode()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            await using var host = await TestHost.StartAsync(
                "Development", Key, new ScriptedGateway(), CacheConfig(directory.FullName, "read_only"));

            Assert.Equal(LlmCacheMode.ReadOnly, host.App.Services.GetRequiredService<LlmCacheSettings>().DefaultMode);
            Assert.Contains(host.Logs.Messages, m => m.Contains($"response cache: read_only at {directory.FullName}"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- explicit off

    [Fact]
    public async Task Mode_off_registers_no_cache_even_with_a_directory()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "off"));

            await host.PostAsync(Body(), Key);
            await host.PostAsync(Body(), Key);

            Assert.Equal(2, calls.Value);
            Assert.Empty(directory.GetFiles());
            Assert.Null(host.App.Services.GetService<LlmCacheSettings>());
            Assert.Contains(host.Logs.Messages, m => m.Contains("response cache: off (Llm:Cache:Mode off)"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- refused outside Development and Eval

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task A_cache_directory_outside_development_and_eval_fails_startup(string environment)
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(
                environment,
                Key,
                new ScriptedGateway(),
                new Dictionary<string, string?> { ["Llm:Cache:Directory"] = directory.FullName }));

            Assert.Contains("Llm:Cache:Directory", error.Message);
            Assert.Contains(environment, error.Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("Production", "read_write")]
    [InlineData("Production", "read_only")]
    [InlineData("Staging", "refresh")]
    public async Task A_cache_mode_other_than_off_outside_development_and_eval_fails_startup(string environment, string mode)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(
            environment, Key, new ScriptedGateway(), new Dictionary<string, string?> { ["Llm:Cache:Mode"] = mode }));

        Assert.Contains("Llm:Cache:Mode", error.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Production_style_environments_start_with_the_cache_off_and_say_so(string environment)
    {
        var calls = new Counter();
        await using var host = await TestHost.StartAsync(environment, Key, CountingGateway(calls));

        Assert.Null(host.App.Services.GetService<LlmCacheSettings>());
        Assert.Contains(host.Logs.Messages, m => m.Contains($"response cache: off (environment {environment})"));
    }

    [Fact]
    public async Task Mode_off_with_a_directory_outside_development_and_eval_still_fails_startup()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(
            "Production", Key, new ScriptedGateway(), CacheConfig("anywhere", "off")));

        Assert.Contains("Llm:Cache:Directory", error.Message);
    }

    // ---------------------------------------------------------------- mode validation

    [Theory]
    [InlineData("Development", "readonly")]
    [InlineData("Development", "READ_ONLY")]
    [InlineData("Development", "read-only")]
    [InlineData("Production", "readonly")]
    [InlineData("Production", "READ_ONLY")]
    [InlineData("Production", "read-only")]
    public async Task A_misspelled_mode_fails_startup_in_every_environment(string environment, string mode)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(
            environment, Key, new ScriptedGateway(), new Dictionary<string, string?> { ["Llm:Cache:Mode"] = mode }));

        Assert.Contains("Llm:Cache:Mode", error.Message);
    }
}
