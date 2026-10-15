using Xunit;
using static Carimbo.Api.Tests.EvalEndpointTests;

namespace Carimbo.Api.Tests;

/// <summary>
/// The host's retry configuration (D-28, LLM-01): Llm:Anthropic:MaxRetries defaults to 2, is bounded to 0..5 at
/// startup and shows in the gateway line, which never carries the provider key.
/// </summary>
public class RetryConfigurationTests
{
    private static Dictionary<string, string?> Config(string? maxRetries, bool unreachable = false)
    {
        var config = WithKeys(DummyProviderKey, null, unreachable);
        if (maxRetries is not null)
        {
            config["Llm:Anthropic:MaxRetries"] = maxRetries;
        }

        return config;
    }

    [Fact]
    public async Task With_the_setting_unset_the_host_uses_two_retries_and_logs_it()
    {
        await using var host = await TestHost.StartAsync("Development", Key, gateway: null, Config(maxRetries: null));

        Assert.Contains(host.Logs.Messages, m => m.Contains("max retries 2"));
        Assert.DoesNotContain(host.Logs.Messages, m => m.Contains(DummyProviderKey));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("5")]
    public async Task A_configured_value_inside_the_bound_is_used_and_logged(string value)
    {
        await using var host = await TestHost.StartAsync("Development", Key, gateway: null, Config(value));

        Assert.Contains(host.Logs.Messages, m => m.Contains($"max retries {value}"));
        Assert.DoesNotContain(host.Logs.Messages, m => m.Contains(DummyProviderKey));
    }

    [Theory]
    [InlineData("6")]
    [InlineData("-1")]
    [InlineData("100")]
    public async Task A_value_outside_zero_to_five_fails_startup_naming_the_setting(string value)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestHost.StartAsync("Development", Key, gateway: null, Config(value)));

        Assert.Contains("Llm:Anthropic:MaxRetries", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DummyProviderKey, ex.Message, StringComparison.Ordinal);
    }
}
