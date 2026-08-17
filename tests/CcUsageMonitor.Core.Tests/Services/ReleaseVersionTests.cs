using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Services;

/// <summary>
/// Version comparison decides whether the user is told to upgrade, so a wrong answer here
/// is silent: either a missed release or a nag to "update" to what they already run.
/// Every case pins the exact parsed value rather than asserting a parse merely succeeded.
/// </summary>
public class ReleaseVersionTests
{
    // ---- TryParseTag ----

    [Fact]
    public void TryParseTag_StripsLeadingV()
    {
        Assert.True(ReleaseVersion.TryParseTag("v1.3.0", out var v));
        Assert.Equal(new Version(1, 3, 0), v);
    }

    [Fact]
    public void TryParseTag_StripsLeadingCapitalV()
    {
        Assert.True(ReleaseVersion.TryParseTag("V2.0.1", out var v));
        Assert.Equal(new Version(2, 0, 1), v);
    }

    [Fact]
    public void TryParseTag_AcceptsTagWithoutVPrefix()
    {
        Assert.True(ReleaseVersion.TryParseTag("1.3.0", out var v));
        Assert.Equal(new Version(1, 3, 0), v);
    }

    [Fact]
    public void TryParseTag_PadsTwoPartTagToThreeParts()
    {
        // "1.3" must become 1.3.0, not Version(1,3) whose Build is -1 and would
        // therefore compare as OLDER than 1.3.0.
        Assert.True(ReleaseVersion.TryParseTag("v1.3", out var v));
        Assert.Equal(new Version(1, 3, 0), v);
    }

    [Fact]
    public void TryParseTag_PadsSinglePartTagToThreeParts()
    {
        Assert.True(ReleaseVersion.TryParseTag("v2", out var v));
        Assert.Equal(new Version(2, 0, 0), v);
    }

    [Fact]
    public void TryParseTag_DropsFourthComponent()
    {
        // Releases are three-part; a revision must not make comparison lopsided.
        Assert.True(ReleaseVersion.TryParseTag("v1.3.0.7", out var v));
        Assert.Equal(new Version(1, 3, 0), v);
    }

    [Fact]
    public void TryParseTag_IgnoresPreReleaseSuffix()
    {
        // /releases/latest excludes pre-releases, so this is defensive: if a suffixed tag
        // ever arrives, compare on its numeric core rather than refusing outright.
        Assert.True(ReleaseVersion.TryParseTag("v1.4.0-beta.1", out var v));
        Assert.Equal(new Version(1, 4, 0), v);
    }

    [Fact]
    public void TryParseTag_IgnoresBuildMetadataSuffix()
    {
        Assert.True(ReleaseVersion.TryParseTag("v1.4.0+abc123", out var v));
        Assert.Equal(new Version(1, 4, 0), v);
    }

    [Fact]
    public void TryParseTag_TrimsSurroundingWhitespace()
    {
        Assert.True(ReleaseVersion.TryParseTag("  v1.3.0  ", out var v));
        Assert.Equal(new Version(1, 3, 0), v);
    }

    [Fact]
    public void TryParseTag_RejectsNull()
    {
        Assert.False(ReleaseVersion.TryParseTag(null, out _));
    }

    [Fact]
    public void TryParseTag_RejectsEmpty()
    {
        Assert.False(ReleaseVersion.TryParseTag("", out _));
    }

    [Fact]
    public void TryParseTag_RejectsWhitespaceOnly()
    {
        Assert.False(ReleaseVersion.TryParseTag("   ", out _));
    }

    [Fact]
    public void TryParseTag_RejectsNonNumericTag()
    {
        Assert.False(ReleaseVersion.TryParseTag("nightly", out _));
    }

    [Fact]
    public void TryParseTag_RejectsBareVPrefix()
    {
        Assert.False(ReleaseVersion.TryParseTag("v", out _));
    }

    [Fact]
    public void TryParseTag_RejectsNegativeComponent()
    {
        Assert.False(ReleaseVersion.TryParseTag("v1.-3.0", out _));
    }

    // ---- IsNewer ----

    [Fact]
    public void IsNewer_TrueWhenPatchIsHigher()
    {
        Assert.True(ReleaseVersion.IsNewer(new Version(1, 3, 1), new Version(1, 3, 0)));
    }

    [Fact]
    public void IsNewer_TrueWhenMinorIsHigher()
    {
        Assert.True(ReleaseVersion.IsNewer(new Version(1, 4, 0), new Version(1, 3, 9)));
    }

    [Fact]
    public void IsNewer_TrueWhenMajorIsHigher()
    {
        Assert.True(ReleaseVersion.IsNewer(new Version(2, 0, 0), new Version(1, 99, 99)));
    }

    [Fact]
    public void IsNewer_FalseWhenVersionsAreEqual()
    {
        Assert.False(ReleaseVersion.IsNewer(new Version(1, 3, 0), new Version(1, 3, 0)));
    }

    [Fact]
    public void IsNewer_FalseWhenRemoteIsOlder()
    {
        // A yanked release must never prompt a "downgrade".
        Assert.False(ReleaseVersion.IsNewer(new Version(1, 2, 0), new Version(1, 3, 0)));
    }

    [Fact]
    public void IsNewer_ComparesNumerically_NotLexically()
    {
        // Lexical comparison would rank "1.9.0" above "1.10.0".
        Assert.True(ReleaseVersion.IsNewer(new Version(1, 10, 0), new Version(1, 9, 0)));
    }
}
