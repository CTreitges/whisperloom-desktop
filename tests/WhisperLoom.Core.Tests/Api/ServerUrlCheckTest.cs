using WhisperLoom.Core.Api;
using static WhisperLoom.Core.Api.ServerUrlCheck;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Auszug aus <c>api/ServerUrlCheckTest.kt</c> (nicht in der Soll-Liste, aber portierter Code braucht Tests).</summary>
public class ServerUrlCheckTest
{
    [Fact]
    public void PrivateAdressenErkannt()
    {
        foreach (var host in new[] { "localhost", "nas.local", "10.0.0.5", "172.16.1.1", "192.168.1.50", "100.64.0.1", "[::1]", "fd00::1", "169.254.1.1" })
        {
            Assert.True(IsPrivateHost(host), host);
        }
        foreach (var host in new[] { "", "api.openai.com", "8.8.8.8", "172.32.0.1", "100.128.0.1", "999.1.1.1" })
        {
            Assert.False(IsPrivateHost(host), host);
        }
    }

    [Fact]
    public void FehlerUndHinweise()
    {
        var custom = ProviderCatalog.Custom;
        Assert.Equal(new Problem(Severity.Error, MsgInvalid), Check(" ", custom));
        Assert.Equal(new Problem(Severity.Error, MsgScheme), Check("ftp://x/v1", custom));
        Assert.Equal(new Problem(Severity.Error, MsgHttpsRequired), Check("http://api.openai.com/v1", ProviderCatalog.OpenAi));
        Assert.Equal(new Problem(Severity.Warning, MsgPublicHttp), Check("http://example.com/v1", custom));
        Assert.Equal(new Problem(Severity.Warning, MsgV1), Check("http://192.168.1.5:8000", custom));
        Assert.Null(Check("http://192.168.1.5:8000/v1/", custom));
        Assert.Null(Check("https://api.openai.com/v1", ProviderCatalog.OpenAi));
        Assert.Null(Check("http://homeserver.lan:11434", ProviderCatalog.ById("ollama")));
    }
}
