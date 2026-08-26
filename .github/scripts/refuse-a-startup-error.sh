#!/usr/bin/env bash
# Refuses a server that did not come up clean, over the log it wrote (#96).
#
# The first bullet of #96 asks that both boots come up without startup errors.
# What the job did until now was print the log and leave the reading to a person,
# which its own step comment admitted, and a green run therefore said nothing
# about whether anybody had looked.
#
# WHAT IS REFUSED IS CHOSEN NARROWLY, AND THE REASON IS THAT THIS LOG IS NOT
# THIS REPOSITORY'S OUTPUT. It is a third party's server writing about itself and
# about several plugins, so a rule over every error line in it reds this board's
# nightly for something that happened in another project, on the day that project
# writes a new one. Two things are refused and everything else is printed:
#
#   * A FATAL, anywhere. The server writing `[FTL]` is the server not coming up,
#     and nothing this harness does provokes one.
#   * AN ERROR FROM THIS PLUGIN, anywhere. The source context of a line names the
#     type that wrote it, so an error out of `Jellyfin.Plugin.ShareLinks` is this
#     board's own and is never expected. The window is the whole run rather than
#     the startup, because an error from this plugin is wrong at any moment and
#     the boundary of a startup window is not something the log carries.
#
# Every other error is COUNTED AND PRINTED AND NOT REFUSED. The run provokes one
# of them on purpose - the session past the ceiling - and the browser leg has
# produced a cancelled request on both boots, and neither is this plugin's.
#
# The log has to be the right server's. A refusal over an empty file, or over the
# log of a server that never loaded this plugin, passes exactly like a clean run,
# so both are refused before anything else is read.
#
# Usage:
#   refuse-a-startup-error.sh <log> <what boot this was>
set -euo pipefail

log="${1:?the log to read}"
which="${2:-this boot}"

# The assembly name the server prints when it loads this plugin, and the source
# context every line this plugin writes carries. One string, because they are the
# same name:
#
#   git grep -n '<AssemblyName>\|RootNamespace' -- Directory.Build.props Jellyfin.Plugin.ShareLinks/Jellyfin.Plugin.ShareLinks.csproj
plugin="Jellyfin.Plugin.ShareLinks"

refusals=0
say() { printf '\n===== %s =====\n' "$*"; }
refuse() {
  printf '::error::%s\n' "$*" >&2
  refusals=$((refusals + 1))
}

say "the log of the boot $which"
[ -f "$log" ] || {
  printf '::error::%s\n' "there is no log at $log, so nothing was read" >&2
  exit 1
}
lines=$(wc -l <"$log")
echo "$lines line(s)"
[ "$lines" -gt 0 ] || refuse "the log of the boot $which is empty, which is not a server that came up clean"

# What says this is the log of a server that ran this plugin. Without it every
# rule below is being applied to somebody else's server, or to nothing, and
# passes for that reason.
if grep -qF "Loaded assembly $plugin" "$log"; then
  grep -F "Loaded assembly $plugin" "$log" | head -1
else
  refuse "the log of the boot $which never says the server loaded $plugin, so whatever this is the log of, it is not the subject"
fi

say "a fatal, anywhere"
if grep -qF '[FTL]' "$log"; then
  grep -F '[FTL]' "$log" | head -20
  refuse "the server wrote a fatal on the boot $which, which is the server not coming up"
else
  echo "none"
fi

say "an error from this plugin, anywhere"
# The source context sits between the thread number and the message and ends at
# the colon, so the plugin's own lines are matched by the name followed by a dot
# and a type. A message that merely mentions the plugin is a different line and
# is not this rule's subject.
if grep -E '\[ERR\]' "$log" | grep -qE "$plugin\.[A-Za-z0-9_]+:"; then
  grep -E '\[ERR\]' "$log" | grep -E "$plugin\.[A-Za-z0-9_]+:" | head -20
  refuse "this plugin wrote an error on the boot $which"
else
  echo "none"
fi

say "every other error, printed rather than refused"
# Not a rule. These are the server's own lines about its own work and about other
# plugins, and a board that reds on them is reporting on somebody else's project.
others=$(grep -cE '\[ERR\]' "$log" || true)
echo "$others error line(s) in this log, none of them this plugin's:"
grep -E '\[ERR\]' "$log" | sed 's/^/  /' | head -20 || true

say "verdict"
if [ "$refusals" -gt 0 ]; then
  echo "$refusals refusal(s) on the boot $which"
  exit 1
fi
echo "the boot $which came up with no fatal and no error of this plugin's"
