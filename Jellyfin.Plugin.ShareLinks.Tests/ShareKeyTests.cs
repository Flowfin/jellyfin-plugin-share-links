using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using Jellyfin.Plugin.ShareLinks.Configuration;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What #28 asks of the key: that it is written once, that it fails closed when
/// it cannot be read, that rotating it stops what was issued under the old one,
/// and that it is never in the configuration file.
/// </summary>
/// <remarks>
/// Every test here creates its own directory under the temporary directory and
/// removes it. Nothing needs a server, a network or a right this process does not
/// already have, which is `docs/testing.md`'s rule.
/// </remarks>
public sealed class ShareKeyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly string _directory;

    public ShareKeyTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover directory under the temporary directory is not worth
            // failing a green suite over.
        }
    }

    private string KeyPath => Path.Combine(_directory, "share-key");

    [Fact]
    public void FirstRunWritesAKeyOfTheWidthTheHashRequires()
    {
        var key = new ShareKeyFile(KeyPath).Read();

        Assert.Equal(ShareTokens.KeyBytes, key.Length);
        Assert.True(key.Length >= ShareTokenHash.MinimumKeyBytes);
        Assert.True(File.Exists(KeyPath));
    }

    [Fact]
    public void TheKeyIsTheSameOnEveryReadAfterTheFirst()
    {
        var file = new ShareKeyFile(KeyPath);

        Assert.Equal(file.Read(), file.Read());
        Assert.Equal(file.Read(), new ShareKeyFile(KeyPath).Read());
    }

    [Fact]
    public void TwoInstallsDoNotShareAKey()
    {
        var first = new ShareKeyFile(Path.Combine(_directory, "first", "share-key")).Read();
        var second = new ShareKeyFile(Path.Combine(_directory, "second", "share-key")).Read();

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The condition this issue leads with. An unreadable key makes resolution
    /// refuse, and the refusal is not a fresh key.
    /// </summary>
    [Fact]
    public void AKeyFileThatCannotBeReadMakesResolutionRefuseRatherThanSucceed()
    {
        var file = new ShareKeyFile(KeyPath);
        var key = file.Read();
        var records = new[] { ARecord(ShareTokenHash.Compute(key, "a-token")) };

        var before = ShareResolution.Resolve(records, file, "a-token", Invited, PluginStatus.Active, At(Now));
        Assert.True(before.IsResolved);

        using (MakeUnreadable())
        {
            var during = ShareResolution.Resolve(records, file, "a-token", Invited, PluginStatus.Active, At(Now));

            Assert.Equal(ShareRefusal.KeyUnavailable, during.Refusal);
            Assert.Null(during.Share);
        }

        // And nothing was replaced while it was unreadable, so the share that
        // existed before still resolves once the file is available again.
        Assert.Equal(key, file.Read());
        Assert.True(ShareResolution.Resolve(records, file, "a-token", Invited, PluginStatus.Active, At(Now)).IsResolved);
    }

    [Fact]
    public void AKeyFileOfTheWrongLengthIsRefusedAndNotReplaced()
    {
        File.WriteAllBytes(KeyPath, new byte[] { 1, 2, 3 });
        var file = new ShareKeyFile(KeyPath);

        var refusal = Assert.Throws<ShareKeyUnavailableException>(() => file.Read());

        Assert.Contains(KeyPath, refusal.Message, StringComparison.Ordinal);
        Assert.Equal(3, File.ReadAllBytes(KeyPath).Length);
    }

    [Fact]
    public void AnUnreadableKeyIsARefusalAndNeverADifferentAnswerToTheCaller()
    {
        var file = new ShareKeyFile(KeyPath);
        var key = file.Read();
        var records = new[] { ARecord(ShareTokenHash.Compute(key, "a-token")) };

        using var unreadable = MakeUnreadable();

        var unknownToken = ShareResolution.Resolve(records, file, "some-other-token", Invited, PluginStatus.Active, At(Now));
        var validToken = ShareResolution.Resolve(records, file, "a-token", Invited, PluginStatus.Active, At(Now));

        // Both refuse and neither carries a share, so the caller cannot tell a
        // token that names a share from one that does not while the key is gone.
        Assert.Null(unknownToken.Share);
        Assert.Null(validToken.Share);
        Assert.Equal(unknownToken.Refusal, validToken.Refusal);
    }

    [Fact]
    public void APluginThatIsNotActiveDoesNotReachTheKeyAtAll()
    {
        var file = new ShareKeyFile(KeyPath);

        var result = ShareResolution.Resolve(Array.Empty<ShareRecord>(), file, "a-token", Invited, PluginStatus.Disabled, At(Now));

        Assert.Equal(ShareRefusal.PluginNotActive, result.Refusal);
        Assert.False(File.Exists(KeyPath));
    }

    /// <summary>
    /// Rotation stops what was issued under the old key. That is what it is for,
    /// and the count it returns is how the operator gets to be told.
    /// </summary>
    [Fact]
    public void RotationInvalidatesEveryTokenIssuedUnderTheOldKey()
    {
        var file = new ShareKeyFile(KeyPath);
        var before = file.Read();
        var records = new[] { ARecord(ShareTokenHash.Compute(before, "a-token")) };

        Assert.True(ShareResolution.Resolve(records, file, "a-token", Invited, PluginStatus.Active, At(Now)).IsResolved);

        var rotation = file.Rotate(liveShares: records.Length);

        Assert.Equal(1, rotation.SharesInvalidated);
        Assert.NotEqual(before, file.Read());
        Assert.Equal(
            ShareRefusal.NoSuchShare,
            ShareResolution.Resolve(records, file, "a-token", Invited, PluginStatus.Active, At(Now)).Refusal);
    }

    [Fact]
    public void RotationSaysHowManySharesItStopped()
    {
        var file = new ShareKeyFile(KeyPath);

        Assert.Equal(7, file.Rotate(liveShares: 7).SharesInvalidated);
        Assert.Equal(0, file.Rotate(liveShares: 0).SharesInvalidated);
    }

    /// <summary>
    /// The key is not in the configuration file, and this asks the type rather
    /// than the current field list, so a key added later fails here.
    /// </summary>
    [Fact]
    public void TheKeyNeverAppearsInThePluginConfiguration()
    {
        foreach (var property in typeof(PluginConfiguration).GetProperties())
        {
            Assert.DoesNotContain("key", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", property.Name, StringComparison.OrdinalIgnoreCase);
        }

        // And nothing of the key's shape can be held by what is there: every
        // setting is a number or the public address, and none of them is bytes.
        foreach (var property in typeof(PluginConfiguration).GetProperties())
        {
            Assert.True(
                property.PropertyType == typeof(int) || property.PropertyType == typeof(string),
                $"PluginConfiguration.{property.Name} is a {property.PropertyType}, and a setting that is not a number or a string is where key material would fit.");
        }
    }

    /// <summary>
    /// On a platform with POSIX modes the file is owner read and write only. On
    /// Windows nothing is set and nothing is claimed, and the skip says so rather
    /// than passing quietly.
    /// </summary>
    [Fact]
    public void OnAPlatformWithPosixModesTheKeyIsOwnerOnly()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Not covered here. What a Windows server gives this file by
            // inheritance was not measured, and docs/share-store.md says so in
            // the same words rather than leaving this test looking green.
            return;
        }

        new ShareKeyFile(KeyPath).Read();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
    }

    /// <summary>
    /// Makes the key file unreadable for the length of a using block, by the
    /// means each platform actually offers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The branch is explicit rather than one mechanism hoped to work on both,
    /// because the failure of a hope here is a test that quietly asserts nothing.
    /// On Windows a second handle opened with no sharing is what a reader meets.
    /// Elsewhere the file's mode is taken away, which is the case an operator
    /// actually arrives in.
    /// </para>
    /// <para>
    /// Neither needs an elevated right, which `docs/testing.md` refuses. A test
    /// that had to change a file's owner would be one of those and would be
    /// skipped and disclosed rather than worked around.
    /// </para>
    /// </remarks>
    /// <returns>Something to dispose to put the file back.</returns>
    private IDisposable MakeUnreadable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new FileStream(KeyPath, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        var mode = File.GetUnixFileMode(KeyPath);
        File.SetUnixFileMode(KeyPath, UnixFileMode.None);
        return new RestoreTheMode(KeyPath, mode);
    }

    private sealed class RestoreTheMode : IDisposable
    {
        private readonly string _path;
        private readonly UnixFileMode _mode;

        public RestoreTheMode(string path, UnixFileMode mode)
        {
            _path = path;
            _mode = mode;
        }

        public void Dispose() => File.SetUnixFileMode(_path, _mode);
    }

    private static ShareRecord ARecord(string tokenHash) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Invited },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = Now.AddDays(7),
        TokenHash = tokenHash,
    };

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
