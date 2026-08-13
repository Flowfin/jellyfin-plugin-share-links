using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ShareLinks.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The configuration judged as one thing, and refused as one thing (#71).
/// </summary>
/// <remarks>
/// <para>
/// The interesting guard here is not that a bad value is refused. Each setting's
/// own routine already did that, and those refusals have their own tests beside
/// the routines that carry them. It is that no setting escapes: a setting added
/// to the class with no bound is a value the plugin will serve under, and nothing
/// about that is visible, because a configuration with an unbounded setting looks
/// exactly like one whose settings are all in range.
/// </para>
/// <para>
/// So the table below is compared against the class by reflection, in both
/// directions, which is the same shape <see cref="ConfigurationReferenceTests"/>
/// uses against the documentation and for the same reason. A setting added
/// without a row reds the suite, and a row naming a setting that was renamed away
/// reds it too.
/// </para>
/// <para>
/// What these do not reach. They judge the class rather than a running server, so
/// the save path is exercised by calling <see cref="Plugin.UpdateConfiguration"/>
/// directly and not by driving the server's route to it, and no test here proves
/// what a server does with the exception that comes back.
/// </para>
/// </remarks>
public class ConfigurationValidationTests
{
    // One value per setting that the plugin may not work under, with the reason
    // it is the value worth picking. Each is the mistake somebody actually makes
    // rather than the furthest thing from valid: a URL with the scheme left off,
    // a ceiling of zero typed in the belief that it means no ceiling, a lifetime
    // above the ceiling that bounds it, and a bitrate typed in bits.
    public static TheoryData<string, object?> InvalidValues => new TheoryData<string, object?>
    {
        { nameof(PluginConfiguration.PublicBaseUrl), "media.example.org" },
        { nameof(PluginConfiguration.MaxLiveShares), 0 },
        { nameof(PluginConfiguration.MaxLiveSharesPerItem), 0 },
        { nameof(PluginConfiguration.MaxShareLifetimeDays), 0 },
        { nameof(PluginConfiguration.DefaultShareLifetimeDays), ShareBounds.DefaultMaxShareLifetimeDays + 1 },
        { nameof(PluginConfiguration.ExpiredShareRetentionDays), -1 },
        { nameof(PluginConfiguration.DefaultMaxBitrateMbps), 8_000_000d },
        { nameof(PluginConfiguration.GuestMaxActiveSessions), 0 },
    };

    private static IReadOnlyList<PropertyInfo> Settings() =>
        typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

    private static PluginConfiguration With(string setting, object? value)
    {
        var configuration = new PluginConfiguration();
        var property = typeof(PluginConfiguration).GetProperty(setting);
        Assert.NotNull(property);
        property!.SetValue(configuration, value);
        return configuration;
    }

    [Fact]
    public void EverySettingOnTheClassHasAValueThatIsRefused()
    {
        var covered = InvalidValues.Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);

        foreach (var property in Settings())
        {
            Assert.True(
                covered.Contains(property.Name),
                $"{property.Name} is a setting an operator can change and no row here names a value it is refused for, so nothing proves it is bounded at all");
        }
    }

    [Fact]
    public void EveryRowNamesASettingOnTheClass()
    {
        var declared = Settings().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var row in InvalidValues)
        {
            var setting = (string)row[0]!;
            Assert.True(
                declared.Contains(setting),
                $"this table has a row for {setting}, which is not a writable setting on PluginConfiguration");
        }
    }

    [Fact]
    public void AFreshConfigurationIsNotRefused()
    {
        // The defaults are what an operator who changes nothing runs under, so a
        // fresh class failing its own validation would be a plugin that refuses to
        // start out of the box.
        Assert.Null(ShareConfiguration.Refuse(new PluginConfiguration()));
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void AnInvalidValueIsRefusedAndTheMessageNamesTheSetting(string setting, object? value)
    {
        var refusal = ShareConfiguration.Refuse(With(setting, value));

        Assert.NotNull(refusal);
        Assert.Contains(setting, refusal, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void SavingAnInvalidValueIsRefusedAndTheMessageNamesTheSetting(string setting, object? value)
    {
        var plugin = APlugin(out _);

        var refused = Assert.Throws<ArgumentException>(() => plugin.UpdateConfiguration(With(setting, value)));

        Assert.Contains(setting, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingAConfigurationTheSettingsAdmitIsNotRefused()
    {
        var plugin = APlugin(out var directory);

        try
        {
            var configuration = new PluginConfiguration { MaxLiveShares = 42 };

            plugin.UpdateConfiguration(configuration);

            Assert.Equal(42, plugin.Configuration.MaxLiveShares);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheDefaultLifetimeIsRefusedAgainstTheCeilingRatherThanAgainstANumberOfItsOwn()
    {
        // The same lifetime, valid under one ceiling and refused under a lower one.
        // Written as one pair because the relation is the whole rule: a number that
        // is only ever compared against a constant would pass both of these.
        var inside = new PluginConfiguration { MaxShareLifetimeDays = 30, DefaultShareLifetimeDays = 14 };
        var outside = new PluginConfiguration { MaxShareLifetimeDays = 7, DefaultShareLifetimeDays = 14 };

        Assert.Null(ShareConfiguration.Refuse(inside));
        Assert.Equal(TimeSpan.FromDays(14), ShareConfiguration.DefaultShareLifetimeFrom(inside));

        var refusal = ShareConfiguration.Refuse(outside);
        Assert.NotNull(refusal);
        Assert.Contains(nameof(PluginConfiguration.DefaultShareLifetimeDays), refusal, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareConfiguration.DefaultShareLifetimeFrom(outside));
    }

    [Fact]
    public void ADefaultLifetimeOfZeroIsRefusedRatherThanReadAsNoExpiryAtAll()
    {
        // The table above holds the upper bound for this setting, because a table
        // of one value per setting can hold one. The lower bound is the value an
        // operator types on purpose, in the belief that zero means the share does
        // not expire, and it is the opposite instruction.
        var refusal = ShareConfiguration.Refuse(new PluginConfiguration { DefaultShareLifetimeDays = 0 });

        Assert.NotNull(refusal);
        Assert.Contains(nameof(PluginConfiguration.DefaultShareLifetimeDays), refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ACeilingOutOfRangeIsNamedBeforeTheDefaultThatIsComparedAgainstIt()
    {
        // MaxShareLifetimeDays of zero makes every default lifetime "above the
        // ceiling" as well, and a routine that asked in the other order would send
        // the operator to the line that is not the mistake.
        var configuration = new PluginConfiguration { MaxShareLifetimeDays = 0 };

        var refusal = ShareConfiguration.Refuse(configuration);

        Assert.NotNull(refusal);
        Assert.Contains(nameof(PluginConfiguration.MaxShareLifetimeDays), refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(PluginConfiguration.DefaultShareLifetimeDays), refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyBaseUrlIsTheDefaultRatherThanAnInvalidValue()
    {
        Assert.Null(ShareLinkBuilder.Refuse(string.Empty));
        Assert.Null(ShareLinkBuilder.Refuse(null));
        Assert.Null(ShareLinkBuilder.Refuse("   "));
    }

    [Fact]
    public void WhatTheValidationAdmitsIsWhatTheLinkBuilderAdmits()
    {
        // The two would drift if the validation held its own copy of the URL rule,
        // and the drift that matters is this direction: a value accepted on save
        // and then refused when a link is built is a share an operator cannot
        // create and was told nothing about.
        const string Configured = "https://media.example.org";

        Assert.Null(ShareLinkBuilder.Refuse(Configured));
        Assert.Equal(
            new Uri("https://media.example.org/ShareLinks/Guest/abc"),
            ShareLinkBuilder.Build(Configured, null, "/ShareLinks/Guest/abc"));
    }

    [Fact]
    public void NothingIsJudgedWhenThereIsNothingToJudge()
    {
        Assert.Throws<ArgumentNullException>(() => ShareConfiguration.Refuse(null!));
        Assert.Throws<ArgumentNullException>(() => ShareConfiguration.DefaultShareLifetimeFrom(null!));
        Assert.Throws<ArgumentNullException>(() => ShareBounds.RefuseSettings(null!));
    }

    // A plugin with somewhere real to write, because the accepting path saves and
    // a save with nowhere to go is a failure that says nothing about validation.
    // The directory is this test's own and is removed by the test that uses it.
    private static Plugin APlugin(out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), "share-links-configuration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(path => path.PluginConfigurationsPath).Returns(directory);
        paths.SetupGet(path => path.PluginsPath).Returns(directory);

        return new Plugin(paths.Object, Mock.Of<IXmlSerializer>());
    }
}
