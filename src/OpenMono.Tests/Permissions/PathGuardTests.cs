using FluentAssertions;
using OpenMono.Permissions;

namespace OpenMono.Tests.Permissions;

public class PathGuardTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;

    public PathGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "omguard-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Validate_NormalFileInsideWorkspace_ReturnsNull()
    {
        var file = Path.Combine(_workspace, "src", "app.cs");
        PathGuard.Validate(file, _workspace).Should().BeNull();
    }

    [Fact]
    public void Validate_ProtectedEnvFileInsideWorkspace_IsDenied()
    {
        var env = Path.Combine(_workspace, ".env");
        PathGuard.Validate(env, _workspace).Should().NotBeNull();
    }

    [Fact]
    public void Validate_SymlinkEscapingWorkspace_IsDenied()
    {
        var secretDir = Path.Combine(_root, "secret");
        Directory.CreateDirectory(secretDir);
        var secret = Path.Combine(secretDir, "loot.txt");
        File.WriteAllText(secret, "top secret");

        var link = Path.Combine(_workspace, "innocent.txt");
        File.CreateSymbolicLink(link, secret);

        PathGuard.Validate(link, _workspace)
            .Should().NotBeNull("a symlink whose real target is outside the workspace must be denied");
    }

    [Fact]
    public void ValidateDirectory_SymlinkEscapingWorkspace_IsDenied()
    {
        var secretDir = Path.Combine(_root, "secretdir");
        Directory.CreateDirectory(secretDir);

        var link = Path.Combine(_workspace, "shortcut");
        Directory.CreateSymbolicLink(link, secretDir);

        PathGuard.ValidateDirectory(link, _workspace)
            .Should().NotBeNull("a directory symlink whose real target escapes the workspace must be denied");
    }
}
