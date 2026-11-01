using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Anbieter-Typ „Firma“ (W2-ENR): Geräte-Token nur an genau den Proxy; Fehlerklasse budget.</summary>
public sealed class CompanyProviderTest
{
    private const string Proxy = "https://wl.example.com/api/v1/proxy";

    [Theory]
    [InlineData("https://wl.example.com/api/v1/proxy", true)]
    [InlineData("https://WL.example.com/api/v1/proxy/", true)]
    [InlineData("https://wl.example.com:443/api/v1/proxy", true)]
    [InlineData("http://wl.example.com/api/v1/proxy", false)]
    [InlineData("https://wl.example.com.evil.test/api/v1/proxy", false)]
    [InlineData("https://wl.example.com/api/v1/proxy/../../x", false)]
    [InlineData("https://wl.example.com/api/v1/proxyx", false)]
    [InlineData("https://wl.example.com:8443/api/v1/proxy", false)]
    [InlineData("https://wl.example.com/api/v1/proxy?x=1", false)]
    [InlineData("", false)]
    public void Nur_genau_der_Proxy_ist_ein_Firmen_Zugang(string url, bool expected) => Assert.Equal(expected, CompanyProvider.Targets(Proxy, url));

    [Fact]
    public void Ohne_Proxy_nie_ein_Firmen_Zugang()
    {
        Assert.False(CompanyProvider.Targets(null, Proxy));
        Assert.False(CompanyProvider.Targets("", Proxy));
        Assert.Equal(Proxy, CompanyProvider.ProxyBaseUrl("https://wl.example.com/"));
    }

    [Fact]
    public void Budget_ist_403_mit_budget_exhausted()
    {
        Assert.True(CompanyProvider.IsBudgetError(ApiErrors.Http(403, "budget_exhausted")));
        Assert.False(CompanyProvider.IsBudgetError(ApiErrors.Http(403, "forbidden")));
        Assert.False(CompanyProvider.IsBudgetError(ApiErrors.Http(429, "budget_exhausted")));
        Assert.False(CompanyProvider.IsBudgetError(new ApiNetworkException("budget_exhausted")));
    }
}
