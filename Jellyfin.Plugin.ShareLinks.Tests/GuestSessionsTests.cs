using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Which accounts a share stopping leaves with nothing to watch, and what is asked
/// of the server about them (#55).
/// </summary>
/// <remarks>
/// <para>
/// The routine is driven directly here and through the revocation route in
/// <c>AdministratorRouteTests</c>. The two are not the same assertion: this one
/// judges the arithmetic over a list of records, and that one judges that the
/// route hands it the store as it stands after the write rather than before it,
/// which is the mistake the routine cannot see from the inside.
/// </para>
/// <para>
/// Nothing here reaches a server. What a session manager does with the ask is the
/// server's, and <c>docs/refused-tests.md</c> is where the test that would watch it
/// is refused with the reason.
/// </para>
/// </remarks>
public sealed class GuestSessionsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Item = new Guid("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// An invited account this plugin did not create is not signed out. It belongs
    /// to somebody who uses this server for their own watching, and the share
    /// ending is not a reason to end their session.
    /// </summary>
    [Fact]
    public void AnInvitedAccountThisPluginDidNotMakeIsLeftAlone()
    {
        var guest = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();
        var stopped = ARecord(
            revokedAt: Now,
            invited: new[] { guest, somebodyElse },
            pluginCreated: new[] { guest });

        Assert.Equal(
            new[] { guest },
            GuestSessions.LeftWithNothingToWatch(new[] { stopped }, stopped, Now));
    }

    /// <summary>
    /// An account a record names twice is asked about once. A repeat costs the
    /// server a second ask that changes nothing and costs a reader a list that does
    /// not match the accounts it is about.
    /// </summary>
    [Fact]
    public void AnAccountNamedTwiceIsAskedAboutOnce()
    {
        var guest = Guid.NewGuid();
        var stopped = ARecord(
            revokedAt: Now,
            invited: new[] { guest },
            pluginCreated: new[] { guest, guest });

        Assert.Equal(
            new[] { guest },
            GuestSessions.LeftWithNothingToWatch(new[] { stopped }, stopped, Now));
    }

    /// <summary>
    /// The order is the record's own, so what a reader compares against the record
    /// is in the order the record writes it.
    /// </summary>
    [Fact]
    public void TheOrderIsTheRecordsOwn()
    {
        var first = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var second = new Guid("11111111-0000-0000-0000-000000000002");
        var stopped = ARecord(
            revokedAt: Now,
            invited: new[] { first, second },
            pluginCreated: new[] { first, second });

        Assert.Equal(
            new[] { first, second },
            GuestSessions.LeftWithNothingToWatch(new[] { stopped }, stopped, Now));
    }

    /// <summary>
    /// Handed the store as it stood before the revocation, the routine finds the
    /// share itself still live and ends nothing. This is not a defect to repair
    /// here, it is the reason the caller has to read the store afterwards, and it
    /// is asserted so that a caller changed to read it first fails a test rather
    /// than silently signing nobody out.
    /// </summary>
    [Fact]
    public void TheListFromBeforeTheRevocationEndsNothing()
    {
        var guest = Guid.NewGuid();
        var before = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Empty(GuestSessions.LeftWithNothingToWatch(new[] { before }, before, Now));
    }

    /// <summary>
    /// Every account named is asked about, in order, and nothing else is asked of
    /// the server.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task EveryAccountNamedIsAskedAboutAndNothingElseIs()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var asked = new List<Guid>();
        var manager = new Mock<ISessionManager>(MockBehavior.Strict);
        manager
            .Setup(sessions => sessions.RevokeUserTokens(It.IsAny<Guid>(), string.Empty))
            .Callback((Guid account, string spared) => asked.Add(account))
            .Returns(Task.CompletedTask);

        await GuestSessions.EndAsync(manager.Object, new[] { first, second });

        Assert.Equal(new[] { first, second }, asked);
        manager.Verify(sessions => sessions.RevokeUserTokens(first, string.Empty), Times.Once);
        manager.Verify(sessions => sessions.RevokeUserTokens(second, string.Empty), Times.Once);
        manager.VerifyNoOtherCalls();
    }

    /// <summary>
    /// No account is no ask. A share whose guests all still hold something live
    /// must not reach the session manager at all.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task NoAccountIsNoAsk()
    {
        var manager = new Mock<ISessionManager>(MockBehavior.Strict);

        await GuestSessions.EndAsync(manager.Object, Array.Empty<Guid>());

        manager.VerifyNoOtherCalls();
    }

    private static ShareRecord ARecord(
        DateTimeOffset? revokedAt = null,
        IReadOnlyList<Guid>? invited = null,
        IReadOnlyList<Guid>? pluginCreated = null) => new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            ItemId = Item,
            InvitedUserIds = invited ?? Array.Empty<Guid>(),
            PluginCreatedUserIds = pluginCreated ?? Array.Empty<Guid>(),
            CreatedByUserId = Operator,
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = Now.AddDays(7),
            RevokedAt = revokedAt,
            RevokedByUserId = revokedAt is null ? null : Operator,
            TokenHash = "a-hash",
        };
}
