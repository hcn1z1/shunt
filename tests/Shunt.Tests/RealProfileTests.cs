using System.IO;
using System.Threading.Tasks;
using Shunt.Profiles;
using Xunit;

namespace Shunt.Tests;

public class RealProfileTests
{
    [Theory]
    [InlineData("profiles/Gamepad to Keyboard and Mouse.rewasd")]
    [InlineData("profiles/Keyboard and Mouse to Gamepad.rewasd")]
    [InlineData("profiles/Native Behavior.rewasd")]
    [InlineData("profiles/DefaultRadialMenu.rewasd")]
    public async Task ProfileLoader_LoadsExtractedProfiles_WithoutError(string relativePath)
    {
        // Path relative to execution directory or project root
        var fullPath = Path.GetFullPath(Path.Combine("../../../../../", relativePath));
        if (!File.Exists(fullPath))
        {
            fullPath = Path.GetFullPath(Path.Combine("../../../", relativePath));
        }

        if (File.Exists(fullPath))
        {
            var profile = await ProfileLoader.LoadFromFileAsync(fullPath);
            Assert.NotNull(profile);
            Assert.True(profile.SchemaVersion >= 3.0);
        }
    }
}
