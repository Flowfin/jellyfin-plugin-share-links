using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A name handed to <see cref="PluginServiceRegistrator.InTheDataFolder"/> cannot
/// reach outside the plugin's data folder.
/// </summary>
/// <remarks>
/// <para>
/// <c>Path.Combine</c> discards its first argument when the second is rooted, so
/// combining the data folder with <c>/etc/passwd</c> returns <c>/etc/passwd</c> -
/// silently, with no error anywhere. CodeQL reported the site as
/// <c>cs/path-combine</c> and could not see the callers.
/// </para>
/// <para>
/// Every caller in this plugin passes a compile-time constant, which is why
/// nothing has ever escaped. That is the reason for this file rather than an
/// argument against it: the method is public and static, and until the check
/// landed its safety was a property of two call sites somebody had to go and
/// read. A third caller written next year would have been warned by nothing.
/// </para>
/// <para>
/// The first assertion below is the one that matters. Delete the rooted-path
/// check and it reds with the escaped path in its message, which is the whole
/// defect in one line.
/// </para>
/// </remarks>
public class DataFolderNameTests
{
    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/tmp/elsewhere.json")]
    public void ARootedNameIsRefused(string name)
    {
        var refused = Assert.Throws<ArgumentException>(() => PluginServiceRegistrator.InTheDataFolder(name));
        Assert.Equal("fileName", refused.ParamName);
    }

    [Theory]
    [InlineData("../shares.json")]
    [InlineData("nested/../../shares.json")]
    public void ANameThatWalksUpwardsIsRefused(string name)
    {
        var refused = Assert.Throws<ArgumentException>(() => PluginServiceRegistrator.InTheDataFolder(name));
        Assert.Equal("fileName", refused.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyNameIsRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => PluginServiceRegistrator.InTheDataFolder(name));
    }

    [Fact]
    public void ANullNameIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => PluginServiceRegistrator.InTheDataFolder(null!));
    }

    /// <summary>
    /// THE NEAR-MISS. Both names this plugin actually uses have to survive, or the
    /// check would be a guard that refuses the only two callers there are - which
    /// every test above would still pass.
    /// </summary>
    /// <remarks>
    /// The call reaches <c>Plugin.Instance</c>, which is null outside a running
    /// server, so what is asserted is that the refusal is NOT the argument one.
    /// The name got past the check and the method failed later, for the reason it
    /// documents.
    /// </remarks>
    [Theory]
    [InlineData(PluginServiceRegistrator.StoreFileName)]
    [InlineData(PluginServiceRegistrator.KeyFileName)]
    public void TheNamesThisPluginUsesArePassed(string name)
    {
        var refused = Record.Exception(() => PluginServiceRegistrator.InTheDataFolder(name));
        Assert.IsNotType<ArgumentException>(refused);
    }

    /// <summary>
    /// What the check is about, stated as the behaviour rather than as a rule:
    /// this is what <c>Path.Combine</c> does with a rooted second argument.
    /// </summary>
    [Fact]
    public void CombineReallyDoesDiscardTheFirstArgument()
    {
        var escaped = Path.Combine(Path.Combine("data", "share-links"), Path.DirectorySeparatorChar + "etc");
        Assert.Equal(Path.DirectorySeparatorChar + "etc", escaped);
    }
}
