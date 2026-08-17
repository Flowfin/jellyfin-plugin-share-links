using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What an operator asks for when creating a share (#67).
/// </summary>
/// <remarks>
/// <para>
/// Four things and nothing else. The item, the people, when it stops, and how
/// fast it may run. Everything else a record holds is known without being asked
/// for: the author is whoever the server signed in, the instant comes from the
/// clock the plugin was handed, and the token is minted rather than supplied,
/// because a token an operator could choose is a token an operator could reuse.
/// </para>
/// <para>
/// The guests are names rather than account identifiers, and that is decision 2
/// of #94 rather than a shape preference. This plugin creates the account the
/// share is for, so at the moment the operator asks there is no identifier to
/// give. <c>docs/guest-accounts.md</c> is the lifecycle that follows.
/// </para>
/// <para>
/// A body rather than query parameters, for the reason
/// <see cref="ShareRevocationRequest"/> gives about itself: a guest's name in a
/// query string is a person's name in the server's access log, in a proxy's log
/// and in a browser's history.
/// </para>
/// </remarks>
public sealed class ShareCreationRequest
{
    /// <summary>
    /// Gets or sets the one library item the share is for.
    /// </summary>
    /// <remarks>
    /// One item, and the record has nowhere for a second one to go, which is what
    /// stops the scope of a share widening after it exists (#44).
    /// </remarks>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the names the invited guests will be known by on this server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One account is made per name. A name the server already holds is refused
    /// back to the operator rather than made unique with a number, because a name
    /// this plugin invented is a name nobody recognises in the server's own user
    /// list, and that list is where an operator goes to find out who these people
    /// are.
    /// </para>
    /// <para>
    /// It is a list because a share for a household is one share, and a record
    /// already holds a set. It is not a way to invite an account somebody else
    /// made: every name here becomes an account this plugin created and claims in
    /// <see cref="ShareRecord.PluginCreatedUserIds"/>.
    /// </para>
    /// </remarks>
#pragma warning disable CA2227 // The model binder sets the whole collection, so the setter is the surface rather than an oversight.
    public IReadOnlyList<string>? GuestNames { get; set; }
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the instant the share stops resolving, or <c>null</c> for the configured default lifetime.
    /// </summary>
    /// <remarks>
    /// An absolute instant rather than a duration, which is <c>docs/expiry.md</c>'s
    /// decision and not this route's. What an operator types in a local zone is
    /// converted at the edge, which is here, and what is stored is the instant.
    /// Null is the ordinary case and takes <c>DefaultShareLifetimeDays</c> from the
    /// configuration, so an operator who has nothing to say about expiry still gets
    /// a share that expires.
    /// </remarks>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the ceiling this share is watched under, in megabits per second, or <c>null</c> for the configured default.
    /// </summary>
    /// <remarks>
    /// Megabits per second because that is the unit an operator's connection is
    /// sold in, which is <c>docs/bitrate-cap.md</c>'s argument; the record keeps
    /// bits per second and <see cref="BitrateCap.InBitsPerSecond"/> is the one
    /// conversion. Null takes the configured default, which is itself allowed to
    /// be no cap at all.
    /// </remarks>
    public double? MaxBitrateMbps { get; set; }
}
