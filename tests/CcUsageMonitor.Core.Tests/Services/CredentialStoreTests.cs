using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Services;
using CcUsageMonitor.Core.Tests.Fakes;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class CredentialStoreTests
{
    private readonly string _tempDir;

    public CredentialStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    #region GetAccessToken

    [Fact]
    public void CredentialStore_GetAccessToken_WithValidFile_ReturnsToken()
    {
        // Arrange
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        var fixtureContent = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials.json"));
        File.WriteAllText(fixturePath, fixtureContent);

        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var token = store.GetAccessToken();

        // Assert
        Assert.Equal("sk-ant-example-token-12345", token);
    }

    [Fact]
    public void CredentialStore_GetAccessToken_MissingFile_ReturnsNull()
    {
        // Arrange
        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, Path.Combine(_tempDir, "nonexistent.json"));

        // Act
        var token = store.GetAccessToken();

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public void CredentialStore_GetAccessToken_CorruptJson_ReturnsNull()
    {
        // Arrange
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        File.WriteAllText(fixturePath, "{ this is not valid json!!!");

        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var token = store.GetAccessToken();

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public void CredentialStore_GetAccessToken_MissingAccessTokenField_ReturnsNull()
    {
        // Arrange
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        File.WriteAllText(fixturePath, "{\"claudeAiOauth\": {\"refreshToken\": \"abc\"}}");

        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var token = store.GetAccessToken();

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public void CredentialStore_GetAccessToken_MissingClaudeAiOauth_ReturnsNull()
    {
        // Arrange
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        File.WriteAllText(fixturePath, "{\"otherKey\": \"value\"}");

        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var token = store.GetAccessToken();

        // Assert
        Assert.Null(token);
    }

    #endregion

    #region RefreshAsync

    [Fact]
    public async Task CredentialStore_RefreshAsync_WithExpiredFileAndNonTimeout_ReturnsFalse()
    {
        // Arrange — expired file with exit code 0 (refresh ran but expiresAt didn't increase)
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        var fixtureContent = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials_expired.json"));
        File.WriteAllText(fixturePath, fixtureContent);

        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0); // CLI succeeded but didn't change expiresAt
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var success = await store.RefreshAsync(CancellationToken.None);

        // Assert — expiresAt unchanged (1700000000000 == 1700000000000) → false
        Assert.False(success);
    }

    [Fact]
    public async Task CredentialStore_RefreshAsync_Timeout_ReturnsFalse()
    {
        // Arrange
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        var fixtureContent = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials.json"));
        File.WriteAllText(fixturePath, fixtureContent);

        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SimulateTimeout(); // returns -1
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var success = await store.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.False(success);
    }

    [Fact]
    public async Task CredentialStore_RefreshAsync_WithRefreshedFile_ReturnsTrue()
    {
        // Arrange — write original fixture (expiresAt = 1781903830381)
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        var originalContent = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials.json"));
        var refreshedContent = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials_refreshed.json"));
        File.WriteAllText(fixturePath, originalContent);

        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        // Swap the file content during the refresh call (between before-read and after-read)
        fakeRunner.SetOnRun(() => File.WriteAllText(fixturePath, refreshedContent));
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var success = await store.RefreshAsync(CancellationToken.None);

        // Assert — refreshed expiresAt (1781903930381) > original (1781903830381) → true
        Assert.True(success);
    }

    [Fact]
    public async Task CredentialStore_RefreshAsync_MissingFile_ReturnsFalse()
    {
        // Arrange
        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        var store = new CredentialStore(fakeRunner, Path.Combine(_tempDir, "nonexistent.json"));

        // Act
        var success = await store.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.False(success);
    }

    [Fact]
    public async Task CredentialStore_RefreshAsync_EqualExpiresAt_ReturnsFalse()
    {
        // Arrange — same file read before and after; expiresAt stays the same
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        var fixtureContent = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials.json"));
        File.WriteAllText(fixturePath, fixtureContent);

        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var success = await store.RefreshAsync(CancellationToken.None);

        // Assert — before == after → false (spec §4.1 trap: strictly greater required)
        Assert.False(success);
    }

    #endregion

    #region Token secrecy

    [Fact]
    public void CredentialStore_GetAccessToken_TokenIsNonEmptyString()
    {
        // Arrange
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        var fixtureContent = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials.json"));
        File.WriteAllText(fixturePath, fixtureContent);

        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, fixturePath);

        // Act
        var token = store.GetAccessToken();

        // Assert — token is a non-empty string (not null, not whitespace)
        // The token is only exposed via the getter return value, never logged to streams
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.DoesNotContain("\n", token);
        Assert.DoesNotContain("\r", token);
    }

    #endregion
}
