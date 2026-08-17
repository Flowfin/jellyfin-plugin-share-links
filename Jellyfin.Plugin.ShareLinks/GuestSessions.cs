using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Ending the server sessions a share was keeping alive (#55).
/// </summary>
/// <remarks>
/// <para>
/// A revocation that only refuses the next click is not a revocation. A guest who
/// is watching holds a session the server signed in, and that session goes on
/// working after the record says the share has stopped, because nothing about the
/// record reaches the session. This is where the record's change is carried into
/// the server's session list.
/// </para>
/// <para>
/// Two rules bound what may be ended, and both of them narrow it. Only accounts
/// this plugin created, which is <see cref="ShareRecord.PluginCreatedUserIds"/>
/// and not the whole invited set: an account somebody else made is an account
/// somebody uses for their own watching, and signing that person out is a change
/// to a person rather than to a share, in the same family as the deletion
/// <c>docs/guest-accounts.md</c> refuses to make without provenance. And only
/// accounts that no live share names any more, because revoking one share must
/// not stop a guest watching another one they still hold.
/// </para>
/// <para>
/// What this does not reach is a segment request already in flight. The server
/// owns that handle and nothing in this plugin stands in the playback path, so
/// what stops the next request is the server refusing a token this call has
/// revoked, and not this plugin refusing a route. <c>docs/revocation.md</c> is
/// where that chain is written out and <c>docs/refused-tests.md</c> is where the
/// test that would watch the handle itself is refused, with the reason.
/// </para>
/// </remarks>
public static class GuestSessions
{
    /// <summary>
    /// The accounts a share stopping leaves with nothing to watch.
    /// </summary>
    /// <param name="records">Every record the store holds, as it stands after the share stopped.</param>
    /// <param name="stopped">The record that has stopped.</param>
    /// <param name="now">The instant the other records are judged live at.</param>
    /// <returns>The accounts whose sessions the revocation ends, in the order the record names them, each one once.</returns>
    /// <remarks>
    /// <para>
    /// <paramref name="records"/> is the store after the change rather than
    /// before it, so the record named by <paramref name="stopped"/> is expected to
    /// be among them and to be the stopped one. Handing this the list from before
    /// the revocation finds the share itself still live and ends nothing, which is
    /// a mistake that looks like a working call.
    /// </para>
    /// <para>
    /// An account named twice appears once. A record may name it twice and a
    /// second ask would be harmless, but a list with a repeat in it is a list a
    /// test cannot compare against what an operator was told.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Guid> LeftWithNothingToWatch(
        IReadOnlyList<ShareRecord> records,
        ShareRecord stopped,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(stopped);

        var ending = new List<Guid>();
        for (var index = 0; index < stopped.PluginCreatedUserIds.Count; index++)
        {
            var account = stopped.PluginCreatedUserIds[index];
            if (!ending.Contains(account) && !HoldsALiveShare(records, account, now))
            {
                ending.Add(account);
            }
        }

        return ending;
    }

    /// <summary>
    /// Ends the server sessions of the accounts named.
    /// </summary>
    /// <param name="sessions">The server's own session manager.</param>
    /// <param name="accounts">The accounts to sign out, from <see cref="LeftWithNothingToWatch"/>.</param>
    /// <returns>A task that completes when the server has been asked about every one of them.</returns>
    /// <remarks>
    /// <para>
    /// Every token the account holds, rather than a session or a device this
    /// plugin picked out. A share is not a session: a guest may have opened the
    /// link on two devices, this plugin records neither, and every token under an
    /// account it created belongs to the share that created the account.
    /// <c>ISessionManager.Logout</c> takes one access token and would be the finer
    /// instrument, and this plugin holds no access token to give it.
    /// </para>
    /// <para>
    /// The empty string is passed where the member's parameter is named
    /// <c>currentAccessToken</c>, so nothing is spared. There is no token to
    /// spare: the caller is an administrator on a session of their own and no
    /// session of theirs is under a guest account. That reading is taken from the
    /// parameter's name and its documentation on the packages this project
    /// compiles against, and not from the server's implementation, which is not in
    /// this tree and was not read.
    /// </para>
    /// </remarks>
    public static async Task EndAsync(ISessionManager sessions, IReadOnlyList<Guid> accounts)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accounts);

        for (var index = 0; index < accounts.Count; index++)
        {
            await sessions.RevokeUserTokens(accounts[index], string.Empty).ConfigureAwait(false);
        }
    }

    // Live is ShareBounds' answer rather than a second comparison over the same
    // two fields, so what this reads and what a listing shows cannot drift apart.
    private static bool HoldsALiveShare(IReadOnlyList<ShareRecord> records, Guid account, DateTimeOffset now)
    {
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (ShareBounds.IsLive(record, now) && record.InvitedUserIds.Contains(account))
            {
                return true;
            }
        }

        return false;
    }
}
