using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MediaBrowser.Model.Users;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The claims <c>docs/account-restoration.md</c> rests on (#58).
/// </summary>
/// <remarks>
/// <para>
/// The page decides that no prior state is recorded anywhere, because this plugin
/// writes a policy onto an account it created and onto no other account. That
/// decision is only safe while both halves of it hold: that the policy really
/// would widen somebody else's account, which is the reason for the rule, and
/// that nothing has quietly started writing one.
/// </para>
/// <para>
/// What none of this shows is a server doing anything. No test in this repository
/// may reach one, and <c>docs/testing.md</c> is where that rule is written.
/// </para>
/// </remarks>
public class AccountRestorationTests
{
    [Fact]
    public void ApplyingTheGuestPolicyToAnAccountThatWasNarrowedWidensIt()
    {
        // This asserts the widening rather than refusing it, and that is the
        // point rather than an oversight. The page's rule exists because writing
        // the guest policy onto an account somebody else owns takes permissions
        // that account did not have and hands them to whoever holds the link. If
        // the policy ever stops doing that, the rule is standing in front of a
        // hazard that is no longer there and the argument has to be read again.
        var prepared = new UserPolicy
        {
            EnableMediaPlayback = false,
            EnableRemoteAccess = false,
            EnableVideoPlaybackTranscoding = false
        };

        GuestPolicy.Apply(prepared, GuestPolicy.DefaultMaxActiveSessions);

        Assert.True(prepared.EnableMediaPlayback, "the guest policy left media playback where it found it, so writing it onto an operator's account is no longer a widening and docs/account-restoration.md argues from one.");
        Assert.True(prepared.EnableRemoteAccess, "the guest policy left remote access where it found it, so writing it onto an operator's account is no longer a widening and docs/account-restoration.md argues from one.");
        Assert.True(prepared.EnableVideoPlaybackTranscoding, "the guest policy left video transcoding where it found it, so writing it onto an operator's account is no longer a widening and docs/account-restoration.md argues from one.");
    }

    [Fact]
    public void ThisPluginTouchesNoUserDataYet()
    {
        // What is left of the tripwire this replaced. That one named two
        // interfaces and fired on the day the create route reached the first of
        // them, which is what it was for: somebody had to read
        // docs/account-restoration.md before an account was ever written to, and
        // the page now records that it was read and what holds the rule instead.
        //
        // This half is unchanged and is a different account surface. What a person
        // has watched, resumed and marked as a favourite is theirs, and nothing
        // here reads or writes any of it.
        //
        // Read out of the compiled plugin rather than off its public shape,
        // because such a call is inside a method body and a signature never
        // mentions it. Every type a compiled assembly names, wherever it names it,
        // is in the type reference table.
        //
        // The bound: a call made by reflection on a name assembled at run time,
        // or one made through a wrapper in another assembly, is named nowhere in
        // this table and is not seen here.
        Assert.DoesNotContain("IUserDataManager", TypeNamesReferencedByThePlugin());
    }

    [Fact]
    public void TheRuleAboutWhichAccountsAreWrittenToIsHeldByATestThatCanFail()
    {
        // The page's rule is that a policy is written onto an account this plugin
        // made and onto no other account. Which account that is, is which
        // identifier reaches a call, and no reading of the compiled metadata can
        // see a value. So the rule is held by a test that drives the route
        // against a server already holding somebody else's account, and this
        // resolves that test by name so that deleting or renaming it reds here
        // rather than leaving the page pointing at nothing.
        //
        // The name is a literal for the same reason the predicate above is one.
        Assert.NotNull(typeof(ShareCreationTests).GetMethod(
            "ThePolicyIsWrittenOntoTheAccountsTheCreateMadeAndOntoNobodyElse"));
    }

    [Fact]
    public void TheRecordStillCarriesThePredicateThePageMakesTheGate()
    {
        // The whole decision is that provenance is the gate on a policy write as
        // well as on a removal. A page naming a predicate that has been renamed
        // or dropped reads exactly like a page naming one that is there.
        //
        // The name is a literal rather than a nameof, so that dropping the
        // predicate reds this test instead of failing the build somewhere else.
        // What the page claims is that the member is there, and a claim is held
        // by something that can fail.
        var predicate = typeof(ShareRecord).GetMethod(
            "WasCreatedByThisPlugin",
            new[] { typeof(Guid) });

        Assert.True(
            predicate is not null,
            "docs/account-restoration.md makes ShareRecord.WasCreatedByThisPlugin(Guid) the gate on writing a policy, and there is no such method.");

        Assert.Equal(typeof(bool), predicate!.ReturnType);
    }

    [Fact]
    public void TheAccountPolicyStillCarriesTheSwitchThatEndsAShare()
    {
        // Disabling is what the end of the last live share does to an account,
        // which docs/guest-accounts.md decides and this page inherits, so the
        // switch it is spelled with has to exist on the line this plugin
        // compiles against.
        var disabled = typeof(UserPolicy).GetProperty("IsDisabled");

        Assert.True(
            disabled is not null,
            "docs/account-restoration.md says an account is disabled when the last live share naming it ends, and UserPolicy has no IsDisabled.");

        Assert.Equal(typeof(bool), disabled!.PropertyType);
    }

    private static IReadOnlyCollection<string> TypeNamesReferencedByThePlugin()
    {
        var location = typeof(Plugin).Assembly.Location;
        Assert.False(
            string.IsNullOrEmpty(location),
            "The plugin assembly has no file on disk, so nothing was read and this fails closed rather than reporting an absence it did not look for.");

        using var stream = File.OpenRead(location);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.TypeReferences)
        {
            names.Add(metadata.GetString(metadata.GetTypeReference(handle).Name));
        }

        Assert.NotEmpty(names);
        return names;
    }
}
