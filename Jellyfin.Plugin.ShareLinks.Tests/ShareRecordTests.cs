using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The share record is the shape everything else in the plugin will read and
/// write (#33), and two of its properties are worth refusing rather than
/// remembering. A field that does not survive a round trip through the store is a
/// share that comes back from a restart missing its expiry or its ceiling, which
/// is a share that is wrong rather than absent, and the difference is that nobody
/// notices. A raw token reaching the serialised form turns a copy of one file
/// into every live share.
/// </summary>
/// <remarks>
/// <para>
/// The round trip is compared member by member through reflection rather than by
/// a list of assertions written out here. A hand-written list covers the members
/// that existed on the day it was written, and the failure this test exists for
/// arrives with the member somebody adds next.
/// </para>
/// <para>
/// What these cannot judge is the on-disk format. <see cref="JsonSerializer"/>
/// is what the test serialises with; which serialiser the store actually uses,
/// and what the file looks like, is #37's to fix. What is being held here is that
/// the type carries no member that cannot survive being written down, and that
/// none of them is the token.
/// </para>
/// </remarks>
public class ShareRecordTests
{
    private static readonly Guid ItemId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstGuest = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SecondGuest = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Operator = new("44444444-4444-4444-4444-444444444444");

    // Test data, not a design. What a keyed hash is made with is #43, and this
    // test needs only some text that is derived from the token and is not the
    // token, so that a serialised record which does contain the token cannot pass
    // by holding the hash instead.
    private static string StandInHashOf(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static ShareRecord Everything(string token) => new()
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = new Guid("55555555-5555-5555-5555-555555555555"),
        ItemId = ItemId,
        InvitedUserIds = [FirstGuest, SecondGuest],
        CreatedByUserId = Operator,
        CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)),
        ExpiresAt = new DateTimeOffset(2026, 1, 9, 3, 4, 5, TimeSpan.FromHours(2)),
        RevokedAt = new DateTimeOffset(2026, 1, 3, 6, 7, 8, TimeSpan.FromHours(-5)),
        RevocationReason = "the guest asked for it to be taken down",
        MaxBitrateBitsPerSecond = 4_000_000,
        TokenHash = StandInHashOf(token),
    };

    private static ShareRecord OnlyWhatIsRequired(string token) => new()
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = new Guid("66666666-6666-6666-6666-666666666666"),
        ItemId = ItemId,
        InvitedUserIds = [FirstGuest],
        CreatedByUserId = Operator,
        CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 1, 9, 3, 4, 5, TimeSpan.Zero),
        TokenHash = StandInHashOf(token),
    };

    private static PropertyInfo[] Members() =>
        typeof(ShareRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static void AssertSameMemberByMember(ShareRecord expected, ShareRecord actual)
    {
        var members = Members();

        // Without this the loop below asserts nothing on a type whose members
        // have all been made non-public, which is a shape this test would
        // otherwise call a successful round trip.
        Assert.NotEmpty(members);

        foreach (var member in members)
        {
            var before = member.GetValue(expected);
            var after = member.GetValue(actual);

            if (before is IEnumerable<Guid> sequence)
            {
                Assert.Equal(sequence, Assert.IsAssignableFrom<IEnumerable<Guid>>(after));
                continue;
            }

            Assert.Equal(before, after);
        }
    }

    [Fact]
    public void ARecordWithEveryMemberSetSurvivesTheRoundTrip()
    {
        var record = Everything(ShareTokens.Mint());

        // A round trip that compares a member nobody set proves nothing about
        // that member, so the fixture is required to have set all of them.
        foreach (var member in Members())
        {
            Assert.NotNull(member.GetValue(record));
        }

        AssertSameMemberByMember(record, JsonSerializer.Deserialize<ShareRecord>(JsonSerializer.Serialize(record))!);
    }

    [Fact]
    public void ARecordWithNothingOptionalSetSurvivesTheRoundTrip()
    {
        var record = OnlyWhatIsRequired(ShareTokens.Mint());

        // The live case. Revocation and the ceiling are absent on every share
        // that has not been revoked and names no ceiling of its own, and null
        // coming back as anything else is the same defect as a value coming back
        // changed.
        Assert.Null(record.RevokedAt);
        Assert.Null(record.RevocationReason);
        Assert.Null(record.MaxBitrateBitsPerSecond);

        AssertSameMemberByMember(record, JsonSerializer.Deserialize<ShareRecord>(JsonSerializer.Serialize(record))!);
    }

    [Fact]
    public void TheSerialisedFormCarriesNoRawToken()
    {
        var token = ShareTokens.Mint();

        foreach (var record in new[] { Everything(token), OnlyWhatIsRequired(token) })
        {
            var written = JsonSerializer.Serialize(record);

            Assert.DoesNotContain(token, written, StringComparison.Ordinal);

            // The hash is derived from the token, so a serialised form that holds
            // the hash and nothing else still has to be there for the assertion
            // above to have been about the right document.
            Assert.Contains(StandInHashOf(token), written, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoMemberIsNamedForTheTokenItself()
    {
        // The assertion above reads one serialised document. This one reads the
        // type, and catches the member that would carry a token on some other
        // path than the fixture happens to take, including one nothing populates
        // yet.
        var suspicious = Members()
            .Select(member => member.Name)
            .Where(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase))
            .Where(name => name != nameof(ShareRecord.TokenHash))
            .ToList();

        Assert.Empty(suspicious);
    }
}
