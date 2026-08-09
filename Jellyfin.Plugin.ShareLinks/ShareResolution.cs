using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ShareLinks;

// The marker the invariant lint reads. It sits outside the documentation comment
// because the compiler wants that comment against the declaration it describes.
//
// decides whether a share resolves: this file is the one routine (#48)

/// <summary>
/// The one place it is decided whether a presented token resolves a share (#48).
/// </summary>
/// <remarks>
/// <para>
/// The decision depends on the token, the caller, the record's state, the clock
/// and the plugin's own status. Spread over the routes that need it, that is five
/// decisions which agree until one of them is edited, and the failure is a
/// revoked share that still plays on the route somebody forgot. So there is one
/// of it, every guest route calls it, and nothing else decides.
/// </para>
/// <para>
/// It is a function of its arguments and reads nothing. No store, no server, no
/// clock of its own. That is what lets the whole table below be a test rather
/// than a deployment, and it is also the honest division of labour: reading the
/// store is the route's job and deciding is this routine's.
/// </para>
/// <para>
/// The order the conditions are taken in, and why it is this order.
/// </para>
/// <para>
/// The plugin's status first, because a plugin that is not active answers for
/// nothing and the question of which share was meant does not arise.
/// <c>docs/plugin-lifecycle.md</c> is where that is decided: the server keeps a
/// disabled plugin running until the next restart, so this read is what makes
/// disable take effect on the next request instead of on the next restart.
/// </para>
/// <para>
/// Then whether a token was presented at all, which costs nothing and asks about
/// the request rather than about any share.
/// </para>
/// <para>
/// Then the lookup, and the lookup happens before anything about the caller.
/// That is deliberate and it is about timing rather than about tidiness. A token
/// naming no share and a token naming a share the caller is not invited to have
/// to be indistinguishable (#26), and they are only indistinguishable if they
/// cost the same. <see cref="ShareLookup.ByToken"/> compares against every record
/// without stopping early, so both paths do the same work, and moving the
/// caller's check in front of it would make one of them free.
/// </para>
/// <para>
/// Then revocation, before expiry. Both refuse, so the order changes only which
/// reason the operator reads for a share that is both, and revocation is the
/// answer that does not depend on a clock. <c>docs/expiry.md</c> records that a
/// clock stepping backwards can make an expired share live again for the
/// difference, and that revocation is immune to that; reporting revocation first
/// keeps the reason an operator sees on the side that does not move.
/// </para>
/// <para>
/// Then the caller: signed in, and named by the record. The identity is a
/// parameter that comes from the server's request context, never from anything in
/// the link, which is #53 and is the property the whole design rests on.
/// </para>
/// <para>
/// What this routine does not do. It does not say what the caller may then do
/// with the item, which is <see cref="GuestPolicy"/>. It does not touch the
/// store, so nothing here records that a token was seen. It takes the key as
/// bytes and takes no position on where the key comes from, which is #28.
/// </para>
/// </remarks>
public static class ShareResolution
{
    /// <summary>
    /// Decides whether a presented token resolves a share for this caller, taking
    /// the install's key from the file that holds it (#28).
    /// </summary>
    /// <param name="records">Every record in the store, as the route read them.</param>
    /// <param name="keyFile">The file the install's key is kept in.</param>
    /// <param name="presentedToken">The token out of the request, or <c>null</c> when the request carried none.</param>
    /// <param name="callerUserId">The account the server identified the caller as, or <c>null</c> when it identified nobody.</param>
    /// <param name="pluginStatus">The plugin's own status, as the server holds it.</param>
    /// <param name="clock">The clock expiry is judged against.</param>
    /// <returns>The share, or the reason there is not one.</returns>
    /// <remarks>
    /// <para>
    /// A key that cannot be read is a refusal here rather than an exception out of
    /// the route, and it is the same shape of refusal as every other: the caller
    /// is told nothing, and the reason survives for the operator. That is what
    /// makes an unreadable key fail closed instead of failing loudly in a place
    /// somebody has to remember to catch.
    /// </para>
    /// <para>
    /// The key is read on the request rather than held, so an operator who
    /// rotates it does not have to restart the server for the new one to be in
    /// force. What that costs is a file read per request, which is the same file
    /// the store is read from beside it.
    /// </para>
    /// </remarks>
    public static ShareResolutionResult Resolve(
        IReadOnlyList<ShareRecord> records,
        ShareKeyFile keyFile,
        string? presentedToken,
        Guid? callerUserId,
        PluginStatus pluginStatus,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(keyFile);

        // Before the key, because a plugin that is not active answers for nothing
        // and has no business reading a credential to say so.
        if (pluginStatus != PluginStatus.Active)
        {
            return new ShareResolutionResult(null, ShareRefusal.PluginNotActive);
        }

        byte[] key;
        try
        {
            key = keyFile.Read();
        }
        catch (ShareKeyUnavailableException)
        {
            return new ShareResolutionResult(null, ShareRefusal.KeyUnavailable);
        }

        return Resolve(records, key, presentedToken, callerUserId, pluginStatus, clock);
    }

    /// <summary>
    /// Decides whether a presented token resolves a share for this caller.
    /// </summary>
    /// <param name="records">Every record in the store, as the route read them.</param>
    /// <param name="key">The install's key, as <see cref="ShareTokenHash"/> takes it.</param>
    /// <param name="presentedToken">The token out of the request, or <c>null</c> when the request carried none.</param>
    /// <param name="callerUserId">The account the server identified the caller as, or <c>null</c> when it identified nobody.</param>
    /// <param name="pluginStatus">The plugin's own status, as the server holds it.</param>
    /// <param name="clock">The clock expiry is judged against.</param>
    /// <returns>The share, or the reason there is not one.</returns>
    /// <remarks>
    /// The reason in the returned value is for the operator. Handing it to the
    /// caller undoes #26, because it is exactly the difference between a token
    /// that names nothing and a token that names something.
    /// </remarks>
    public static ShareResolutionResult Resolve(
        IReadOnlyList<ShareRecord> records,
        ReadOnlySpan<byte> key,
        string? presentedToken,
        Guid? callerUserId,
        PluginStatus pluginStatus,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(clock);

        if (pluginStatus != PluginStatus.Active)
        {
            return new ShareResolutionResult(null, ShareRefusal.PluginNotActive);
        }

        if (string.IsNullOrEmpty(presentedToken))
        {
            return new ShareResolutionResult(null, ShareRefusal.NoTokenPresented);
        }

        var share = ShareLookup.ByToken(records, key, presentedToken);
        if (share is null)
        {
            return new ShareResolutionResult(null, ShareRefusal.NoSuchShare);
        }

        if (share.RevokedAt is not null)
        {
            return new ShareResolutionResult(null, ShareRefusal.Revoked);
        }

        // Half-open, as docs/expiry.md decides: live strictly before the instant,
        // refused at it and after it. The comparison is against the seam's clock
        // and never the machine's, which the invariant lint refuses.
        if (clock.GetUtcNow() >= share.ExpiresAt)
        {
            return new ShareResolutionResult(null, ShareRefusal.Expired);
        }

        if (callerUserId is not { } caller)
        {
            return new ShareResolutionResult(null, ShareRefusal.CallerNotSignedIn);
        }

        if (!Invites(share, caller))
        {
            return new ShareResolutionResult(null, ShareRefusal.CallerNotInvited);
        }

        return new ShareResolutionResult(share, ShareRefusal.None);
    }

    // A membership test written out rather than a Contains call, so that the one
    // sentence this whole plugin exists for - a share is for the accounts it
    // names and for nobody else - is a line somebody has to delete rather than a
    // default somebody could change.
    private static bool Invites(ShareRecord share, Guid caller)
    {
        var invited = share.InvitedUserIds;
        for (var index = 0; index < invited.Count; index++)
        {
            if (invited[index] == caller)
            {
                return true;
            }
        }

        return false;
    }
}
