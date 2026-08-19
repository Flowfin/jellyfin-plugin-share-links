using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Where the refusal of a caller the server has not signed in happens, relative
/// to the first thing the guest route reads (#53).
/// </summary>
/// <remarks>
/// <para>
/// Every other test of this route calls the action method, which begins after the
/// authorize attribute has already been honoured or ignored, so none of them can
/// say whether an unauthenticated request reaches the store. This one puts the
/// framework's own <see cref="AuthorizationMiddleware"/> in front of the action,
/// the same type <c>UseAuthorization</c> inserts, and drives a request through it.
/// </para>
/// <para>
/// The endpoint the middleware judges is built out of the attributes the
/// controller and the action actually carry, read by reflection rather than
/// written out here. That is what makes this a test of the plugin instead of a
/// test of the framework: deleting the authorize attribute from the controller,
/// or granting the action anonymous access, changes the metadata this builds and
/// the request arrives at the store.
/// </para>
/// <para>
/// One action, because that is the whole guest surface, and
/// <c>TheRouteSurfaceIsThisOneActionAndItRequiresAnIdentifiedCaller</c> in
/// <c>GuestRouteTests</c> is what holds it to one rather than a second list here.
/// </para>
/// <para>
/// What this does NOT reach, and the list is the point of the paragraph. It does
/// not read the server's own authorization: whether Jellyfin registers this
/// middleware at all, what its default policy requires, and what its
/// authentication schemes do are three facts about another assembly, and the
/// policy and the scheme below are this test's own. It says nothing about the
/// administrator routes, whose policy is a name the server defines and this
/// repository cannot read. And a middleware driven directly is not a server: what
/// is proven is an ordering, in front of an action carrying this plugin's
/// attributes.
/// </para>
/// </remarks>
public sealed class RouteRefusalOrderTests : IDisposable
{
    private const string TheScheme = "the-server";

    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // An instant rather than the machine's clock, which the invariant lint
    // refuses outside the one routine that supplies it. Nothing here depends on
    // what it is: this test asks where the refusal happens, not when.
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory;
    private readonly ShareKeyFile _keyFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteRefusalOrderTests"/> class.
    /// </summary>
    public RouteRefusalOrderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-refusal-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _keyFile = new ShareKeyFile(Path.Combine(_directory, PluginServiceRegistrator.KeyFileName));
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// A request the server has not signed in never reaches the action, so the
    /// store is not read and the token in the link is never looked at.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnUnauthenticatedRequestIsRefusedBeforeTheGuestRouteReadsTheStore()
    {
        var store = new CountingStore();

        var arrival = await AskTheGuestRoute(store, signedIn: false).ConfigureAwait(true);

        Assert.False(arrival.ReachedTheAction);
        Assert.Equal(0, store.Reads);
        Assert.Equal(StatusCodes.Status401Unauthorized, arrival.Status);
    }

    /// <summary>
    /// The same endpoint and the same middleware, with a caller the server has
    /// signed in. The action runs and reads the store, which is what makes the
    /// zero above a refusal rather than a fixture that calls nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    /// <remarks>
    /// What the action then answers is <c>GuestRouteTests</c>' subject. This asks
    /// only whether it ran and whether it read.
    /// </remarks>
    [Fact]
    public async Task ARequestTheServerHasSignedInReachesTheActionAndItsStoreRead()
    {
        var store = new CountingStore();

        var arrival = await AskTheGuestRoute(store, signedIn: true).ConfigureAwait(true);

        Assert.True(arrival.ReachedTheAction);
        Assert.Equal(1, store.Reads);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, arrival.Status);
    }

    // The endpoint as the server would build one: the attributes the controller
    // carries, then the attributes the action carries. Nothing is written out, so
    // an attribute removed from the source is an attribute missing from here.
    private static Endpoint TheGuestRouteAsItIsDeclared()
    {
        var action = typeof(ShareLinksGuestController).GetMethod(nameof(ShareLinksGuestController.Open))
            ?? throw new InvalidOperationException("the guest action is not where this test looks for it");

        var metadata = typeof(ShareLinksGuestController).GetCustomAttributes(inherit: true)
            .Concat(action.GetCustomAttributes(inherit: true))
            .ToArray();

        return new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "ShareLinks/Guest/{token}");
    }

    private static IServiceProvider TheServicesAuthorizationNeeds(bool signedIn)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();

        var authentication = services.AddAuthentication(TheScheme);
        if (signedIn)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, SignsTheCallerIn>(TheScheme, configureOptions: null);
        }
        else
        {
            authentication.AddScheme<AuthenticationSchemeOptions, SignsNobodyIn>(TheScheme, configureOptions: null);
        }

        return services.BuildServiceProvider();
    }

    private static IAuthorizationContext AnAuthorizationContextSaying(bool signedIn)
    {
        var answer = new AuthorizationInfo
        {
            IsAuthenticated = signedIn,
            User = signedIn ? new User("guest", "provider", "reset") { Id = Caller } : null,
        };

        var context = new Mock<IAuthorizationContext>();
        context.Setup(c => c.GetAuthorizationInfo(It.IsAny<HttpRequest>())).ReturnsAsync(answer);

        return context.Object;
    }

    // The plugin's own status is not this test's subject, and a manager listing
    // nothing is the cheapest answer the route reads without throwing.
    private static IPluginManager AManagerListingNothing()
    {
        var manager = new Mock<IPluginManager>();
        manager.SetupGet(m => m.Plugins).Returns(Array.Empty<LocalPlugin>());

        return manager.Object;
    }

    private async Task<Arrival> AskTheGuestRoute(IShareStore store, bool signedIn)
    {
        var services = TheServicesAuthorizationNeeds(signedIn);
        var http = new DefaultHttpContext { RequestServices = services };
        http.SetEndpoint(TheGuestRouteAsItIsDeclared());

        var arrival = new Arrival();

        // The two middlewares in the order a server runs them: the first says who
        // the caller is, the second decides whether that caller may be here. Both
        // are the framework's own types rather than stand-ins, because an ordering
        // proven against a stand-in is a property of the stand-in.
        var authorization = new AuthorizationMiddleware(
            async _ =>
            {
                arrival.ReachedTheAction = true;
                await TheAction(store, signedIn).ConfigureAwait(false);
            },
            services.GetRequiredService<IAuthorizationPolicyProvider>());

        var authentication = new AuthenticationMiddleware(
            context => authorization.Invoke(context),
            services.GetRequiredService<IAuthenticationSchemeProvider>());

        await authentication.Invoke(http).ConfigureAwait(true);

        arrival.Status = http.Response.StatusCode;
        return arrival;
    }

    private async Task<ActionResult> TheAction(IShareStore store, bool signedIn)
    {
        var controller = new ShareLinksGuestController(
            store,
            _keyFile,
            AnAuthorizationContextSaying(signedIn),
            AManagerListingNothing(),
            new FixedClock(Now),
            NullLogger<ShareLinksGuestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Open("a-token-that-names-nothing", CancellationToken.None).ConfigureAwait(false);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }

    private sealed class Arrival
    {
        public bool ReachedTheAction { get; set; }

        public int Status { get; set; }
    }

    // Counting rather than throwing. A store that threw would make the failure an
    // exception out of the middleware, and what this test wants to report is how
    // many times the store was read before the refusal.
    private sealed class CountingStore : IShareStore
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<ShareRecord>> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<ShareRecord>>(Array.Empty<ShareRecord>());
        }

        public Task<IReadOnlyList<ShareRecord>> MutateAsync(
            Func<IReadOnlyList<ShareRecord>, IReadOnlyList<ShareRecord>> change,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the guest route does not write");
    }

    private sealed class SignsNobodyIn : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public SignsNobodyIn(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class SignsTheCallerIn : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public SignsTheCallerIn(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, Caller.ToString()) },
                TheScheme);

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
