# What a leaked link is worth, and where the token sits in it

This is the answer issue #24 asks for. A link is text, and text travels. It goes
into a chat window that fetches a preview of it, a mail server that scans it, a
browser history, a reverse proxy access log, and whatever backup touches any of
those. None of that is a failure anybody will notice, so the design assumes it
has already happened and asks what the holder of that text gains.

## What the holder gains

Nothing, and the reason is the shape of the design rather than the secrecy of the
token.

A share names an account. The link resolves for that account and for nobody else,
which is the sentence the readme opens with and the property every issue under
the security milestone keeps true. The token in the link names a record; it does
not say who is asking. Who is asking comes from the server, out of the request
context, and there is nothing a holder of the text can put in a URL that changes
it:

    controller=~/.nuget/packages/jellyfin.controller/10.11.11/lib/net9.0/MediaBrowser.Controller.xml
    grep -oE 'name="M:MediaBrowser.Controller.Net.IAuthorizationContext.GetAuthorizationInfo\(Microsoft.AspNetCore.Http.HttpContext\)"' "$controller"
    name="M:MediaBrowser.Controller.Net.IAuthorizationContext.GetAuthorizationInfo(Microsoft.AspNetCore.Http.HttpContext)"
    grep -oE 'name="P:MediaBrowser.Controller.Net.AuthorizationInfo.(IsAuthenticated|User|UserId)"' "$controller" | sort -u
    name="P:MediaBrowser.Controller.Net.AuthorizationInfo.IsAuthenticated"
    name="P:MediaBrowser.Controller.Net.AuthorizationInfo.User"
    name="P:MediaBrowser.Controller.Net.AuthorizationInfo.UserId"

So the two answers a leaked link can get are the two refusals: the caller is not
signed in, or the caller is signed in as somebody the share does not name. Both
are refusals and #26 is where they are made indistinguishable from each other.

**Both refusals are now made by something that runs.** The route is #68 and it is
on the mainline, so the two tests #24 asks for are two tests rather than a
sentence here:

    grep -n 'AValidUnexpiredTokenFromACallerTheServerHasNotIdentifiedGetsNothing\|ACallerTheShareDoesNotNameGetsNothing' Jellyfin.Plugin.ShareLinks.Tests/GuestRouteTests.cs

The first presents a token that resolves, from a caller the server has not
identified, and gets nothing. The second presents the same token as an account
the share does not name, and gets the same nothing. Both drive the route rather
than the decision behind it, so what they judge is what a holder of the text
would receive.

What is still held by design rather than by a test is the step in front of them.
An unauthenticated request does not reach this plugin at all, because the route
carries an authorize attribute and the server refuses first, and no test in this
suite may start a server to watch that happen.

## What a leaked link tells the holder even so

The refusal gives away nothing, but the text itself can, and that is a decision
about what goes in the link rather than about what the route answers.

**The link carries the token and nothing else.** No item identifier, no user
name, no expiry, no share number. A holder of the text then learns that somebody
made a share on this server, and that is the whole of it. Put the item identifier
in the link for convenience and the same holder learns which title was shared and
to whom it might have gone, from text that never reached the server at all.

This costs the route nothing, because the token is what the record is found by.
Anything else in the link would be a second copy of something the record already
holds.

## Where the token sits in the URL

Three positions were considered.

### The fragment

The fragment never reaches the server. RFC 3986 section 3.5 separates it from the
rest of the URI before the dereference, and RFC 9112 section 3.2.1 gives the
origin-form of a request target as an absolute path and an optional query with no
fragment in it. That is the property it is usually chosen for.

It is not available to this plugin. A token the server never receives has to be
read by script in the browser and handed back, and this plugin adds nothing to
the web client. That is the readme's sentence rather than an aspiration here, and
whether anything is added to the web client at all is one of the decisions #94
holds open. If that decision goes the other way, the fragment becomes available
and this section is where the argument restarts.

### The query string

Both statements read out of the specification above put the query in the request
target, alongside the path. So the common shorthand, that a query string is
logged where a path is not, is not a difference: an access log that records the
request line records either of them. Nothing was measured against a real proxy
here, and no claim about any particular proxy's default configuration is made in
either direction.

### The path segment, which is the choice

The token sits in the path, as one segment of the guest route.

Two reasons, and neither is about logging.

The first is that a path segment is part of the route rather than a value inside
it. A request that carries no token does not match the route at all, so it is
refused before any code of this plugin's runs. A query parameter is bound by name
and its absence is an ordinary value, which the plugin has to remember to refuse,
and a refusal written by hand is a refusal that can be written wrong. This is the
reason the choice was taken and it is **not measured here**: it becomes a test
when the route exists, and #68 is where it is owed.

The second is that a path segment has nowhere to grow. A query string invites a
second parameter beside the first, and the second parameter is where an item
identifier ends up during an afternoon of making the link easier to debug. A
route whose template is one token-shaped segment refuses that change by having no
room for it.

The token needs no escaping in either position. Every character of its alphabet
is unreserved in a URI, which is recorded against the routine that mints it:

    git grep -n 'public const string Alphabet' -- Jellyfin.Plugin.ShareLinks/ShareTokens.cs
    Jellyfin.Plugin.ShareLinks/ShareTokens.cs:67:    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

## What this does not cover

Timing. That the two refusals carry the same bytes is asserted by a test on the
route; that they take the same time to produce is not measured anywhere, and no
claim about it is made here.

Where a caller who is not signed in ends up. The route answers only callers the
server has already identified, so a guest who follows the link before signing in
meets the server's own refusal rather than anything of this plugin's, and comes
back to the item only by opening the link again afterwards. Making that a round
trip needs something in the web client, which this plugin does not add, and #68
is where that is written down and what it waits on.

Nothing here was measured against a reverse proxy, a chat client's preview
fetcher or a mail scanner. The design assumes all three read the link and none of
them gain anything by it, and that assumption rests on the section above rather
than on any of them having been observed.
