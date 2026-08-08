using System;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A share record names the accounts a share is for, and #144 is about the fact
/// it did not say where any of them came from. The decision in
/// <c>docs/guest-accounts.md</c> has this plugin create the guest account with
/// the invitation and remove it when the last record naming it goes, so an
/// identifier in the invited list is an identifier something will eventually
/// hand to a deletion. An identifier that got there another way is then a real
/// account belonging to a real person, deleted by this plugin, and nothing in
/// the old shape could tell the two apart.
/// </summary>
/// <remarks>
/// <para>
/// These tests are over the record's own answer rather than over a deletion.
/// Nothing in this tree deletes an account yet, and the routine those tests will
/// eventually cover is the one asserted here.
/// </para>
/// </remarks>
public class ShareRecordProvenanceTests
{
    private static readonly Guid MadeByThePlugin = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TheOperatorsOwnAccount = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid NotInvitedAtAll = new("99999999-9999-9999-9999-999999999999");

    private static ShareRecord ShareInvitingBoth() => new()
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = new Guid("55555555-5555-5555-5555-555555555555"),
        ItemId = new Guid("11111111-1111-1111-1111-111111111111"),
        InvitedUserIds = [MadeByThePlugin, TheOperatorsOwnAccount],
        PluginCreatedUserIds = [MadeByThePlugin],
        CreatedByUserId = new Guid("44444444-4444-4444-4444-444444444444"),
        CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 1, 9, 3, 4, 5, TimeSpan.Zero),
        TokenHash = "not-a-token",
    };

    [Fact]
    public void ARecordClaimsTheAccountItCreated()
    {
        // The permissive direction. Without it the two tests below pass on a
        // routine that answers no to everything, which would refuse the removal
        // of every account including the ones this plugin is supposed to clean
        // up.
        Assert.True(ShareInvitingBoth().WasCreatedByThisPlugin(MadeByThePlugin));
    }

    [Fact]
    public void ARecordDoesNotClaimAnInvitedAccountItDidNotCreate()
    {
        // The case the issue is named for. Both accounts are invited by the same
        // share and only one of them exists because this plugin made it.
        Assert.False(ShareInvitingBoth().WasCreatedByThisPlugin(TheOperatorsOwnAccount));
    }

    [Fact]
    public void ARecordDoesNotClaimAnAccountItDoesNotInvite()
    {
        // A claim standing on its own, which is what an edited file produces.
        // Reading the claim alone would let a record on disk nominate any account
        // on the server; the invitation has to be there as well.
        var edited = new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = new Guid("66666666-6666-6666-6666-666666666666"),
            ItemId = new Guid("11111111-1111-1111-1111-111111111111"),
            InvitedUserIds = [MadeByThePlugin],
            PluginCreatedUserIds = [MadeByThePlugin, NotInvitedAtAll],
            CreatedByUserId = new Guid("44444444-4444-4444-4444-444444444444"),
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 1, 9, 3, 4, 5, TimeSpan.Zero),
            TokenHash = "not-a-token",
        };

        Assert.False(edited.WasCreatedByThisPlugin(NotInvitedAtAll));
    }

    [Fact]
    public void ARecordWrittenBeforeThisFieldExistedClaimsNothing()
    {
        // A record at the schema version that came before the field. It is the
        // population an operator already has on disk, and the reading of its
        // silence decides whether an upgrade deletes their accounts.
        const string WrittenByTheOlderCode = """
            {
              "SchemaVersion": 1,
              "Id": "55555555-5555-5555-5555-555555555555",
              "ItemId": "11111111-1111-1111-1111-111111111111",
              "InvitedUserIds": [
                "22222222-2222-2222-2222-222222222222",
                "33333333-3333-3333-3333-333333333333"
              ],
              "CreatedByUserId": "44444444-4444-4444-4444-444444444444",
              "CreatedAt": "2026-01-02T03:04:05+00:00",
              "ExpiresAt": "2026-01-09T03:04:05+00:00",
              "TokenHash": "not-a-token"
            }
            """;

        var older = JsonSerializer.Deserialize<ShareRecord>(WrittenByTheOlderCode)!;

        Assert.Equal(1, older.SchemaVersion);
        Assert.NotEqual(ShareRecord.CurrentSchemaVersion, older.SchemaVersion);
        Assert.Equal(2, older.InvitedUserIds.Count);
        Assert.Empty(older.PluginCreatedUserIds);
        Assert.False(older.WasCreatedByThisPlugin(MadeByThePlugin));
        Assert.False(older.WasCreatedByThisPlugin(TheOperatorsOwnAccount));
    }

    [Fact]
    public void ARecordWhoseProvenanceIsExplicitlyNullClaimsNothing()
    {
        // Absent and null are different documents and a defaulted property only
        // covers the first. The second is what an edit produces, and it would
        // otherwise reach the routine as a null list and throw somewhere further
        // on, in the middle of deciding what to delete.
        const string EditedToNull = """
            {
              "SchemaVersion": 2,
              "Id": "55555555-5555-5555-5555-555555555555",
              "ItemId": "11111111-1111-1111-1111-111111111111",
              "InvitedUserIds": ["22222222-2222-2222-2222-222222222222"],
              "PluginCreatedUserIds": null,
              "CreatedByUserId": "44444444-4444-4444-4444-444444444444",
              "CreatedAt": "2026-01-02T03:04:05+00:00",
              "ExpiresAt": "2026-01-09T03:04:05+00:00",
              "TokenHash": "not-a-token"
            }
            """;

        var edited = JsonSerializer.Deserialize<ShareRecord>(EditedToNull)!;

        Assert.Empty(edited.PluginCreatedUserIds);
        Assert.False(edited.WasCreatedByThisPlugin(MadeByThePlugin));
    }

    [Fact]
    public void TheProvenanceOfAnAccountSurvivesTheRoundTrip()
    {
        // The field is worth nothing if it does not come back off disk. A record
        // that loses it reads exactly like a record that never claimed anything,
        // which is the safe direction and therefore the one that fails silently.
        var written = JsonSerializer.Serialize(ShareInvitingBoth());
        var read = JsonSerializer.Deserialize<ShareRecord>(written)!;

        Assert.True(read.WasCreatedByThisPlugin(MadeByThePlugin));
        Assert.False(read.WasCreatedByThisPlugin(TheOperatorsOwnAccount));
    }
}
