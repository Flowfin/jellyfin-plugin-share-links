using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ShareLinks.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// <para>
/// A setting arrives here together with the routine that reads it and the
/// refusal that bounds it; a setting that means nothing is a setting somebody
/// eventually wires to something.
/// </para>
/// <para>
/// The four bounds below are read by <see cref="ShareBounds"/>, which refuses a
/// value outside what the setting admits rather than serving under a rule nobody
/// wrote. Refusal on save, and the controls an operator edits these with, are
/// #71 and #70 and are not here yet, so a value edited into the file by hand is
/// refused when a share is created and not when it is written.
/// </para>
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the URL this server is reached at from outside, with no
    /// trailing slash, for example <c>https://media.example.org</c> or
    /// <c>https://example.org/jellyfin</c> when a proxy mounts it under a path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty means the link is built from what the request claimed, which is text
    /// a caller supplies. <see cref="ShareLinkBuilder"/> is where that is argued
    /// and where the fallback lives.
    /// </para>
    /// <para>
    /// A string rather than a <see cref="System.Uri"/>, because the server writes
    /// this class out with <c>XmlSerializer</c> and that serialiser refuses a type
    /// with no parameterless constructor. What the setting has to survive is the
    /// round trip, and <c>PluginConfigurationTests</c> is where it does.
    /// </para>
    /// </remarks>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ceiling on how many shares may be live across the server at once.
    /// </summary>
    /// <remarks>
    /// Live means neither revoked nor past its expiry. Revoking or letting a share
    /// expire frees a place; deleting the record is retention's job and not this
    /// one's. <c>docs/bounds.md</c> is where the number is argued.
    /// </remarks>
    public int MaxLiveShares { get; set; } = ShareBounds.DefaultMaxLiveShares;

    /// <summary>
    /// Gets or sets the ceiling on how many live shares may name one item.
    /// </summary>
    /// <remarks>
    /// A share is one item handed to one invited guest, so several live shares on
    /// one item is the ordinary case of a film lent to several people. This is the
    /// bound that keeps a loop pointed at one item from consuming the whole
    /// server ceiling.
    /// </remarks>
    public int MaxLiveSharesPerItem { get; set; } = ShareBounds.DefaultMaxLiveSharesPerItem;

    /// <summary>
    /// Gets or sets the longest lifetime, in days, a link may be given.
    /// </summary>
    /// <remarks>
    /// Checked when a share is created and never when one is resolved, so lowering
    /// this does not shorten links an operator has already handed out.
    /// <c>docs/expiry.md</c> is where that is argued.
    /// </remarks>
    public int MaxShareLifetimeDays { get; set; } = ShareBounds.DefaultMaxShareLifetimeDays;

    /// <summary>
    /// Gets or sets how many days a share that has stopped working is kept before it is deleted.
    /// </summary>
    /// <remarks>
    /// Counted from the instant the share stopped answering rather than from when
    /// it was created. Zero deletes it at the first write after that instant,
    /// which is how an operator empties the store of what has expired.
    /// </remarks>
    public int ExpiredShareRetentionDays { get; set; } = ShareBounds.DefaultExpiredShareRetentionDays;

    /// <summary>
    /// Gets or sets the ceiling a new share is given when the operator creating it
    /// names none, in megabits per second. No value means new shares get no
    /// ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Megabits per second here and bits per second on the record, which is
    /// <see cref="BitrateCap"/>'s reason for existing along with the bounds this
    /// value is refused against. An empty value is no ceiling rather than a
    /// ceiling of zero, and zero is refused, because serve nothing and serve
    /// without a limit are opposite instructions.
    /// </para>
    /// <para>
    /// A default rather than a ceiling of its own. A share created with a ceiling
    /// of its own keeps it, and what happens when several ceilings apply at once
    /// is #64.
    /// </para>
    /// <para>
    /// Nullable rather than a sentinel number, so that "no ceiling" is the absence
    /// of a value in the file instead of a magic one an operator has to know.
    /// <c>PluginConfigurationTests</c> is where that survives the serialiser the
    /// server writes this class out with.
    /// </para>
    /// </remarks>
    public double? DefaultMaxBitrateMbps { get; set; }

    /// <summary>
    /// Gets or sets how many sessions one invited guest may hold at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ceiling on the account rather than on the share, because the switch it is
    /// written onto belongs to the account. A guest invited to two shares carries
    /// one ceiling across both and not one each, which is a consequence an
    /// operator meets rather than a choice this setting offers;
    /// <c>docs/guest-capabilities.md</c> is where that is argued.
    /// </para>
    /// <para>
    /// <see cref="GuestPolicy"/> is where the value is refused against its bounds
    /// and where an account that already carries a lower ceiling keeps it, because
    /// this plugin narrows an account and never widens one.
    /// </para>
    /// </remarks>
    public int GuestMaxActiveSessions { get; set; } = GuestPolicy.DefaultMaxActiveSessions;
}
