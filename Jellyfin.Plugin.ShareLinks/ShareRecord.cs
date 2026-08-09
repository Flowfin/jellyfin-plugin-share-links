using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// One share, as it is written down (#33).
/// </summary>
/// <remarks>
/// <para>
/// This is the shape the store holds, decided before the code that reads it, so
/// that the fields are argued about once rather than accumulated one at a time by
/// whatever the next routine happened to need. Where the file lives and why is
/// <c>docs/share-store.md</c>. How it is written without a crash truncating it is
/// #35, and what happens when this shape changes under an operator who already
/// has records is #37.
/// </para>
/// <para>
/// Two things are deliberately absent. The raw token is not here, because a store
/// that holds it turns a copy of one file into every live share; only
/// <see cref="TokenHash"/> is, and #43 decides what that hash is made with.
/// Nothing derived from a password is here either, in any field, because this
/// plugin never sees one: an invited guest signs in to the server, and the server
/// owns that exchange.
/// </para>
/// <para>
/// The invited accounts are a set rather than one account. That follows the
/// design as it stands and is one of the questions collected in #94, which can
/// narrow it to exactly one. A set narrows to one without changing the shape of
/// anything that reads it; one widens to a set only by changing every reader, so
/// the set is the cheaper thing to be wrong about.
/// </para>
/// <para>
/// Every member is init-only. A share is not edited after it is made, with one
/// exception that is revocation, and revocation is recorded by writing a new
/// record over the old one rather than by mutating an object some other request
/// is holding. That is a property of this type and not yet of any store, because
/// there is no store; #35 is where it becomes one.
/// </para>
/// </remarks>
public sealed class ShareRecord
{
    private readonly IReadOnlyList<Guid> _pluginCreatedUserIds = Array.Empty<Guid>();

    /// <summary>
    /// The schema version a record written by this code carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is here so that the first release already stamps its records, rather
    /// than a later release meeting a directory of records that say nothing about
    /// what wrote them. What reads an older number, and what refuses a newer one,
    /// is #37.
    /// </para>
    /// <para>
    /// Version 2 added <see cref="PluginCreatedUserIds"/> (#144). A record at
    /// version 1 carries no provenance for its invited accounts at all, and the
    /// only safe reading of that silence is that this plugin created none of
    /// them, because the other reading hands an account somebody else made to
    /// whatever eventually deletes one.
    /// </para>
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Gets the version of this record's shape.
    /// </summary>
    /// <remarks>
    /// Written from <see cref="CurrentSchemaVersion"/> and read back as whatever
    /// wrote it, which is the point of it: a record is worth reading only once
    /// the code knows which shape it is in.
    /// </remarks>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Gets the identifier of this share.
    /// </summary>
    /// <remarks>
    /// The name a share is known by everywhere except in the link. It is what an
    /// operator revokes, what an administrator view lists, and what a log line
    /// can name a share by without the line becoming usable, which is the shape
    /// #27 decides. It is not the token and it is not derived from the token.
    /// </remarks>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the identifier of the one library item this share is for.
    /// </summary>
    /// <remarks>
    /// One item, fixed when the share is made. The scope of a share cannot widen
    /// because there is nowhere in this record for a second item to go, and #44
    /// is where that is proven of the resolution path rather than only of the
    /// shape.
    /// </remarks>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the accounts this share is for.
    /// </summary>
    /// <remarks>
    /// The link resolves for these accounts and for nobody else. Holding the
    /// accounts rather than a count is what lets a resolution answer the question
    /// it is actually asked, which is whether this caller is invited, without
    /// consulting anything the request supplied.
    /// </remarks>
    public required IReadOnlyList<Guid> InvitedUserIds { get; init; }

    /// <summary>
    /// Gets the invited accounts this plugin brought into existence itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A subset of <see cref="InvitedUserIds"/>, and the field that separates an
    /// account this plugin made from an account it was merely pointed at. Without
    /// it the list of invited accounts is a list of identifiers with nothing
    /// beside them, and anything that removes an account has no way to tell the
    /// two apart. The consequence is not an untidy store: it is this plugin
    /// deleting a person's real account because an identifier reached the list
    /// through a store carried forward from before the decision, a record edited
    /// by hand, or a route that let an operator invite an account of their own.
    /// </para>
    /// <para>
    /// Not <c>required</c>, and empty when it is not written. That is what makes
    /// the silence of a version 1 record readable: absent means this plugin
    /// created none of these accounts, which is the reading that removes nothing
    /// rather than the reading that removes everything. An explicit null in the
    /// serialised form is taken the same way, because a file that has been edited
    /// is exactly the case this field exists for.
    /// </para>
    /// <para>
    /// It is provenance and not permission. What may then be done with an account
    /// this plugin created, and when the last record naming it releases it, is
    /// #51 and #58; this record answers only which accounts it can claim.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Guid> PluginCreatedUserIds
    {
        get => _pluginCreatedUserIds;
        init => _pluginCreatedUserIds = value ?? Array.Empty<Guid>();
    }

    /// <summary>
    /// Gets the account that made this share.
    /// </summary>
    /// <remarks>
    /// Every share has an author, and a share nobody can be traced to is a share
    /// nobody can be asked about. This is the field that makes an audit of who
    /// shared what possible at all.
    /// </remarks>
    public required Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Gets when this share was made.
    /// </summary>
    /// <remarks>
    /// Offsets rather than local times, everywhere in this record. A server that
    /// moves across a daylight saving boundary, or a store copied to a machine in
    /// another zone, changes what a local time means and changes nothing about an
    /// instant. Where the instant comes from is a seam rather than the machine
    /// clock, which is #36.
    /// </remarks>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets when this share stops resolving on its own.
    /// </summary>
    /// <remarks>
    /// Not nullable, because a share that never expires is a share somebody
    /// forgets, and the feature this plugin exists for is a link that stops
    /// working. What happens exactly at this instant, and what happens to a
    /// stream already playing when it passes, is #45.
    /// </remarks>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Gets when this share was revoked, or null if it has not been.
    /// </summary>
    /// <remarks>
    /// Null is the whole of the live case, which is why revocation is a time
    /// rather than a flag beside a time: two fields that can disagree eventually
    /// do. Revocation being immediate, repeatable without complaint, and recorded
    /// is #46.
    /// </remarks>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>
    /// Gets why this share was revoked, or null if it has not been.
    /// </summary>
    /// <remarks>
    /// Free text an operator writes for another operator to read later. It is
    /// never shown to a guest and never part of a refusal, because a refusal that
    /// explains itself tells the wrong reader why their link stopped working.
    /// </remarks>
    public string? RevocationReason { get; init; }

    /// <summary>
    /// Gets the ceiling on the bitrate the guest may stream this item at, in bits
    /// per second, or null for no ceiling of this share's own.
    /// </summary>
    /// <remarks>
    /// Per share, because the reason for a ceiling is usually this share rather
    /// than this server. Null means this share names none, not that none applies:
    /// a server-wide or account-wide limit still does, and taking the lowest of
    /// the ones that apply and saying which one applied is #64.
    /// </remarks>
    public long? MaxBitrateBitsPerSecond { get; init; }

    /// <summary>
    /// Gets the keyed hash of this share's token, encoded as text.
    /// </summary>
    /// <remarks>
    /// The token itself is never written down, here or anywhere else, so a copy
    /// of the store is not a set of working links. What the hash is computed
    /// with, where its key lives and how it is rotated is #43 and #28; this
    /// record holds the result and takes no position on either. It is text rather
    /// than bytes so that a record can be read by a person looking at the file
    /// during support, which is the same reason the store is a file at all.
    /// </remarks>
    public required string TokenHash { get; init; }

    /// <summary>
    /// Returns this record in the current schema, for a record read at an older
    /// one.
    /// </summary>
    /// <param name="record">The record as it was read.</param>
    /// <returns>A record carrying the same facts, stamped with <see cref="CurrentSchemaVersion"/>.</returns>
    /// <remarks>
    /// <para>
    /// Every field a later schema added has a documented reading for its absence,
    /// and that reading is what the property does when nothing sets it, so the
    /// upgrade is a copy rather than a computation. <see cref="PluginCreatedUserIds"/>
    /// is the whole of it today: absent means this plugin created none of the
    /// invited accounts, which is the reading that deletes nothing.
    /// </para>
    /// <para>
    /// It is here rather than in the store because it is a fact about this shape.
    /// The hazard it carries is a field added to this type and forgotten in this
    /// copy, which would silently drop that field from every migrated record, so
    /// the suite compares a migrated record against its source field by field
    /// rather than trusting the list below to stay complete.
    /// </para>
    /// </remarks>
    public static ShareRecord Upgraded(ShareRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new ShareRecord
        {
            SchemaVersion = CurrentSchemaVersion,
            Id = record.Id,
            ItemId = record.ItemId,
            InvitedUserIds = record.InvitedUserIds,
            PluginCreatedUserIds = record.PluginCreatedUserIds,
            CreatedByUserId = record.CreatedByUserId,
            CreatedAt = record.CreatedAt,
            ExpiresAt = record.ExpiresAt,
            RevokedAt = record.RevokedAt,
            RevocationReason = record.RevocationReason,
            MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
            TokenHash = record.TokenHash,
        };
    }

    /// <summary>
    /// Answers whether this record claims that the plugin created the account
    /// named, which is the question anything removing an account has to ask
    /// first.
    /// </summary>
    /// <param name="userId">The account being considered for removal.</param>
    /// <returns>
    /// <c>true</c> only where this record both invites the account and claims to
    /// have created it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Both halves are load bearing, and the second is the one that is easy to
    /// leave out. An identifier that appears only in
    /// <see cref="PluginCreatedUserIds"/> is a claim about an account this share
    /// is not for, and a record can arrive in that shape from a file somebody
    /// edited. Reading the claim on its own would let such a file nominate any
    /// account on the server for deletion; requiring the invitation as well
    /// bounds the damage of an edited record to the accounts the share already
    /// names.
    /// </para>
    /// <para>
    /// There is nothing in this tree that deletes an account yet. This is the
    /// routine such a call has to go through when it is written, put here rather
    /// than beside it so that the answer comes from the record instead of from
    /// whatever the caller happened to have to hand.
    /// </para>
    /// </remarks>
    public bool WasCreatedByThisPlugin(Guid userId) =>
        InvitedUserIds.Contains(userId) && PluginCreatedUserIds.Contains(userId);
}
