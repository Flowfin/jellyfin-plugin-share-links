using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Which accounts the end of a share leaves with nothing naming them, and what is
/// written onto them (#58).
/// </summary>
/// <remarks>
/// <para>
/// The routine is driven directly here and through the revocation route in
/// <c>AdministratorRouteTests</c>. The two are not the same assertion: this one
/// judges the arithmetic over a list of records and the shape of the policy that
/// is written, and that one judges that the route reaches the routine at all and
/// hands it the store as it stands after the write.
/// </para>
/// <para>
/// Nothing here reaches a server. Whether a server honours <c>IsDisabled</c> is
/// asserted by nothing in this repository, and <c>docs/testing.md</c> is where
/// that rule is written.
/// </para>
/// </remarks>
public sealed class GuestAccountsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Item = new Guid("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// A record that has ended still claims the accounts it created, and that is
    /// where the candidates come from. Reading the claims off live records alone
    /// would find nothing at all, because the record holding the claim is the one
    /// that ended.
    /// </summary>
    [Fact]
    public void TheAccountOfARecordThatHasEndedIsFound()
    {
        var guest = Guid.NewGuid();
        var ended = ARecord(revokedAt: Now, invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Equal(new[] { guest }, GuestAccounts.WithNoLiveShareLeft(new[] { ended }, Now));
    }

    /// <summary>
    /// An account another live share still invites is left alone, whichever record
    /// created it. An account named by two shares stays live while either does.
    /// </summary>
    [Fact]
    public void AnAccountAnotherLiveShareInvitesIsLeftAlone()
    {
        var guest = Guid.NewGuid();
        var ended = ARecord(revokedAt: Now, invited: new[] { guest }, pluginCreated: new[] { guest });
        var live = ARecord(invited: new[] { guest });

        Assert.Empty(GuestAccounts.WithNoLiveShareLeft(new[] { ended, live }, Now));
    }

    /// <summary>
    /// A record that has reached its expiry instant is not a live one. A reading
    /// that counted records naming the account rather than live ones would leave
    /// every guest enabled for as long as their record is kept.
    /// </summary>
    [Fact]
    public void AnotherShareHavingExpiredDoesNotKeepAnAccountEnabled()
    {
        var guest = Guid.NewGuid();
        var ended = ARecord(revokedAt: Now, invited: new[] { guest }, pluginCreated: new[] { guest });
        var expired = ARecord(expiresAt: Now.AddDays(-1), invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Equal(new[] { guest }, GuestAccounts.WithNoLiveShareLeft(new[] { ended, expired }, Now));
    }

    /// <summary>
    /// An identifier among a record's created accounts that the record does not
    /// also invite is not claimed, and is not switched off. That is the shape a
    /// store carried forward or a record edited by hand produces, and it is the
    /// difference between tidying up after a share and reaching into somebody's
    /// own account.
    /// </summary>
    [Fact]
    public void AnAccountAHandEditPutAmongTheCreatedOnesIsNotClaimed()
    {
        var nominated = Guid.NewGuid();
        var edited = ARecord(
            revokedAt: Now,
            invited: Array.Empty<Guid>(),
            pluginCreated: new[] { nominated });

        Assert.Empty(GuestAccounts.WithNoLiveShareLeft(new[] { edited }, Now));
    }

    /// <summary>
    /// An account two ended records both claim is written once. A repeat costs the
    /// server a second write that changes nothing and costs a reader a list that
    /// does not match the accounts it is about.
    /// </summary>
    [Fact]
    public void AnAccountTwoEndedRecordsClaimIsFoundOnce()
    {
        var guest = Guid.NewGuid();
        var first = ARecord(revokedAt: Now, invited: new[] { guest }, pluginCreated: new[] { guest });
        var second = ARecord(revokedAt: Now, invited: new[] { guest }, pluginCreated: new[] { guest });

        Assert.Equal(new[] { guest }, GuestAccounts.WithNoLiveShareLeft(new[] { first, second }, Now));
    }

    /// <summary>
    /// The ceiling written is the one the account already carries, so a number an
    /// operator raised in the configuration after the share was created is not
    /// written onto the account by the call that ends it. Widening an account is
    /// what this issue is named against.
    /// </summary>
    [Fact]
    public async Task TheCeilingTheAccountCarriesIsTheCeilingWrittenBack()
    {
        var guest = Guid.NewGuid();
        var server = new AServer { Carries = { [guest] = 2 } };

        await GuestAccounts.DisableAsync(server.Manager, new[] { guest });

        Assert.Equal(2, server.Policies[guest].MaxActiveSessions);
    }

    /// <summary>
    /// An account carrying the server's own zero is carrying no ceiling at all
    /// rather than the lowest one, so a number is written where there was none.
    /// The alternative reading leaves an account that is being switched off
    /// carrying no ceiling, and a ceiling of zero is refused by the routine that
    /// builds the policy anyway.
    /// </summary>
    [Fact]
    public async Task AnAccountCarryingNoCeilingAtAllIsGivenOne()
    {
        var guest = Guid.NewGuid();
        var server = new AServer { Carries = { [guest] = 0 } };

        await GuestAccounts.DisableAsync(server.Manager, new[] { guest });

        Assert.Equal(GuestPolicy.DefaultMaxActiveSessions, server.Policies[guest].MaxActiveSessions);
    }

    /// <summary>
    /// An account carrying more than an operator may set is narrowed to the bound
    /// rather than written back as it was. Narrowing is the direction this plugin
    /// is allowed to move an account in, and the routine that builds the policy
    /// refuses the number outright.
    /// </summary>
    [Fact]
    public async Task AnAccountCarryingMoreThanTheBoundIsNarrowedToIt()
    {
        var guest = Guid.NewGuid();
        var server = new AServer { Carries = { [guest] = GuestPolicy.MaximumMaxActiveSessions + 40 } };

        await GuestAccounts.DisableAsync(server.Manager, new[] { guest });

        Assert.Equal(GuestPolicy.MaximumMaxActiveSessions, server.Policies[guest].MaxActiveSessions);
    }

    /// <summary>
    /// An account the server does not answer for is written a policy all the same.
    /// It is the state a deletion somebody made by hand leaves, and the alternative
    /// is a call that quietly skips an account it was asked to switch off; what the
    /// server does with a policy for an account it does not hold is the server's.
    /// </summary>
    [Fact]
    public async Task AnAccountTheServerDoesNotAnswerForIsStillWritten()
    {
        var guest = Guid.NewGuid();
        var server = new AServer();

        await GuestAccounts.DisableAsync(server.Manager, new[] { guest });

        Assert.Equal(new[] { guest }, server.Written);
        Assert.True(server.Policies[guest].IsDisabled);
    }

    private static ShareRecord ARecord(
        DateTimeOffset? expiresAt = null,
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
            ExpiresAt = expiresAt ?? Now.AddDays(7),
            RevokedAt = revokedAt,
            RevokedByUserId = revokedAt is null ? null : Operator,
            TokenHash = "not-a-token",
        };

    // A server, as far as this routine can see one: what each account carries as a
    // session ceiling, and what was written onto it. Strict, so a routine reaching
    // for a member nobody expected fails here rather than passing quietly.
    private sealed class AServer
    {
        private readonly Mock<IUserManager> _manager = new Mock<IUserManager>(MockBehavior.Strict);

        public AServer()
        {
            _manager
                .Setup(manager => manager.GetUserById(It.IsAny<Guid>()))
                .Returns((Guid id) => Carries.TryGetValue(id, out var ceiling)
                    ? new User("a guest", "provider", "reset") { Id = id, MaxActiveSessions = ceiling }
                    : null);

            _manager
                .Setup(manager => manager.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
                .Callback((Guid id, UserPolicy policy) =>
                {
                    Written.Add(id);
                    Policies[id] = policy;
                })
                .Returns(Task.CompletedTask);
        }

        public IUserManager Manager => _manager.Object;

        public List<Guid> Written { get; } = new List<Guid>();

        public Dictionary<Guid, UserPolicy> Policies { get; } = new Dictionary<Guid, UserPolicy>();

        public Dictionary<Guid, int> Carries { get; } = new Dictionary<Guid, int>();
    }
}
