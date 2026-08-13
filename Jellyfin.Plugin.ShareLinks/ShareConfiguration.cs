using System;
using System.Globalization;
using Jellyfin.Plugin.ShareLinks.Configuration;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The whole configuration judged as one thing, and the lifetime a share gets
/// when nobody names one (#71).
/// </summary>
/// <remarks>
/// <para>
/// Every setting already carries its own refusal, in the routine that reads it:
/// <see cref="ShareLinkBuilder"/> for the base URL, <see cref="ShareBounds"/> for
/// the four ceilings, <see cref="BitrateCap"/> for the ceiling a new share gets
/// and <see cref="GuestPolicy"/> for the session ceiling. What did not exist is
/// an answer about the file as a whole, and without one there is no moment at
/// which an operator can be told which line is wrong: each refusal arrives when
/// its own setting is next read, which for some of them is the next time somebody
/// creates a share.
/// </para>
/// <para>
/// So this routine asks each of those rather than comparing anything a second
/// time. That is the property worth having: a validation answer that repeated the
/// comparisons would be a second copy of every bound, and the copy that drifts is
/// the one nothing enforces. The only rule declared here is the one that belongs
/// to no single setting, because it is a relation between two of them.
/// </para>
/// <para>
/// The order the settings are asked in is not cosmetic. The four ceilings are
/// asked before the default lifetime, because the default lifetime is compared
/// against <c>MaxShareLifetimeDays</c> and a comparison against a ceiling that is
/// itself out of range would name the wrong setting in the message.
/// </para>
/// <para>
/// One refusal is returned rather than all of them. An operator fixes one line,
/// saves, and is told the next one; a list of everything wrong with a file is a
/// better report and a worse repair, because every entry after the first is
/// derived from a file the operator is about to change.
/// </para>
/// </remarks>
public static class ShareConfiguration
{
    /// <summary>
    /// The shortest lifetime the default may be set to, in days.
    /// </summary>
    /// <remarks>
    /// A day rather than an hour, because the setting is in days and zero days is
    /// a link that has expired before it is sent. An operator who wants a shorter
    /// one names the expiry on the share instead, which is the per-share value
    /// this setting is only the fallback for.
    /// </remarks>
    public const int MinimumShareLifetimeDays = 1;

    /// <summary>
    /// The lifetime a share is given when the operator creating it names none, in
    /// days.
    /// </summary>
    /// <remarks>
    /// Seven days is a starting value with a reason rather than a principle, and
    /// the reason is the one <c>docs/expiry.md</c> gives for the ceiling read the
    /// other way. A share is for watching one thing, and a week is long enough for
    /// a guest to find an evening and short enough that a link nobody used stops
    /// working while the operator still remembers sending it. The ceiling in the
    /// same document is thirty days, so the default sits well inside it and an
    /// operator who lowers the ceiling to a week still has a default that fits.
    /// </remarks>
    public const int DefaultShareLifetimeDays = 7;

    /// <summary>
    /// Why the configuration may not be used, or <c>null</c> when it may.
    /// </summary>
    /// <param name="configuration">The plugin configuration, as the server read it out of the file.</param>
    /// <returns>A sentence naming the setting and what is wrong with it, or <c>null</c>.</returns>
    /// <remarks>
    /// The sentence names the setting first, because the file this answer is about
    /// is edited by hand as often as it is edited through a page, and an operator
    /// holding a message about a ceiling has to find the line to change.
    /// </remarks>
    public static string? Refuse(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (ShareLinkBuilder.Refuse(configuration.PublicBaseUrl) is { } url)
        {
            return Naming(nameof(PluginConfiguration.PublicBaseUrl), url);
        }

        // Already named by the routine that enforces it, because the ceilings are
        // refused by a constructor that is handed each setting's own name.
        if (ShareBounds.RefuseSettings(configuration) is { } ceiling)
        {
            return ceiling;
        }

        if (RefuseLifetime(configuration.DefaultShareLifetimeDays, configuration.MaxShareLifetimeDays) is { } lifetime)
        {
            return Naming(nameof(PluginConfiguration.DefaultShareLifetimeDays), lifetime);
        }

        if (BitrateCap.Refuse(configuration.DefaultMaxBitrateMbps) is { } cap)
        {
            return Naming(nameof(PluginConfiguration.DefaultMaxBitrateMbps), cap);
        }

        if (GuestPolicy.Refuse(configuration.GuestMaxActiveSessions) is { } sessions)
        {
            return Naming(nameof(PluginConfiguration.GuestMaxActiveSessions), sessions);
        }

        return null;
    }

    /// <summary>
    /// Why a default lifetime may not be used against a ceiling, or <c>null</c>
    /// when it may.
    /// </summary>
    /// <param name="defaultShareLifetimeDays">The default lifetime an operator configured, in days.</param>
    /// <param name="maxShareLifetimeDays">The ceiling on the lifetime a link may be given, in days.</param>
    /// <returns>A sentence naming the bound that was missed, or <c>null</c>.</returns>
    /// <remarks>
    /// The upper bound is the other setting rather than a number of its own. A
    /// default above the ceiling is a configuration in which every share created
    /// without an explicit expiry is refused by <see cref="ShareBounds"/>, which is
    /// a plugin that looks configured and creates nothing.
    /// </remarks>
    public static string? RefuseLifetime(int defaultShareLifetimeDays, int maxShareLifetimeDays)
    {
        if (defaultShareLifetimeDays < MinimumShareLifetimeDays)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the lifetime asked for is {defaultShareLifetimeDays} days and the shortest that may be set is {MinimumShareLifetimeDays}; zero is a link that expired before it was sent");
        }

        if (defaultShareLifetimeDays > maxShareLifetimeDays)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the lifetime asked for is {defaultShareLifetimeDays} days and MaxShareLifetimeDays is {maxShareLifetimeDays}, so every share created without an expiry of its own would be refused by the ceiling");
        }

        return null;
    }

    /// <summary>
    /// The lifetime a new share gets when the operator creating it names none.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The lifetime.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The configured value is outside what the setting admits. The message names the setting, because a configuration file edited by hand is where it arrives from and the operator needs to know which line to fix.</exception>
    /// <remarks>
    /// This is the fallback rather than a ceiling. A share created with an expiry
    /// of its own keeps it, bounded by <c>MaxShareLifetimeDays</c>, which is the
    /// bound this value is also held to.
    /// </remarks>
    public static TimeSpan DefaultShareLifetimeFrom(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (RefuseLifetime(configuration.DefaultShareLifetimeDays, configuration.MaxShareLifetimeDays) is { } refusal)
        {
            // The setting's name goes in the message rather than in the parameter
            // name, because the parameter here is the whole configuration and an
            // exception naming a parameter the method does not have is refused by
            // the analysers this project builds with.
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                configuration.DefaultShareLifetimeDays,
                Naming(nameof(PluginConfiguration.DefaultShareLifetimeDays), refusal));
        }

        return TimeSpan.FromDays(configuration.DefaultShareLifetimeDays);
    }

    private static string Naming(string setting, string refusal)
        => string.Create(CultureInfo.InvariantCulture, $"{setting}: {refusal}");
}
