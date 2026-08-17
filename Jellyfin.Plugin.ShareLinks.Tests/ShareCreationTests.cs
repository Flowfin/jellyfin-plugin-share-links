using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ShareLinks.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Creating a share, and the accounts it is for (#67).
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of test, kept apart on purpose. The rules that can be decided from a
/// request and a configuration are driven against <see cref="ShareCreation"/>,
/// which takes no server and no store, so a refusal is asserted where it is
/// decided. The rules that need the server to answer something are driven through
/// the action, against a server that is a few dictionaries: what accounts exist,
/// what policy each one carries, what it was given to sign in with, and which of
/// them were taken away again.
/// </para>
/// <para>
/// That fake is what lets the clause this issue's own hazard is about be
/// asserted at all. A create that gets as far as making accounts and is then
/// refused by the store has to leave the server as it found it, and the only way
/// to see that is to be able to ask the server afterwards.
/// </para>
/// <para>
/// Nothing here reaches a real server, a network or a media file, which is
/// <c>docs/testing.md</c>. What it therefore does not reach is whether this
/// server's own account creation behaves as its interface documents: the policy
/// this plugin asks for is asserted here, and whether the server honours a policy
/// is the server's, which <c>GuestPolicy</c> says about itself.
/// </para>
/// <para>
/// The settings come from <see cref="IPluginConfigurationSource"/> rather than
/// from <see cref="Plugin.Instance"/>, and that seam exists because of these
/// tests. Reading the static made them pass inside the whole suite and fail on
/// their own, because whether a plugin with a configuration on it happened to
/// exist was decided by whichever other class had run last. What is handed over
/// below is this fixture's own configuration, so the two runs mean the same
/// thing.
/// </para>
/// </remarks>
public sealed class ShareCreationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Item = new Guid("55555555-5555-5555-5555-555555555555");

    private readonly string _directory;
    private readonly ShareKeyFile _keyFile;
    private readonly byte[] _key;
    private readonly AServer _server = new AServer();

    public ShareCreationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _keyFile = new ShareKeyFile(Path.Combine(_directory, PluginServiceRegistrator.KeyFileName));
        _key = _keyFile.Read();
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

    private string StorePath => Path.Combine(_directory, PluginServiceRegistrator.StoreFileName);

    // Every field of a request that has to be refused, with the field its
    // sentence has to name. One theory rather than a test each, because what is
    // being asserted is that the whole set is refused and that each refusal says
    // which box to change; a case that stopped being refused would otherwise be a
    // deleted test rather than a red one.
    public static TheoryData<string, ShareCreationRequest> RefusedRequests() => new TheoryData<string, ShareCreationRequest>
    {
        { "ItemId", new ShareCreationRequest { ItemId = Guid.Empty, GuestNames = new[] { "Ada" } } },
        { "GuestNames", new ShareCreationRequest { ItemId = Item, GuestNames = null } },
        { "GuestNames", new ShareCreationRequest { ItemId = Item, GuestNames = Array.Empty<string>() } },
        { "GuestNames", new ShareCreationRequest { ItemId = Item, GuestNames = new[] { "Ada", "   " } } },
        { "GuestNames", new ShareCreationRequest { ItemId = Item, GuestNames = new[] { "Ada", "ada" } } },
        { "GuestNames", new ShareCreationRequest { ItemId = Item, GuestNames = Enumerable.Range(0, ShareCreation.MaximumGuestsPerShare + 1).Select(index => "guest-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray() } },
        { "ExpiresAt", new ShareCreationRequest { ItemId = Item, GuestNames = new[] { "Ada" }, ExpiresAt = Now } },
        { "ExpiresAt", new ShareCreationRequest { ItemId = Item, GuestNames = new[] { "Ada" }, ExpiresAt = Now.AddSeconds(-1) } },
        { "MaxBitrateMbps", new ShareCreationRequest { ItemId = Item, GuestNames = new[] { "Ada" }, MaxBitrateMbps = BitrateCap.MinimumMegabitsPerSecond / 2 } },
        { "MaxBitrateMbps", new ShareCreationRequest { ItemId = Item, GuestNames = new[] { "Ada" }, MaxBitrateMbps = BitrateCap.MaximumMegabitsPerSecond + 1 } },
        { "MaxBitrateMbps", new ShareCreationRequest { ItemId = Item, GuestNames = new[] { "Ada" }, MaxBitrateMbps = double.NaN } },
    };

    /// <summary>
    /// Every input rule this route validates server-side, refused at the routine
    /// that owns it, with the sentence naming the field the operator has to
    /// change. This is #67's fourth clause for the rules that need nothing but
    /// the request.
    /// </summary>
    /// <param name="field">The field the refusal has to name.</param>
    /// <param name="request">The request that must be refused.</param>
    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public void EveryRuleAboutTheRequestItselfIsRefusedAndTheSentenceNamesTheField(string field, ShareCreationRequest request)
    {
        var refusal = ShareCreation.Refuse(request, Now);

        Assert.NotNull(refusal);
        Assert.StartsWith(field, refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The expiry boundary, from the create side. <c>docs/expiry.md</c> makes a
    /// share live strictly before its instant, so an expiry of exactly now is a
    /// share that is already refused and one tick later is not.
    /// </summary>
    [Fact]
    public void AnExpiryOfExactlyNowIsRefusedAndOneTickLaterIsNot()
    {
        Assert.NotNull(ShareCreation.Refuse(ARequest(expiresAt: Now), Now));
        Assert.Null(ShareCreation.Refuse(ARequest(expiresAt: Now.AddTicks(1)), Now));
    }

    /// <summary>
    /// An operator who says nothing about expiry gets the configured default
    /// lifetime rather than a share that never stops.
    /// </summary>
    [Fact]
    public void AnAbsentExpiryTakesTheConfiguredDefaultLifetime()
    {
        var configuration = new PluginConfiguration { DefaultShareLifetimeDays = 3 };

        Assert.Equal(
            Now.AddDays(3),
            ShareCreation.ExpiryOf(configuration, ARequest(), Now));
    }

    /// <summary>
    /// An instant an operator supplied in their own zone is converted rather than
    /// reinterpreted, which is what makes the stored value mean the same thing
    /// after the server's offset moves.
    /// </summary>
    [Fact]
    public void AnExpiryInAnotherOffsetIsConvertedRatherThanReinterpreted()
    {
        var asked = new DateTimeOffset(2026, 6, 8, 14, 0, 0, TimeSpan.FromHours(2));

        var stored = ShareCreation.ExpiryOf(new PluginConfiguration(), ARequest(expiresAt: asked), Now);

        Assert.Equal(TimeSpan.Zero, stored.Offset);
        Assert.Equal(asked.UtcDateTime, stored.UtcDateTime);
    }

    /// <summary>
    /// Every account a create invites is one this plugin made, so the record
    /// claims all of them. Nothing a later removal reads can then take an account
    /// somebody else made for one of these (#144).
    /// </summary>
    [Fact]
    public void EveryInvitedAccountOnACreatedRecordIsClaimedAsOneThisPluginMade()
    {
        var guests = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var record = ShareCreation.Record(new PluginConfiguration(), ARequest(), Guid.NewGuid(), Operator, guests, "a-hash", Now);

        Assert.Equal(guests, record.InvitedUserIds);
        Assert.Equal(guests, record.PluginCreatedUserIds);
        Assert.All(guests, guest => Assert.True(record.WasCreatedByThisPlugin(guest)));
    }

    /// <summary>
    /// The whole of a create that works: an account is made, narrowed to the
    /// guest policy and given something to sign in with, the record is written,
    /// and the answer carries the link and the credential. This is #67's first
    /// clause and the half of its third that says the link appears in the create
    /// response.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACreateMakesTheAccountNarrowsItWritesTheRecordAndAnswersWithTheLinkOnce()
    {
        using var store = new ShareStore(StorePath);

        var created = Assert.IsType<ShareCreated>(
            Assert.IsType<OkObjectResult>((await Controller(store).Create(ARequest(), CancellationToken.None)).Result).Value);

        var guest = Assert.Single(created.Guests);
        var account = Assert.Single(_server.Users);
        Assert.Equal("Ada", guest.Name);
        Assert.Equal(account.Id, guest.UserId);

        // Narrowed, and by the one routine that decides every switch. The two
        // asserted here are the two that would be a real widening if the call
        // were forgotten: an administrator, and an account visible on the sign-in
        // list of a server whose owner never advertised who they invited.
        var policy = _server.Policies[account.Id];
        Assert.False(policy.IsAdministrator);
        Assert.True(policy.IsHidden);
        Assert.Equal(GuestPolicy.Create(GuestPolicy.DefaultMaxActiveSessions).EnableContentDownloading, policy.EnableContentDownloading);

        // Something to sign in with, drawn by the routine that draws token bytes
        // rather than by a second source of secret material in this plugin.
        Assert.Equal(_server.Credentials[account.Id], guest.Credential);
        Assert.Equal(ShareTokens.EncodedLength, guest.Credential.Length);
        Assert.All(guest.Credential, character => Assert.True(ShareTokens.Alphabet.Contains(character, StringComparison.Ordinal)));

        // And the record, with the accounts on it.
        var record = Assert.Single(await store.ReadAsync());
        Assert.Equal(created.Share.Id, record.Id);
        Assert.Equal(new[] { account.Id }, record.InvitedUserIds);
        Assert.Equal(new[] { account.Id }, record.PluginCreatedUserIds);
        Assert.Equal(Operator, record.CreatedByUserId);
    }

    /// <summary>
    /// The rule `docs/account-restoration.md` rests on, asserted rather than
    /// described: the policy goes onto the accounts this call made and onto no
    /// other account on the server. That page decides that nothing records what
    /// an account used to be, and the whole of its argument is that every account
    /// written to is one that did not exist a moment earlier.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ThePolicyIsWrittenOntoTheAccountsTheCreateMadeAndOntoNobodyElse()
    {
        using var store = new ShareStore(StorePath);
        var somebodyElse = _server.Add("The operator's own account");

        await Created(store);

        var made = _server.Users.Single(user => user.Id != somebodyElse.Id);
        Assert.Equal(new[] { made.Id }, _server.Policies.Keys.ToArray());
        Assert.Equal(new[] { made.Id }, _server.Credentials.Keys.ToArray());
        Assert.Empty(_server.Deleted);
    }

    /// <summary>
    /// The link in the answer opens this share and nothing else: it points at the
    /// guest route, and the token it carries is the one the record's keyed hash
    /// answers for. A link built from a second mint would pass every other
    /// assertion here and open nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheTokenInTheLinkIsTheOneTheRecordAnswersFor()
    {
        using var store = new ShareStore(StorePath);

        var created = await Created(store);
        var record = Assert.Single(await store.ReadAsync());

        Assert.True(created.Link.IsAbsoluteUri);
        Assert.StartsWith("/ShareLinks/Guest/", created.Link.AbsolutePath, StringComparison.Ordinal);

        var token = created.Link.AbsolutePath["/ShareLinks/Guest/".Length..];
        Assert.True(ShareTokenHash.Matches(_key, token, record.TokenHash));
    }

    /// <summary>
    /// The link and the credential are in the create answer and in nothing the
    /// listing route hands back, in any field and anywhere in the bytes. This is
    /// the other half of #67's third clause.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task NeitherTheLinkNorTheCredentialIsInTheListingAfterwards()
    {
        using var store = new ShareStore(StorePath);

        var created = await Created(store);
        var credential = Assert.Single(created.Guests).Credential;

        var listing = Assert.IsAssignableFrom<IReadOnlyList<ShareSummary>>(
            Assert.IsType<OkObjectResult>((await Controller(store).List(CancellationToken.None)).Result).Value);

        var written = JsonSerializer.Serialize(listing);
        Assert.DoesNotContain(credential, written, StringComparison.Ordinal);
        Assert.DoesNotContain(created.Link.AbsolutePath["/ShareLinks/Guest/".Length..], written, StringComparison.Ordinal);
    }

    /// <summary>
    /// An item this server does not hold is refused, and nothing is made for it.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnItemThisServerDoesNotHoldIsRefusedAndNothingIsMade()
    {
        using var store = new ShareStore(StorePath);
        _server.Items.Clear();

        var answer = await Controller(store).Create(ARequest(), CancellationToken.None);

        Assert.StartsWith("ItemId", TheRefusal(answer), StringComparison.Ordinal);
        await NothingWasMade(store);
    }

    /// <summary>
    /// A name this server already has is refused back to the operator rather than
    /// made unique with a number, which is <c>docs/guest-accounts.md</c>'s
    /// decision, and nothing is made for it.
    /// </summary>
    /// <remarks>
    /// The taken name is the second of two, which is what makes this about the
    /// names being asked about before any of them is made rather than about the
    /// server refusing one. Without that question the first name is created, the
    /// second is refused by the server, and the first is deleted again: the same
    /// answer, reached by making and destroying a real account on somebody's
    /// server for an ordinary typing mistake.
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ANameThisServerAlreadyHasIsRefusedBeforeAnyOfThemIsMade()
    {
        using var store = new ShareStore(StorePath);
        _server.Add("Grace");

        var answer = await Controller(store).Create(ARequest(guests: new[] { "Ada", "Grace" }), CancellationToken.None);

        Assert.StartsWith("GuestNames", TheRefusal(answer), StringComparison.Ordinal);
        Assert.Single(_server.Users);
        Assert.Empty(_server.Deleted);
        Assert.Empty(await store.ReadAsync());
    }

    /// <summary>
    /// A lifetime past the configured ceiling is refused, and it is refused
    /// before an account is made. This is #45's third clause, which asks that the
    /// create route refuse a lifetime past the ceiling, and the ceiling is the
    /// same routine the store enforces rather than a copy of the number here.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ALifetimePastTheCeilingIsRefusedBeforeAnAccountIsMade()
    {
        using var store = new ShareStore(StorePath);
        var past = Now.AddDays(ShareBounds.DefaultMaxShareLifetimeDays + 1);

        var answer = await Controller(store).Create(ARequest(expiresAt: past), CancellationToken.None);

        Assert.Contains("MaxShareLifetimeDays", TheRefusal(answer), StringComparison.Ordinal);
        await NothingWasMade(store);
    }

    /// <summary>
    /// A ceiling outside the bounds is refused by the route as well as by the
    /// routine, so an operator meets it where they typed it.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACeilingOutsideItsBoundsIsRefusedByTheRouteAndNothingIsMade()
    {
        using var store = new ShareStore(StorePath);

        var answer = await Controller(store).Create(
            ARequest(maxBitrateMbps: BitrateCap.MaximumMegabitsPerSecond + 1),
            CancellationToken.None);

        Assert.StartsWith("MaxBitrateMbps", TheRefusal(answer), StringComparison.Ordinal);
        await NothingWasMade(store);
    }

    /// <summary>
    /// A caller the server has not identified creates nothing. The elevation
    /// policy has already refused one in front of the action, so this cannot
    /// happen on a server; what it stops is the empty identifier being written
    /// into the field that says who made the share.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACallerTheServerHasNotIdentifiedCreatesNothing()
    {
        using var store = new ShareStore(StorePath);

        var answer = await Controller(store, caller: null).Create(ARequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(answer.Result).StatusCode);
        await NothingWasMade(store);
    }

    /// <summary>
    /// A create with no body at all is refused rather than faulted.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACreateWithNoBodyIsRefused()
    {
        using var store = new ShareStore(StorePath);

        var answer = await Controller(store).Create(request: null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(answer.Result);
        await NothingWasMade(store);
    }

    /// <summary>
    /// A route asked to create before the server has said what the settings are
    /// is a fault rather than a create under numbers this plugin chose. The
    /// defaults are a set of values an operator has never seen, and a share made
    /// against them would carry a lifetime and a ceiling nobody agreed to.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACreateBeforeTheServerHasSaidWhatTheSettingsAreIsAFaultRatherThanADefault()
    {
        using var store = new ShareStore(StorePath);
        var nothingSaved = new Mock<IPluginConfigurationSource>();
        nothingSaved.Setup(source => source.Current()).Returns((PluginConfiguration?)null);

        var answer = await Controller(store, Operator, nothingSaved.Object).Create(ARequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(answer.Result).StatusCode);
        await NothingWasMade(store);
    }

    /// <summary>
    /// A configuration outside its own bounds is a fault too, and the sentence
    /// names the setting. It arrives from a file edited by hand, because the save
    /// path refuses one (#71), and an administrator meeting a bare 500 has
    /// nothing to act on.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AConfigurationOutsideItsOwnBoundsIsAFaultThatNamesTheSetting()
    {
        using var store = new ShareStore(StorePath);
        var editedByHand = new Mock<IPluginConfigurationSource>();
        editedByHand.Setup(source => source.Current())
            .Returns(new PluginConfiguration { GuestMaxActiveSessions = 0 });

        var answer = await Controller(store, Operator, editedByHand.Object).Create(ARequest(), CancellationToken.None);

        var fault = Assert.IsType<ObjectResult>(answer.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, fault.StatusCode);
        Assert.Contains(
            nameof(PluginConfiguration.GuestMaxActiveSessions),
            Assert.IsType<string>(fault.Value),
            StringComparison.Ordinal);
        await NothingWasMade(store);
    }

    /// <summary>
    /// The hazard this issue names. The authoritative ceiling check is inside the
    /// store mutation, so a create that loses that race to a second administrator
    /// has already made its accounts. They go back, because no record names them
    /// and nothing later would ever find them.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACreateRefusedInsideTheMutationTakesBackTheAccountsItHadAlreadyMade()
    {
        // Empty when the route reads it and at its ceiling when the mutation runs,
        // which is what a second administrator creating in between looks like from
        // here.
        var store = new AStoreThatFillsUpBetweenTheReadAndTheWrite(Now);

        var answer = await Controller(store).Create(ARequest(), CancellationToken.None);

        Assert.Contains("MaxLiveShares", TheRefusal(answer), StringComparison.Ordinal);
        Assert.Equal(new[] { _server.Users[0].Id }, _server.Deleted);
        Assert.Empty(_server.Remaining);
    }

    /// <summary>
    /// The same way back when the store cannot be written at all. The caller is
    /// told the create failed, which is true, and the server is left as it was
    /// found.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACreateAgainstAStoreThatCannotBeWrittenTakesBackItsAccountsToo()
    {
        var store = new AStoreThatCannotBeWritten();

        var answer = await Controller(store).Create(ARequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(answer.Result).StatusCode);
        Assert.Equal(new[] { _server.Users[0].Id }, _server.Deleted);
        Assert.Empty(_server.Remaining);
    }

    /// <summary>
    /// A share for more than one guest makes one account each, and the
    /// credentials come back in the order the operator named them, so a person
    /// reading the answer can tell which one belongs to whom.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareForSeveralGuestsMakesOneAccountEachAndPairsTheCredentialsWithTheNames()
    {
        using var store = new ShareStore(StorePath);

        var created = Assert.IsType<ShareCreated>(
            Assert.IsType<OkObjectResult>(
                (await Controller(store).Create(ARequest(guests: new[] { "Ada", "Grace" }), CancellationToken.None)).Result).Value);

        Assert.Equal(new[] { "Ada", "Grace" }, created.Guests.Select(guest => guest.Name).ToArray());
        Assert.Equal(2, created.Guests.Select(guest => guest.Credential).Distinct(StringComparer.Ordinal).Count());

        var record = Assert.Single(await store.ReadAsync());
        Assert.Equal(created.Guests.Select(guest => guest.UserId).ToArray(), record.InvitedUserIds);
    }

    // The request every test starts from: one item this server holds, one guest,
    // and nothing said about expiry or the ceiling, which is the ordinary case.
    private static ShareCreationRequest ARequest(
        IReadOnlyList<string>? guests = null,
        DateTimeOffset? expiresAt = null,
        double? maxBitrateMbps = null) => new ShareCreationRequest
        {
            ItemId = Item,
            GuestNames = guests ?? new[] { "Ada" },
            ExpiresAt = expiresAt,
            MaxBitrateMbps = maxBitrateMbps,
        };

    private static string TheRefusal<T>(ActionResult<T> answer)
        => Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(answer.Result).Value);

    // The settings these tests run under: the defaults, and nothing an operator
    // changed. Every assertion here is written against them, so a ceiling met in
    // a test is the ceiling this plugin ships with.
    private static IPluginConfigurationSource TheDefaults()
    {
        var source = new Mock<IPluginConfigurationSource>();
        source.Setup(s => s.Current()).Returns(new PluginConfiguration());

        return source.Object;
    }

    private async Task<ShareCreated> Created(IShareStore store)
        => Assert.IsType<ShareCreated>(
            Assert.IsType<OkObjectResult>((await Controller(store).Create(ARequest(), CancellationToken.None)).Result).Value);

    private async Task NothingWasMade(IShareStore store)
    {
        Assert.Empty(_server.Remaining);
        Assert.Empty(_server.Deleted);
        Assert.Empty(await store.ReadAsync());
    }

    private ShareLinksAdminController Controller(IShareStore store) => Controller(store, Operator);

    private ShareLinksAdminController Controller(IShareStore store, Guid? caller)
        => Controller(store, caller, TheDefaults());

    private ShareLinksAdminController Controller(IShareStore store, Guid? caller, IPluginConfigurationSource configuration)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("media.example.org");

        return new ShareLinksAdminController(
            store,
            _keyFile,
            _server.Manager(),
            _server.Library(),
            configuration,
            ContextFor(caller),
            new FixedClock(Now),
            NullLogger<ShareLinksAdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static IAuthorizationContext ContextFor(Guid? caller)
    {
        var authorization = new AuthorizationInfo
        {
            IsAuthenticated = caller is not null,
            User = caller is { } identified
                ? new User("an administrator", "provider", "reset") { Id = identified }
                : null,
        };

        var context = new Mock<IAuthorizationContext>();
        context.Setup(c => c.GetAuthorizationInfo(It.IsAny<HttpRequest>()))
            .ReturnsAsync(authorization);

        return context.Object;
    }

    // A server, as far as this route can see one: the accounts that exist, what
    // each one may do, what it was given to sign in with, the items it holds, and
    // what has been taken away again. Everything asserted about the server after
    // a create is read off this.
    private sealed class AServer
    {
        public List<User> Users { get; } = new List<User>();

        public List<Guid> Deleted { get; } = new List<Guid>();

        public Dictionary<Guid, UserPolicy> Policies { get; } = new Dictionary<Guid, UserPolicy>();

        public Dictionary<Guid, string> Credentials { get; } = new Dictionary<Guid, string>();

        public List<Guid> Items { get; } = new List<Guid> { Item };

        // The accounts that are still there, which is what "the server was left as
        // it was found" is asserted against.
        public IReadOnlyList<User> Remaining
            => Users.Where(user => !Deleted.Contains(user.Id)).ToList();

        public User Add(string name)
        {
            var user = new User(name, "provider", "reset") { Id = Guid.NewGuid() };
            Users.Add(user);
            return user;
        }

        public IUserManager Manager()
        {
            var manager = new Mock<IUserManager>(MockBehavior.Strict);

            manager.Setup(m => m.GetUserByName(It.IsAny<string>()))
                .Returns((string name) => Remaining.FirstOrDefault(user => string.Equals(user.Username, name, StringComparison.OrdinalIgnoreCase)));

            manager.Setup(m => m.CreateUserAsync(It.IsAny<string>()))
                .Returns((string name) => string.IsNullOrEmpty(name)
                    ? throw new ArgumentNullException(nameof(name))
                    : Remaining.Any(user => string.Equals(user.Username, name, StringComparison.OrdinalIgnoreCase))
                        ? throw new ArgumentException("the name " + name + " already exists", nameof(name))
                        : Task.FromResult(Add(name)));

            manager.Setup(m => m.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
                .Returns((Guid id, UserPolicy policy) =>
                {
                    Policies[id] = policy;
                    return Task.CompletedTask;
                });

            manager.Setup(m => m.ChangePassword(It.IsAny<Guid>(), It.IsAny<string>()))
                .Returns((Guid id, string credential) =>
                {
                    Credentials[id] = credential;
                    return Task.CompletedTask;
                });

            manager.Setup(m => m.DeleteUserAsync(It.IsAny<Guid>()))
                .Returns((Guid id) =>
                {
                    Deleted.Add(id);
                    return Task.CompletedTask;
                });

            return manager.Object;
        }

        public ILibraryManager Library()
        {
            var library = new Mock<ILibraryManager>(MockBehavior.Strict);

            // The item is answered for by identity alone. What a real one is, is
            // the server's, and nothing this route does with it reaches past
            // whether it is there.
            library.Setup(m => m.GetItemById(It.IsAny<Guid>()))
                .Returns((Guid id) => Items.Contains(id) ? new Folder { Id = id } : null);

            return library.Object;
        }
    }

    // A store that is empty when it is read and full when it is written, which is
    // what a second administrator creating in the same moment looks like from
    // inside one call.
    private sealed class AStoreThatFillsUpBetweenTheReadAndTheWrite : IShareStore
    {
        private readonly DateTimeOffset _now;

        public AStoreThatFillsUpBetweenTheReadAndTheWrite(DateTimeOffset now) => _now = now;

        public Task<IReadOnlyList<ShareRecord>> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ShareRecord>>(Array.Empty<ShareRecord>());

        public Task<IReadOnlyList<ShareRecord>> MutateAsync(
            Func<IReadOnlyList<ShareRecord>, IReadOnlyList<ShareRecord>> change,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(change);

            var full = new List<ShareRecord>(ShareBounds.DefaultMaxLiveShares);
            for (var index = 0; index < ShareBounds.DefaultMaxLiveShares; index++)
            {
                full.Add(new ShareRecord
                {
                    SchemaVersion = ShareRecord.CurrentSchemaVersion,
                    Id = Guid.NewGuid(),
                    ItemId = Guid.NewGuid(),
                    InvitedUserIds = Array.Empty<Guid>(),
                    CreatedByUserId = Operator,
                    CreatedAt = _now.AddDays(-1),
                    ExpiresAt = _now.AddDays(1),
                    TokenHash = "not-a-token",
                });
            }

            return Task.FromResult<IReadOnlyList<ShareRecord>>(change(full));
        }
    }

    private sealed class AStoreThatCannotBeWritten : IShareStore
    {
        public Task<IReadOnlyList<ShareRecord>> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ShareRecord>>(Array.Empty<ShareRecord>());

        public Task<IReadOnlyList<ShareRecord>> MutateAsync(
            Func<IReadOnlyList<ShareRecord>, IReadOnlyList<ShareRecord>> change,
            CancellationToken cancellationToken = default)
            => throw new ShareStoreUnwritableException("a path", "the disk said no");
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
