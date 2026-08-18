using System;
using System.Globalization;
using Jellyfin.Plugin.ShareLinks.Configuration;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The unit a bitrate ceiling is written in, the unit it is kept in, and the
/// bounds a value has to sit inside to be either (#62).
/// </summary>
/// <remarks>
/// <para>
/// Two units, on purpose. An operator writes megabits per second, because that is
/// the unit an uplink is sold in and the number they already know. The record
/// keeps bits per second, because that is the unit the server counts a rate in, so
/// nothing converts between the stored number and the ceiling it is compared
/// against. <c>docs/bitrate-cap.md</c> is where the enforcement point is decided.
/// </para>
/// <para>
/// One unit for both would make one of the two sides do the conversion in its
/// head. The mistake this type exists against is the one that produces: a number
/// in the wrong unit is off by a factor of a million, so a cap meant as 8 megabits
/// arrives as 8 bits or as 8 terabits, and neither reads as wrong at a glance. The
/// bounds below are what turn that into a refusal instead of a share nobody can
/// play.
/// </para>
/// <para>
/// The bounds are a judgement rather than a measurement, and each one says what it
/// is for. Nothing here was measured against a server, a client or a file, and no
/// number below is evidence of what any of them will do.
/// </para>
/// <para>
/// Absent means no ceiling, and it is absent rather than zero. That is the reading
/// <see cref="ShareRecord.MaxBitrateBitsPerSecond"/> already takes, and a second
/// convention would be two answers to one question. Zero is refused instead of
/// being read as no ceiling, because a value that says serve nothing and a value
/// that says serve without a limit are opposite instructions and the difference
/// between them cannot be a typing mistake.
/// </para>
/// <para>
/// Where the refusal happens is worth stating rather than assuming. This is the
/// routine that reads the setting, so a value outside the bounds is refused when
/// the setting is read and not when an operator writes it. Refusal on save is #71
/// and is not here, which is the same position <c>PublicBaseUrl</c> is in today
/// and is written down in <c>docs/configuration.md</c> rather than only here.
/// </para>
/// </remarks>
public static class BitrateCap
{
    /// <summary>
    /// The bits in a megabit, as a network rate is counted.
    /// </summary>
    /// <remarks>
    /// A decimal million rather than 1048576. Link rates are sold and measured in
    /// decimal units, and the server's own bitrate settings are counted the same
    /// way, so the binary reading would put every converted value 4.9 per cent
    /// under what the operator asked for.
    /// </remarks>
    public const long BitsPerMegabit = 1_000_000;

    /// <summary>
    /// The lowest ceiling an operator may set, in megabits per second.
    /// </summary>
    /// <remarks>
    /// A tenth of a megabit is under what a single audio stream needs, so a
    /// ceiling below it is a ceiling that serves nothing at all. Refusing it is
    /// preferred to accepting a share that cannot play, because the second one
    /// looks like a broken plugin to the guest and like a working one to the
    /// operator. The number is a judgement about the lowest thing worth serving
    /// and was not measured against any file.
    /// </remarks>
    public const double MinimumMegabitsPerSecond = 0.1;

    /// <summary>
    /// The highest ceiling an operator may set, in megabits per second.
    /// </summary>
    /// <remarks>
    /// A thousand megabits is a gigabit link, which is past what the uplink this
    /// feature exists for can carry. Its job is not to stop somebody with a fast
    /// line: it is to catch the unit mistake, because a value typed in bits per
    /// second lands orders of magnitude above this and is refused, where the same
    /// value accepted would be a ceiling of eight million megabits that reads as
    /// an ordinary number.
    /// </remarks>
    public const double MaximumMegabitsPerSecond = 1000;

    /// <summary>
    /// Why a ceiling may not be used, or <c>null</c> when it may.
    /// </summary>
    /// <param name="megabitsPerSecond">The ceiling as an operator wrote it, or <c>null</c> for no ceiling.</param>
    /// <returns>A sentence naming the bound that was missed, or <c>null</c>.</returns>
    /// <remarks>
    /// The sentence names the bound and the value, because an operator reading it
    /// has to know which end they are on: "out of range" leaves them to guess
    /// whether the number was too small or a million times too large.
    /// </remarks>
    public static string? Refuse(double? megabitsPerSecond)
    {
        if (megabitsPerSecond is not { } value)
        {
            return null;
        }

        if (double.IsNaN(value))
        {
            return "the ceiling is not a number";
        }

        if (value < MinimumMegabitsPerSecond)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the ceiling asked for is {value:0.###} Mbit/s and the lowest that may be set is {MinimumMegabitsPerSecond:0.###} Mbit/s");
        }

        if (value > MaximumMegabitsPerSecond)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the ceiling asked for is {value:0.###} Mbit/s and the highest that may be set is {MaximumMegabitsPerSecond:0.###} Mbit/s");
        }

        return null;
    }

    /// <summary>
    /// A ceiling an operator wrote, in the unit a record keeps it in.
    /// </summary>
    /// <param name="megabitsPerSecond">The ceiling as an operator wrote it, or <c>null</c> for no ceiling.</param>
    /// <returns>The ceiling in bits per second, or <c>null</c> when there is no ceiling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the bounds. The message carries the sentence <see cref="Refuse(double?)"/> gives.</exception>
    /// <remarks>
    /// The conversion rounds to the nearest bit rather than truncating. A ceiling
    /// is a number an operator chose, and moving it down by a fraction of a bit to
    /// avoid a decimal is a change nobody asked for in the direction nobody would
    /// notice.
    /// </remarks>
    public static long? InBitsPerSecond(double? megabitsPerSecond)
    {
        if (Refuse(megabitsPerSecond) is { } refusal)
        {
            throw new ArgumentOutOfRangeException(nameof(megabitsPerSecond), megabitsPerSecond, refusal);
        }

        if (megabitsPerSecond is not { } value)
        {
            return null;
        }

        return (long)Math.Round(value * BitsPerMegabit, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The ceiling a new share gets when the operator creating it names none.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The ceiling in bits per second, or <c>null</c> when the setting names none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The configured value is outside the bounds. The message names the setting, because a configuration file edited by hand is where it arrives from and the operator needs to know which line to fix.</exception>
    /// <remarks>
    /// This is the default rather than a ceiling of its own. A share that names
    /// its own ceiling keeps it, which is the per-share override, and the
    /// arithmetic that takes the lowest of the ceilings actually in play is #64.
    /// </remarks>
    public static long? DefaultForNewShares(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Refuse(configuration.DefaultMaxBitrateMbps) is { } refusal)
        {
            // The setting's name goes in the message rather than in the parameter
            // name, because the parameter here is the whole configuration and an
            // exception naming a parameter the method does not have is refused by
            // the analysers this project builds with.
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                configuration.DefaultMaxBitrateMbps,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{nameof(PluginConfiguration.DefaultMaxBitrateMbps)}: {refusal}"));
        }

        return InBitsPerSecond(configuration.DefaultMaxBitrateMbps);
    }
}
