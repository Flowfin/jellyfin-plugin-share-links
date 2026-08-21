using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Which accounts a sweep releases, and what removing them does and does not do
/// (#238).
/// </summary>
/// <remarks>
/// <para>
/// The two halves are judged apart because they fail apart. Which accounts are
/// released is arithmetic over records and is where the guard that keeps somebody
/// else's account out of the list lives. What the removal does is a sequence of
/// asks of the server, and what is worth asserting there is the one an operator
/// meets: some of them failing.
/// </para>
/// <para>
/// Nothing here reaches a server. That a server deletes an account when it is
/// asked, and what it does to the sessions and the data of one, is the server's
/// and is asserted by nothing in this repository, which <c>docs/testing.md</c> is
/// where that rule is written.
/// </para>
/// </remarks>
public sealed class GuestAccountRemovalTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Item = new Guid("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// An account whose last record has been swept is released. The record that
    /// claimed it is the thing that is gone, which is what
    /// <c>docs/guest-accounts.md</c> makes the trigger.
    /// </summary>
    [Fact]
    public void AnAccountWhoseLastRecordWasSweptIsReleased()
    {
        var guest = Guid.NewGuid();
        var swept = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Equal(
            new[] { guest },
            GuestAccounts.ReleasedBy(new[] { swept }, Array.Empty<ShareRecord>()));
    }

    /// <summary>
    /// An account a surviving record still names is not released, whatever that
    /// record's state is. The last record naming it matters and not the last live
    /// one: an expired record is still a record, and the account goes when the
    /// record goes.
    /// </summary>
    [Fact]
    public void AnAccountASurvivingRecordStillNamesIsNotReleased()
    {
        var guest = Guid.NewGuid();
        var swept = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        var kept = ARecord(expiresAt: Now.AddDays(-1), invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Empty(GuestAccounts.ReleasedBy(new[] { swept, kept }, new[] { kept }));
    }

    /// <summary>
    /// An invited account the record does not claim under
    /// <c>WasCreatedByThisPlugin</c> is not released. It belongs to whoever made
    /// it, and this is the call that would delete it on their own server.
    /// </summary>
    [Fact]
    public void AnInvitedAccountThisPluginDidNotMakeIsNotReleased()
    {
        var somebodyElse = Guid.NewGuid();
        var swept = ARecord(invited: new[] { somebodyElse }, pluginCreated: Array.Empty<Guid>());

        Assert.Empty(GuestAccounts.ReleasedBy(new[] { swept }, Array.Empty<ShareRecord>()));
    }

    /// <summary>
    /// An identifier a hand edit put among a record's created accounts without
    /// inviting it is not claimed, so it is not released. A store carried forward
    /// and a record somebody edited are the two ways an identifier reaches that
    /// list without an account behind it.
    /// </summary>
    [Fact]
    public void AnAccountAHandEditPutAmongTheCreatedOnesIsNotReleased()
    {
        var nominated = Guid.NewGuid();
        var edited = ARecord(invited: Array.Empty<Guid>(), pluginCreated: new[] { nominated });

        Assert.Empty(GuestAccounts.ReleasedBy(new[] { edited }, Array.Empty<ShareRecord>()));
    }

    /// <summary>
    /// An account two swept records both claim is released once. A repeat is a
    /// second deletion of something that is already gone, and a list with a repeat
    /// in it is a list a count cannot be read off.
    /// </summary>
    [Fact]
    public void AnAccountTwoSweptRecordsClaimIsReleasedOnce()
    {
        var guest = Guid.NewGuid();
        var first = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        var second = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Equal(
            new[] { guest },
            GuestAccounts.ReleasedBy(new[] { first, second }, Array.Empty<ShareRecord>()));
    }

    /// <summary>
    /// A record that is still there releases nothing, however many accounts it
    /// claims. Reading the claims off every record rather than off the ones that
    /// went would delete the accounts of every share on the server at the first
    /// create.
    /// </summary>
    [Fact]
    public void ARecordThatSurvivedReleasesNothing()
    {
        var guest = Guid.NewGuid();
        var kept = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Empty(GuestAccounts.ReleasedBy(new[] { kept }, new[] { kept }));
    }

    /// <summary>
    /// Every account released is asked about, and the answer says so.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task EveryReleasedAccountIsRemoved()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var server = new AServer();

        var outcome = await GuestAccounts.RemoveAsync(server.Manager, new[] { first, second }, NullLogger());

        Assert.Equal(new[] { first, second }, server.Deleted);
        Assert.Equal(new[] { first, second }, outcome.Removed);
        Assert.Empty(outcome.LeftBehind);
    }

    /// <summary>
    /// A removal that fails part way leaves a named state rather than a set of
    /// accounts half gone. The refusal in the middle does not stop the one after
    /// it, because the record that claimed it is already swept and nothing will
    /// look at it again.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ARemovalThatFailsPartWayNamesWhatItLeftBehind()
    {
        var first = Guid.NewGuid();
        var refused = Guid.NewGuid();
        var third = Guid.NewGuid();
        var server = new AServer { Refuses = { refused } };

        var outcome = await GuestAccounts.RemoveAsync(server.Manager, new[] { first, refused, third }, NullLogger());

        Assert.Equal(new[] { first, third }, outcome.Removed);
        Assert.Equal(new[] { refused }, outcome.LeftBehind);
        Assert.Equal(new[] { first, third }, server.Deleted);
    }

    /// <summary>
    /// An account the server says it does not have is counted as removed. The call
    /// asked for it to be gone and it is gone, and a second pass over the same
    /// identifier is the ordinary way that happens.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnAccountTheServerDoesNotHaveIsRemovedRatherThanLeftBehind()
    {
        var gone = Guid.NewGuid();
        var server = new AServer { NotThere = { gone } };

        var outcome = await GuestAccounts.RemoveAsync(server.Manager, new[] { gone }, NullLogger());

        Assert.Equal(new[] { gone }, outcome.Removed);
        Assert.Empty(outcome.LeftBehind);
    }

    /// <summary>
    /// The line about a removal counts the accounts and names none of them, which
    /// is the rule every other line in this plugin follows. What it costs is that
    /// an operator learns how many accounts went and not which, and that is stated
    /// on the routine rather than left to be discovered.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheLineCountsTheAccountsAndNamesNone()
    {
        var removed = Guid.NewGuid();
        var refused = Guid.NewGuid();
        var log = new RecordingLog();
        var server = new AServer { Refuses = { refused } };

        await GuestAccounts.RemoveAsync(server.Manager, new[] { removed, refused }, log);

        Assert.Equal(2, log.Lines.Count);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Information);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Warning);

        foreach (var line in log.Lines)
        {
            foreach (var account in new[] { removed, refused })
            {
                Assert.DoesNotContain(account.ToString("N", CultureInfo.InvariantCulture), line.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(account.ToString("D", CultureInfo.InvariantCulture), line.Text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// A removal that had nothing to remove writes nothing. Every create sweeps,
    /// so a line per create saying that no account was released is the ordinary
    /// case filling the log until the interesting one cannot be found in it.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ARemovalWithNothingToRemoveWritesNoLine()
    {
        var log = new RecordingLog();

        var outcome = await GuestAccounts.RemoveAsync(new AServer().Manager, Array.Empty<Guid>(), log);

        Assert.Empty(outcome.Removed);
        Assert.Empty(outcome.LeftBehind);
        Assert.Empty(log.Lines);
    }

    private static ILogger NullLogger() => new RecordingLog();

    private static ShareRecord ARecord(
        DateTimeOffset? expiresAt = null,
        IReadOnlyList<Guid>? invited = null,
        IReadOnlyList<Guid>? pluginCreated = null) => new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            ItemId = Item,
            InvitedUserIds = invited ?? Array.Empty<Guid>(),
            PluginCreatedUserIds = pluginCreated ?? Array.Empty<Guid>(),
            CreatedByUserId = Operator,
            CreatedAt = Now.AddDays(-2),
            ExpiresAt = expiresAt ?? Now.AddDays(-1),
            TokenHash = "not-a-token",
        };

    // A server, as far as the removal can see one: which accounts it was asked to
    // delete, which ones it refuses, and which ones it says it never had. Strict,
    // so a routine reaching for a member nobody expected fails here rather than
    // passing quietly.
    private sealed class AServer
    {
        private readonly Mock<IUserManager> _manager = new Mock<IUserManager>(MockBehavior.Strict);

        public AServer()
        {
            _manager
                .Setup(manager => manager.DeleteUserAsync(It.IsAny<Guid>()))
                .Returns((Guid id) =>
                {
                    if (NotThere.Contains(id))
                    {
                        throw new ArgumentException("there is no account with that identifier", nameof(id));
                    }

                    if (Refuses.Contains(id))
                    {
                        throw new InvalidOperationException("this account may not be deleted");
                    }

                    Deleted.Add(id);
                    return Task.CompletedTask;
                });
        }

        public IUserManager Manager => _manager.Object;

        public List<Guid> Deleted { get; } = new List<Guid>();

        public HashSet<Guid> Refuses { get; } = new HashSet<Guid>();

        public HashSet<Guid> NotThere { get; } = new HashSet<Guid>();
    }

    private sealed class RecordingLog : ILogger
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = new List<(LogLevel, string)>();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => new Nothing();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Lines.Add((logLevel, formatter(state, exception)));
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
