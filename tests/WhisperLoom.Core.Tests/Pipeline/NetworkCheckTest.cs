using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>Port von <c>api/NetworkCheckTest.kt</c>: Host-Einordnung und Entscheidung der Netz-Vorprüfung.</summary>
public sealed class NetworkCheckTest
{
    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:8000/v1")]
    [InlineData("http://192.168.1.10:11434")]
    [InlineData("http://10.0.0.5/v1")]
    [InlineData("http://172.16.0.1:8080/v1")]
    [InlineData("http://100.101.102.103:8000/v1")]
    [InlineData("https://nas.tail1234.ts.net/v1")]
    [InlineData("http://homeserver.local:11434")]
    [InlineData("http://nas.lan/v1")]
    [InlineData("http://pi.home.arpa:8000/v1")]
    [InlineData("http://[::1]:11434")]
    [InlineData("http://[fd12::1]:8000/v1")]
    [InlineData(" HTTP://Server.LOCAL:11434 ")]
    public void ServerImEigenenNetz(string url) => Assert.True(NetworkAvailability.IsOwnNetwork(url), url);

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai")]
    [InlineData("https://ollama.com")]
    [InlineData("https://whisper.example.de/v1")]
    [InlineData("http://8.8.8.8/v1")]
    [InlineData("https://ts.net.example.com/v1")]
    [InlineData("")]
    [InlineData("kein url")]
    public void OeffentlicheServer(string url) => Assert.False(NetworkAvailability.IsOwnNetwork(url), url);

    [Fact]
    public void OhneAktivesNetzNieVerfuegbar()
    {
        Assert.False(NetworkAvailability.Available(activeNetwork: false, validated: false, ownNetwork: false));
        Assert.False(NetworkAvailability.Available(activeNetwork: false, validated: false, ownNetwork: true));
        Assert.False(NetworkAvailability.Available(activeNetwork: false, validated: true, ownNetwork: true));
    }

    [Fact]
    public void InternetBrauchtValidiertesNetz()
    {
        Assert.True(NetworkAvailability.Available(activeNetwork: true, validated: true, ownNetwork: false));
        Assert.False(NetworkAvailability.Available(activeNetwork: true, validated: false, ownNetwork: false));
    }

    [Fact]
    public void NachGelungenerAnfrageReichtEinAktivesNetz()
    {
        Assert.True(NetworkAvailability.Available(activeNetwork: true, validated: false, ownNetwork: false, proven: true));
        Assert.False(NetworkAvailability.Available(activeNetwork: false, validated: false, ownNetwork: false, proven: true));
    }

    [Fact]
    public void EigenesNetzReichtEinAktivesNetz()
    {
        Assert.True(NetworkAvailability.Available(activeNetwork: true, validated: false, ownNetwork: true));
        Assert.True(NetworkAvailability.Available(activeNetwork: true, validated: true, ownNetwork: true));
    }

    /// <summary>Desktop: ein Server auf Loopback (Ollama, whisper-server) braucht nie eine Netzwerkschnittstelle.</summary>
    [Fact]
    public void LoopbackIstImmerErreichbar()
    {
        Assert.True(NetworkAvailability.For("http://127.0.0.1:11434", proven: false));
        Assert.True(NetworkAvailability.For("http://localhost:8080/v1", proven: false));
    }
}
