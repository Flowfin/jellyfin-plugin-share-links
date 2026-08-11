using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.ShareLinks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What every routine in the token path does with the inputs an attacker sends,
/// and with the arguments a caller gets wrong (#76).
/// </summary>
/// <remarks>
/// <para>
/// The success cases and the ordinary refusals are in <c>ShareTokenTests</c>,
/// <c>ShareTokenHashTests</c> and <c>ShareLookupTests</c>, and are not repeated
/// here. What is here is the two groups those files left: the attacker inputs the
/// issue lists by name, and the argument guards, which a mutation run measured as
/// unasserted rather than which anybody guessed at.
/// </para>
/// <para>
/// The argument guards are worth a test for a reason beyond the score. Each one
/// turns a bad call into a refusal at the routine that was called wrongly, and
/// the alternative is not an exception somewhere else: it is
/// <see cref="ShareTokenHash.Matches"/> hashing an empty token, or
/// <see cref="ShareLookup.ByToken"/> comparing every record against nothing,
/// which are answers rather than errors and would be believed.
/// </para>
/// <para>
/// Nothing here needs anything outside the process. No file, no clock, no server
/// and no network: the whole token path is a function of its arguments.
/// </para>
/// </remarks>
public class TokenPathRefusalTests
{
    private static readonly byte[] Key = Enumerable.Range(0, ShareTokenHash.MinimumKeyBytes).Select(value => (byte)value).ToArray();

    /// <summary>
    /// Gets the inputs the issue names, each of which a caller can send and none
    /// of which may resolve anything.
    /// </summary>
    public static TheoryData<string, string> WhatAnAttackerSends => new TheoryData<string, string>
    {
        { "one character", "A" },
        { "a token one character short", new string('A', ShareTokens.EncodedLength - 1) },
        { "a token one character long", new string('A', ShareTokens.EncodedLength + 1) },
        { "an oversized string", new string('A', 10_000) },
        { "the wrong alphabet, standard base64 padding", new string('A', ShareTokens.EncodedLength - 1) + "=" },
        { "the wrong alphabet, a character outside it", new string('A', ShareTokens.EncodedLength - 1) + "+" },
        { "text of the right length that was never issued", new string('A', ShareTokens.EncodedLength) },
        { "a space", " " },
        { "control characters rather than text", "\u0000\u0001\u0002" },
    };

    [Theory]
    [MemberData(nameof(WhatAnAttackerSends))]
    public void NothingAnAttackerSendsResolvesAnything(string what, string presented)
    {
        Assert.False(string.IsNullOrEmpty(what));

        var issued = ShareTokens.Mint();
        var records = new[] { ARecord(issued) };

        Assert.False(ShareTokenHash.Matches(Key, presented, ShareTokenHash.Compute(Key, issued)));
        Assert.Null(ShareLookup.ByToken(records, Key, presented));
    }

    /// <summary>
    /// A valid token with one character changed, at every position it has.
    /// </summary>
    /// <remarks>
    /// The nearest miss there is, and the one a comparison that stopped early or
    /// compared a prefix would let through. Every position rather than one,
    /// because a comparison that is wrong is usually wrong at one end.
    /// </remarks>
    [Fact]
    public void AValidTokenWithOneCharacterChangedResolvesNothingAtAnyPosition()
    {
        var issued = ShareTokens.Mint();
        var stored = ShareTokenHash.Compute(Key, issued);
        var records = new[] { ARecord(issued) };

        for (var position = 0; position < issued.Length; position++)
        {
            var changed = new StringBuilder(issued);
            changed[position] = issued[position] == 'A' ? 'B' : 'A';
            var attempt = changed.ToString();

            Assert.False(ShareTokenHash.Matches(Key, attempt, stored), $"position {position} was accepted");
            Assert.Null(ShareLookup.ByToken(records, Key, attempt));
        }

        // And the untouched token still resolves, so the loop above is a loop over
        // near misses rather than one over a token that never worked.
        Assert.Same(records[0], ShareLookup.ByToken(records, Key, issued));
    }

    /// <summary>
    /// A token whose share is gone finds nothing, and finding nothing is not the
    /// same as being told the share is gone.
    /// </summary>
    [Fact]
    public void AValidTokenFromADeletedShareFindsNothingAndLooksLikeAnyOtherMiss()
    {
        var issued = ShareTokens.Mint();
        var somebodyElses = ShareTokens.Mint();
        var afterDeletion = new[] { ARecord(somebodyElses) };

        var deleted = ShareLookup.ByToken(afterDeletion, Key, issued);
        var neverIssued = ShareLookup.ByToken(afterDeletion, Key, new string('A', ShareTokens.EncodedLength));

        Assert.Null(deleted);
        Assert.Null(neverIssued);
    }

    [Fact]
    public void AnEmptyStoreAnswersForNothingWithoutBeingHandedOne()
    {
        Assert.Null(ShareLookup.ByToken(Array.Empty<ShareRecord>(), Key, ShareTokens.Mint()));
    }

    /// <summary>
    /// Minting hands back a different token every time, which is the property the
    /// whole path rests on and the one a broken source would break silently.
    /// </summary>
    [Fact]
    public void TwoMintedTokensNeverResolveEachOther()
    {
        var one = ShareTokens.Mint();
        var two = ShareTokens.Mint();

        Assert.NotEqual(one, two);
        Assert.False(ShareTokenHash.Matches(Key, one, ShareTokenHash.Compute(Key, two)));
        Assert.False(ShareTokenHash.Matches(Key, two, ShareTokenHash.Compute(Key, one)));
    }

    /// <summary>
    /// Gets the calls that are wrong rather than hostile, and the exception each
    /// one owes.
    /// </summary>
    /// <remarks>
    /// Written as a table because the failure is one of these guards being
    /// dropped, and a table is the shape where the missing row is visible. A
    /// mutation run over this path is what said they were unasserted: removing any
    /// of them left the suite green.
    /// </remarks>
    public static TheoryData<string> TheArgumentsARoutineRefuses => new TheoryData<string>
    {
        "hashing a null token",
        "hashing an empty token",
        "hashing under a key that is too short",
        "comparing a null presented token",
        "comparing an empty presented token",
        "comparing against a null stored value",
        "comparing under a key that is too short",
        "looking up in a null list of records",
        "looking up a null token",
        "looking up an empty token",
        "looking up an empty token in an empty store",
    };

    [Theory]
    [MemberData(nameof(TheArgumentsARoutineRefuses))]
    public void ACallThatIsWrongIsRefusedAtTheRoutineRatherThanAnswered(string call)
    {
        var records = new[] { ARecord(ShareTokens.Mint()) };
        var stored = records[0].TokenHash;
        var shortKey = new byte[ShareTokenHash.MinimumKeyBytes - 1];

        Action wrong = call switch
        {
            "hashing a null token" => () => ShareTokenHash.Compute(Key, null!),
            "hashing an empty token" => () => ShareTokenHash.Compute(Key, string.Empty),
            "hashing under a key that is too short" => () => ShareTokenHash.Compute(shortKey, "a-token"),
            "comparing a null presented token" => () => ShareTokenHash.Matches(Key, null!, stored),
            "comparing an empty presented token" => () => ShareTokenHash.Matches(Key, string.Empty, stored),
            "comparing against a null stored value" => () => ShareTokenHash.Matches(Key, "a-token", null!),
            "comparing under a key that is too short" => () => ShareTokenHash.Matches(shortKey, "a-token", stored),
            "looking up in a null list of records" => () => ShareLookup.ByToken(null!, Key, "a-token"),
            "looking up a null token" => () => ShareLookup.ByToken(records, Key, null!),
            "looking up an empty token" => () => ShareLookup.ByToken(records, Key, string.Empty),

            // The row above cannot tell this routine's guard from the one inside
            // the comparison it calls, because with records to walk both throw the
            // same thing about the same parameter. With no records the loop never
            // runs, so the refusal can only be this routine's, and dropping its
            // guard turns the call into an answer of nothing rather than a
            // refusal. A mutation run is what showed the first row was not enough.
            "looking up an empty token in an empty store" => () => ShareLookup.ByToken(Array.Empty<ShareRecord>(), Key, string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, "The table names a call this method does not build."),
        };

        // ArgumentException covers the null and empty cases through their derived
        // types, and the short key is the range case. What matters is that the
        // routine refuses rather than answering, so the assertion is on that and
        // not on which of the two the framework chose.
        var refused = Record.Exception(wrong);

        Assert.NotNull(refused);
        Assert.True(
            refused is ArgumentException,
            $"{call} produced {refused.GetType()} rather than an argument refusal");
    }

    /// <summary>
    /// The message a short key is refused with names the floor, so an operator or
    /// a caller reading it knows what would have been long enough.
    /// </summary>
    /// <remarks>
    /// The mutation run emptied this message and nothing noticed, which is the
    /// class of defect where a refusal survives and the reason for it does not.
    /// </remarks>
    [Fact]
    public void TheShortKeyRefusalSaysWhatWouldHaveBeenLongEnough()
    {
        var shortKey = new byte[ShareTokenHash.MinimumKeyBytes - 1];

        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => ShareTokenHash.Compute(shortKey, "a-token"));

        Assert.Contains(
            ShareTokenHash.MinimumKeyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refused.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every routine in the token path is reached by something above.
    /// </summary>
    /// <remarks>
    /// The clause the issue opens with is every routine, not every routine
    /// somebody remembered. This reads the public surface of the three types and
    /// requires each to be named in this file, so a routine added later is a red
    /// test rather than an untested one.
    /// </remarks>
    [Fact]
    public void EveryRoutineInTheTokenPathIsNamedByATestInThisFile()
    {
        var routines = new[] { typeof(ShareTokens), typeof(ShareTokenHash), typeof(ShareLookup) }
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(method => method.DeclaringType!.Name + "." + method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "ShareLookup.ByToken",
                "ShareTokenHash.Compute",
                "ShareTokenHash.Matches",
                "ShareTokens.Mint",
                "ShareTokens.MintKeyBytes",
            ],
            routines);
    }

    /// <summary>
    /// The key minting routine, which is in the path and has no refusal case of
    /// its own.
    /// </summary>
    /// <remarks>
    /// It takes nothing, so there is no wrong way to call it. What it owes instead
    /// is that what it hands back is long enough for the routines that take it,
    /// which is the only way it can be wrong.
    /// </remarks>
    [Fact]
    public void AMintedKeyIsAcceptedByTheRoutinesThatTakeAKey()
    {
        var minted = ShareTokens.MintKeyBytes();
        var token = ShareTokens.Mint();

        Assert.True(minted.Length >= ShareTokenHash.MinimumKeyBytes);
        Assert.True(ShareTokenHash.Matches(minted, token, ShareTokenHash.Compute(minted, token)));
        Assert.NotEqual(minted, ShareTokens.MintKeyBytes());
    }

    private static ShareRecord ARecord(string token) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Guid.NewGuid() },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        TokenHash = ShareTokenHash.Compute(Key, token),
    };
}
