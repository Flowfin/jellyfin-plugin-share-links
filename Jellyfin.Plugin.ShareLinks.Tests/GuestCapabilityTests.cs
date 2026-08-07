using System;
using System.Linq;
using MediaBrowser.Model.Users;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The account switches <c>docs/guest-capabilities.md</c> names still exist on the
/// server this plugin compiles against (#57).
/// </summary>
/// <remarks>
/// <para>
/// This is not the test #57 asks for. That one asserts a guest is refused each
/// capability, and it needs a guest account, which needs the account creation path
/// in #51. This is the smaller thing that can be true now: the document names
/// switches by their exact names, and a name is a claim about another artefact.
/// A server line that renames or drops one turns the document into a list of
/// settings nobody sets, silently, because a policy object is assembled by
/// property and an absent property is a compile error only where it is written
/// out.
/// </para>
/// <para>
/// The names are held here as well as in the document. They move together or the
/// document is wrong; nothing refuses a name added to the document alone, and
/// that limit is stated in the document rather than hidden here.
/// </para>
/// </remarks>
public class GuestCapabilityTests
{
    /// <summary>
    /// Gets the switches the document names, with the type each one has to have.
    /// </summary>
    public static TheoryData<string, string> Switches => new()
    {
        { "EnableMediaPlayback", "Boolean" },
        { "EnableVideoPlaybackTranscoding", "Boolean" },
        { "EnableAudioPlaybackTranscoding", "Boolean" },
        { "EnablePlaybackRemuxing", "Boolean" },
        { "EnableContentDownloading", "Boolean" },
        { "EnableMediaConversion", "Boolean" },
        { "EnableSyncTranscoding", "Boolean" },
        { "EnableContentDeletion", "Boolean" },
        { "EnableRemoteControlOfOtherUsers", "Boolean" },
        { "EnableSharedDeviceControl", "Boolean" },
        { "EnableRemoteAccess", "Boolean" },
        { "EnableLiveTvAccess", "Boolean" },
        { "EnableLiveTvManagement", "Boolean" },
        { "EnableCollectionManagement", "Boolean" },
        { "EnableSubtitleManagement", "Boolean" },
        { "EnableLyricManagement", "Boolean" },
        { "EnableUserPreferenceAccess", "Boolean" },
        { "EnablePublicSharing", "Boolean" },
        { "IsAdministrator", "Boolean" },
        { "IsHidden", "Boolean" },
        { "IsDisabled", "Boolean" },
        { "MaxActiveSessions", "Int32" },
        { "RemoteClientBitrateLimit", "Int32" }
    };

    [Theory]
    [MemberData(nameof(Switches))]
    public void TheAccountPolicyStillCarriesTheSwitchTheDocumentNames(string name, string type)
    {
        var property = typeof(UserPolicy).GetProperty(name);

        Assert.True(
            property is not null,
            $"docs/guest-capabilities.md names {name} as an account policy switch and UserPolicy has no such property. "
            + "The server line moved under the document: "
            + string.Join(", ", typeof(UserPolicy).GetProperties().Select(candidate => candidate.Name).OrderBy(candidate => candidate, StringComparer.Ordinal)));

        Assert.Equal(type, property!.PropertyType.Name);
    }

    [Fact]
    public void TheNarrowestSynchronisedPlaybackValueStillExists()
    {
        // The document sets this one to its narrowest value rather than to false,
        // because it is an enum and not a switch, so the value has to exist by
        // name.
        var type = typeof(UserPolicy).GetProperty("SyncPlayAccess")!.PropertyType;

        Assert.True(type.IsEnum, "UserPolicy.SyncPlayAccess is no longer an enum, so the value the document names may not mean what it did.");
        Assert.Contains("None", Enum.GetNames(type));
    }
}
