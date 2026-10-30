namespace WhisperLoom.Core.Tests;

public class AppInfoTest
{
    [Fact]
    public void Name_is_WhisperLoom() => Assert.Equal("WhisperLoom", AppInfo.Name);
}
