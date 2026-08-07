# Security

## Reporting a vulnerability

Use GitHub's private reporting on this repository. Open the Security tab and
choose Report a vulnerability. That opens a report only the maintainers of this
repository can read, and it is the route to use for anything that could be
exploited.

It is enabled, which is checkable rather than promised:

```
gh api repos/iderex/jellyfin-plugin-share-links/private-vulnerability-reporting
{"enabled":true}
```

Please do not open a public issue for a security problem. A public issue is the
finding, the affected version and often the reproduction, published to everybody
who reads this repository before anybody can fix it.

## What to expect

You get an acknowledgement within seven days that a person has read the report.
Within thirty days you get either an assessment, which says whether it is
accepted, what its scope is and what the fix looks like, or an explanation of why
it is taking longer.

This is one maintainer working on a small plugin. There is no on-call rotation
and no shorter number that would be true. If a report is urgent and quiet, say so
in it, and it is read sooner rather than differently.

If you would like credit for the finding, say so and you will be named in the
change that fixes it. If you would rather not be named, that is the default.

## What is in scope

The plugin in this repository. A share link resolving for somebody it does not
name, a token that can be guessed or replayed after it should not be, a token or
a key reaching a log, a guest reaching anything beyond the one item their share
names, or a route answering a caller it should refuse.

The Jellyfin server itself is not this repository's to fix. Report those to the
Jellyfin project. If a defect is in the server but this plugin makes it reachable
or worse, that is worth reporting here as well as there, and it will be treated as
in scope for the part this plugin owns.

## What is already known and not defended

`docs/leaked-link.md` argues what a leaked link is worth, and the answer is that
the text on its own opens nothing, because a share names an account and the
caller's identity comes from the server rather than from the link.

Some things are accepted rather than defended, and a report of one of them is not
a vulnerability here, though it is still worth reading:

- A guest who is entitled to watch can hand their own session to somebody else,
  or record what they are watching. No token model prevents that.
- The operator is trusted. They can read every file the server can read.
- Anybody who can read the server's filesystem as the server's own user has both
  the media and any key the plugin holds.
- Transport security belongs to the deployment. If the link travels over plain
  HTTP, the token is on the wire, and nothing in this plugin is between it and
  whoever is listening.

## Supported versions

None yet. No version of this plugin has been published, so there is nothing in
anybody's hands to fix and no version table to keep honest. When that changes,
this section names the versions that get fixes and this sentence goes.
