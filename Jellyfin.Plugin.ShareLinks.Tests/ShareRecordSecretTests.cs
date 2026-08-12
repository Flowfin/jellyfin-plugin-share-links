using System;
using System.Buffers.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.ShareLinks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The second thing the threat model refuses the store, which is the keyed hash
/// secret, held over the record rather than only over the configuration (#32).
/// </summary>
/// <remarks>
/// <para>
/// T4 and T5 in <c>docs/threat-model.md</c> are two halves of one sentence. T4 is
/// that the store holds a keyed hash and never the token, and
/// <c>ShareRecordTests</c> holds it with a search of the serialised form and a
/// near miss that shows the search would find one. T5 is that the key is not in
/// the store, and it buys the whole of what T4 is worth: a copy of a store whose
/// key came with it is a set of working tokens again, so a record that carried
/// its own key would undo the field it was carrying.
/// </para>
/// <para>
/// Only the configuration side of T5 was asserted by anything that ran, in
/// <c>ShareKeyTests.TheKeyNeverAppearsInThePluginConfiguration</c>. The record
/// carrying no key material was true by reading the type, which is the state a
/// field added later moves out of without anything saying so.
/// </para>
/// <para>
/// The key is read through <see cref="ShareKeyFile"/> in a directory of this
/// test's own rather than assembled here, so this file draws no bytes of its own
/// and the material searched for is the material the plugin would really hold.
/// </para>
/// </remarks>
public sealed class ShareRecordSecretTests : IDisposable
{
    private static readonly Guid ItemId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Guest = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid Operator = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly string _directory;

    public ShareRecordSecretTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-record-secret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
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

    private string KeyPath => Path.Combine(_directory, "share-key");

    /// <summary>
    /// Gets the two spellings a key would arrive in if one ever reached a record.
    /// </summary>
    /// <remarks>
    /// Hexadecimal and base64 without padding, because those are how a byte
    /// string is written down when somebody puts one in a text field. Base64url
    /// beside them, and it is the one that matters most rather than a third for
    /// symmetry: it is the encoding this plugin already writes a token and a hash
    /// in, so it is the spelling key material would arrive in if a routine here
    /// ever encoded the wrong argument. It differs from base64 in two characters
    /// of its alphabet, which is enough for a search for one to walk past the
    /// other.
    /// </remarks>
    private static string[] SpellingsOf(byte[] key) =>
    [
        Convert.ToHexString(key),
        Convert.ToBase64String(key).TrimEnd('='),
        Base64Url.EncodeToString(key),
    ];

    /// <summary>
    /// Gets a spelling as the serialiser writes it, which is what a search of a
    /// serialised document has to look for.
    /// </summary>
    /// <remarks>
    /// This is not defensive. Searching a document for base64 as it was handed
    /// out finds nothing, because the encoder writes a plus sign as its escape
    /// and the needle and the haystack then disagree on one character in the
    /// middle of the key. A search that walks past the material it exists to find
    /// reports a clean document either way, which is the failure this file is
    /// about turned on the file itself. It was found by the near miss below
    /// failing, and the near miss is what keeps it found.
    /// </remarks>
    private static string AsWritten(string value) =>
        JsonSerializer.Serialize(value).Trim('"');

    private static ShareRecord ARecord(string tokenHash, string? reason) => new()
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = new Guid("12121212-1212-1212-1212-121212121212"),
        ItemId = ItemId,
        InvitedUserIds = [Guest],
        CreatedByUserId = Operator,
        CreatedAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
        RevocationReason = reason,
        TokenHash = tokenHash,
    };

    [Fact]
    public void TheSerialisedFormCarriesNoKeyMaterial()
    {
        var key = new ShareKeyFile(KeyPath).Read();
        var token = ShareTokens.Mint();
        var digest = ShareTokenHash.Compute(key, token);

        var written = JsonSerializer.Serialize(ARecord(digest, "the operator took it down"));

        foreach (var spelling in SpellingsOf(key))
        {
            Assert.DoesNotContain(AsWritten(spelling), written, StringComparison.OrdinalIgnoreCase);
        }

        // The absence above is worth nothing unless the document searched is the
        // one this record produced, so the hash is read back as a value rather
        // than searched for as text.
        Assert.Equal(digest, JsonSerializer.Deserialize<ShareRecord>(written)!.TokenHash);
    }

    [Fact]
    public void ASearchOfTheSerialisedFormWouldFindKeyMaterial()
    {
        // The test above proves an absence by searching text, which proves
        // nothing unless the search would find the material when it is there.
        // A record carrying each spelling in a text field is found by exactly the
        // search that test makes, escaping included.
        var key = new ShareKeyFile(KeyPath).Read();
        var token = ShareTokens.Mint();
        var digest = ShareTokenHash.Compute(key, token);

        foreach (var spelling in SpellingsOf(key))
        {
            Assert.Contains(
                AsWritten(spelling),
                JsonSerializer.Serialize(ARecord(digest, spelling)),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoMemberIsNamedForAKeyOrASecret()
    {
        // The assertions above read one serialised document each. This one reads
        // the type, and catches the member that would carry key material on some
        // other path than a fixture happens to take, including one nothing
        // populates yet.
        var suspicious = typeof(ShareRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name)
            .Where(name =>
                name.Contains("Key", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Secret", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(suspicious);
    }
}
