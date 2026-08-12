using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A share outlives the server that wrote it, which is the condition #32 carries
/// of its own: the store round-trips a share across a restart.
/// </summary>
/// <remarks>
/// <para>
/// Two suites already stand either side of this and neither of them holds it.
/// <c>ShareRecordTests</c> round-trips a record member for member through
/// <c>JsonSerializer</c> and says of itself that the on-disk format is not what
/// it judges. <c>ShareStoreTests</c> writes and reads through the store and
/// compares the records by their hash alone, so a field the store dropped on the
/// way to the file leaves both of them green. Between the two there is no
/// statement that what an operator created is what comes back.
/// </para>
/// <para>
/// The restart is the second half and it is why a fresh <see cref="ShareStore"/>
/// is built rather than the writing one reused. A store that answered from what
/// it had written would pass every read in this repository and lose every share
/// on the next server start, and the object that wrote the file is the one place
/// that failure cannot be seen from.
/// </para>
/// <para>
/// A restart is the file surviving the object, so the writing store is disposed
/// before the second one opens. Nothing here starts a server, and no test in this
/// repository may, which <c>docs/testing.md</c> is where it is written down.
/// </para>
/// </remarks>
public sealed class ShareStoreRestartTests : IDisposable
{
    private readonly string _directory;

    public ShareStoreRestartTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-restart-" + Guid.NewGuid().ToString("N"));
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

    private string StorePath => Path.Combine(_directory, "shares.json");

    /// <summary>
    /// Gets a record with every member carrying a value, including the ones a
    /// live share leaves empty.
    /// </summary>
    /// <remarks>
    /// The revoked members are set here although a revoked share is not the
    /// ordinary case, because a member nobody set proves nothing about that
    /// member and the whole point of the comparison below is that it covers all
    /// of them. The hash stands in for one: what a keyed hash is made with is
    /// #43's, and the store neither reads it nor cares what produced it.
    /// </remarks>
    private static ShareRecord Everything() => new()
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = new Guid("77777777-7777-7777-7777-777777777777"),
        ItemId = new Guid("88888888-8888-8888-8888-888888888888"),
        InvitedUserIds =
        [
            new Guid("99999999-9999-9999-9999-999999999999"),
            new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        ],
        PluginCreatedUserIds = [new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")],
        CreatedByUserId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)),
        ExpiresAt = new DateTimeOffset(2026, 3, 11, 5, 6, 7, TimeSpan.FromHours(2)),
        RevokedAt = new DateTimeOffset(2026, 3, 5, 8, 9, 10, TimeSpan.FromHours(-5)),
        RevocationReason = "the operator took it down",
        RevokedByUserId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        MaxBitrateBitsPerSecond = 3_500_000,
        TokenHash = "bm90IGEgdG9rZW4sIGEgc3RhbmQtaW4gZm9yIG9uZQ",
    };

    private static PropertyInfo[] Members() =>
        typeof(ShareRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public async Task AShareComesBackFromARestartMemberForMember()
    {
        var written = Everything();

        // A comparison over a member the fixture left empty is a comparison of
        // two nulls, and it would report a member the store drops as one it
        // carried. The members are asked for through reflection rather than
        // listed, so the member somebody adds next is covered on the day it
        // arrives rather than on the day this file is remembered.
        var members = Members();
        Assert.NotEmpty(members);
        foreach (var member in members)
        {
            Assert.NotNull(member.GetValue(written));
        }

        var before = new ShareStore(StorePath);
        await before.MutateAsync(_ => new[] { written });

        // The server is gone. What is left is the file, and the second store is
        // built from nothing but its path.
        before.Dispose();

        var after = new ShareStore(StorePath);
        var restored = Assert.Single(await after.ReadAsync());
        after.Dispose();

        foreach (var member in members)
        {
            var expected = member.GetValue(written);
            var actual = member.GetValue(restored);

            if (expected is IEnumerable<Guid> sequence)
            {
                Assert.Equal(sequence, Assert.IsAssignableFrom<IEnumerable<Guid>>(actual));
                continue;
            }

            Assert.Equal(expected, actual);
        }
    }
}
