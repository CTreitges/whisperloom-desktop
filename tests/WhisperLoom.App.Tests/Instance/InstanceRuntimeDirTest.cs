using System.Runtime.Versioning;
using WhisperLoom.App.Instance;

namespace WhisperLoom.App.Tests.Instance;

/// <summary>
/// TS-WLINUX (WLD-P2-24 Detail 4): Socket-Ordner unter Linux ohne <c>XDG_RUNTIME_DIR</c>. Gehört
/// <c>/tmp/whisperloom-&lt;user&gt;</c> jemand anderem oder ist er ein Symlink, nimmt die App einen eigenen Ausweichordner
/// mit 700 statt abzustürzen oder einen fremden Ordner zu benutzen.
/// </summary>
// Unter Windows überspringt LinuxOnly() jeden Fall mit Unix-Rechten.
[UnsupportedOSPlatform("windows")]
public sealed class InstanceRuntimeDirTest : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "wl-rt-" + Guid.NewGuid().ToString("N"));

    public InstanceRuntimeDirTest()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string Candidate => Path.Combine(_root, "whisperloom-user");

    private string Fallback => Path.Combine(_root, "home", ".cache", "whisperloom-run");

    private static void LinuxOnly()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix-Socket-Ordner nur unter Linux");
        Assert.SkipWhen(Environment.UserName == "root", "als root gelingt chmod auf fremde Ordner; der Test braucht einen normalen Benutzer");
    }

    [Fact]
    public void Uses_xdg_runtime_dir_when_it_exists()
    {
        Assert.Equal(_root, InstanceEndpoint.RuntimeDir(_root, Candidate, Fallback, isRoot: false));
    }

    [Fact]
    public void Creates_the_tmp_dir_with_owner_only_rights()
    {
        LinuxOnly();
        Assert.Equal(Candidate, InstanceEndpoint.RuntimeDir(null, Candidate, Fallback, isRoot: false));
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(Candidate));
    }

    [Fact]
    public void Own_tmp_dir_with_loose_rights_is_tightened()
    {
        LinuxOnly();
        Directory.CreateDirectory(Candidate);
        File.SetUnixFileMode(Candidate, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        Assert.Equal(Candidate, InstanceEndpoint.RuntimeDir(null, Candidate, Fallback, isRoot: false));
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(Candidate));
    }

    // Fremder Besitzer: /usr gehört root (755); chmod gelingt einem normalen Benutzer nicht. Vorher: UnauthorizedAccessException.
    [Fact]
    public void Foreign_owned_tmp_dir_is_not_used_but_a_private_fallback()
    {
        LinuxOnly();
        var dir = InstanceEndpoint.RuntimeDir(null, "/usr", Fallback, isRoot: false);

        Assert.Equal(Fallback, dir);
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(Fallback));
    }

    // Symlink eines anderen auf einen Ordner, den er lesen kann: nie folgen, nie dessen Rechte ändern.
    [Fact]
    public void Symlinked_tmp_dir_is_not_followed()
    {
        LinuxOnly();
        var target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target);
        File.SetUnixFileMode(target, OwnerOnly | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Directory.CreateSymbolicLink(Candidate, target);

        Assert.Equal(Fallback, InstanceEndpoint.RuntimeDir(null, Candidate, Fallback, isRoot: false));
        Assert.Equal(OwnerOnly | UnixFileMode.OtherRead | UnixFileMode.OtherExecute, File.GetUnixFileMode(target));
    }

    // root: chmod gelingt auch auf fremden Ordnern, beweist also keinen Besitz → /tmp gar nicht erst benutzen.
    [Fact]
    public void Root_never_uses_the_shared_tmp_dir()
    {
        LinuxOnly();
        Assert.Equal(Fallback, InstanceEndpoint.RuntimeDir(null, Candidate, Fallback, isRoot: true));
        Assert.False(Directory.Exists(Candidate));
    }

    [Fact]
    public void Unusable_fallback_ends_with_a_clear_message()
    {
        LinuxOnly();
        var e = Assert.Throws<IOException>(() => InstanceEndpoint.RuntimeDir(null, "/usr", "/usr/share", isRoot: false));
        Assert.Contains("/usr/share", e.Message);
        Assert.Contains("XDG_RUNTIME_DIR", e.Message);
    }
}
