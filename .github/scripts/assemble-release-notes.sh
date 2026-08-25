#!/usr/bin/env bash
# The release notes a published version carries, assembled from the fragments in
# changelog.d (#89).
#
# The notes are a document for the operator installing the plugin, decided on #89
# on 2026-08-11 and again on 2026-08-24. That is a different document from a list
# of the commits that landed, which is what the forge builds from a tag range and
# which this replaces: a commit list is a record of the work and answers a
# question the installing operator did not ask.
#
# It fails closed in the two directions that matter. A release with no fragment at
# all stops here rather than publishing a version whose notes say nothing, and a
# fragment whose name carries no issue or an unknown kind stops here rather than
# being dropped silently out of a release nobody can cut a second time.
#
# Nothing here shells out. The whole assembly is parameter expansion and a glob,
# which costs no process per fragment and, more usefully, means the only thing
# that can fail is this file.
set -euo pipefail

# The order the headings appear in, worst-news-first, and the set a fragment name
# may end with. changelog.d/README.md documents the same list for the person
# writing a fragment, and ReleaseNotesTests compares the two so that a kind added
# to one and not the other cannot put an entry nowhere.
KINDS=(security added changed fixed removed)

if [ "$#" -ne 1 ]; then
    echo "usage: $0 <output-file>" >&2
    exit 2
fi

out="$1"
here="${BASH_SOURCE[0]%/*}"
dir="${here}/../../changelog.d"

if [ ! -d "${dir}" ]; then
    echo "::error::${dir} does not exist. The release notes are assembled from the fragments in it, and changelog.d/README.md says what one looks like."
    exit 1
fi

shopt -s nullglob
found=("${dir}"/*.md)
shopt -u nullglob

kept=()
for fragment in "${found[@]}"; do
    name="${fragment##*/}"
    if [ "${name}" != "README.md" ]; then
        kept+=("${fragment}")
    fi
done

if [ "${#kept[@]}" -eq 0 ]; then
    echo "::error::changelog.d holds no fragment, so this release would carry no notes. Write one before pushing the tag; changelog.d/README.md says what one looks like."
    exit 1
fi

kindsPattern=""
for kind in "${KINDS[@]}"; do
    kindsPattern="${kindsPattern}${kindsPattern:+|}${kind}"
done

for fragment in "${kept[@]}"; do
    name="${fragment##*/}"

    if ! [[ "${name}" =~ ^[0-9]+\.(${kindsPattern})\.md$ ]]; then
        echo "::error::changelog.d/${name} is not named <issue>.<kind>.md, with a kind of: ${KINDS[*]}"
        exit 1
    fi

    content="$(<"${fragment}")"
    if [ -z "${content//[[:space:]]/}" ]; then
        echo "::error::changelog.d/${name} is empty. A fragment that says nothing is a heading in the notes with nothing under it."
        exit 1
    fi
done

: > "${out}"

for kind in "${KINDS[@]}"; do
    heading=""

    for fragment in "${kept[@]}"; do
        name="${fragment##*/}"

        # Stripping the suffix changes the name only for a fragment of this kind.
        if [ "${name%".${kind}.md"}" = "${name}" ]; then
            continue
        fi

        if [ -z "${heading}" ]; then
            heading="${kind^}"
            printf '### %s\n\n' "${heading}" >> "${out}"
        fi

        # One entry per fragment, on one line, so a bullet list stays a bullet
        # list however the fragment was wrapped for the diff in the tree.
        content="$(<"${fragment}")"
        text="${content//$'\n'/ }"
        while [ "${text}" != "${text//  / }" ]; do
            text="${text//  / }"
        done
        text="${text# }"
        text="${text% }"

        printf -- '- %s (#%s)\n' "${text}" "${name%%.*}" >> "${out}"
    done

    if [ -n "${heading}" ]; then
        printf '\n' >> "${out}"
    fi
done

echo "Assembled ${#kept[@]} fragment(s) into ${out}."
