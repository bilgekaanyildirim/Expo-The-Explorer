#!/bin/bash
# codemap_guard.sh — PostToolUse hook (Edit|Write|MultiEdit on mapped source).
#
# The Stop hook repairs the codemap at the END of a turn. That is one turn too
# late: by then the file has been written, the reasoning that produced it has
# moved on, and the semantic fields get filled from memory instead of from the
# code that was just in front of Claude. This hook closes that window — it fires
# right after the write and names the exact line that is missing or unfinished.
#
# It is a NUDGE, not a gate: PostToolUse cannot undo a tool call. Exit 2 is the
# documented way to put stderr in front of Claude after the tool already ran;
# exit 0 would send it to the debug log only. The real gate stays PreToolUse.
#
# Read-only by design: it never regenerates the codemap, so an edit never
# produces a surprise write. Pure bash + coreutils.

exit_note() { printf '%s\n' "$1" >&2; exit 2; }

INPUT="$(cat 2>/dev/null)" || exit 0
[ -n "$INPUT" ] || exit 0

json_field() {  # first match only: the tool parameter precedes any content
  printf '%s' "$INPUT" | tr '\n' ' ' \
    | grep -o '"'"$1"'"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -n1 \
    | sed 's/^"[^"]*"[[:space:]]*:[[:space:]]*"\(.*\)"$/\1/'
}

FILE_PATH="$(json_field file_path)"
[ -n "$FILE_PATH" ] || exit 0

# mapped source types only (.csv / .csproj must not match)
case "$FILE_PATH" in
  *.cs|*.asmdef) : ;;
  *) exit 0 ;;
esac

PROJ="${CLAUDE_PROJECT_DIR:-$(json_field cwd)}"
[ -n "$PROJ" ] || PROJ="$(pwd)"
CDIR="$PROJ/.claude"
[ -d "$CDIR" ] || exit 0            # not a unity-dev project

REL="${FILE_PATH#"$PROJ"/}"
case "$REL" in /*) exit 0 ;; esac   # outside the project; not ours to map

MATCH=""
for f in "$CDIR"/codemap-*.md; do
  [ -f "$f" ] || continue
  line="$(grep -F -m1 -- "$REL |" "$f" 2>/dev/null)"
  if [ -n "$line" ]; then MATCH="$line"; break; fi
done

if [ -z "$MATCH" ]; then
  exit_note "codemap: no line for $REL. Write it in this turn, in the SKILL.md schema (path | role | sys: | api: | dep: | used: | crit: | note:), in the codemap file for its shard (.claude/shards.json maps path to shard). The Stop hook will add the hash."
fi

case "$MATCH" in
  ORPHAN\ *|MOVED\ *)
    exit_note "codemap: the line for $REL is marked $(printf '%s' "$MATCH" | cut -d' ' -f1). The file exists again or moved shard — repair the line and delete the marker word." ;;
esac

UNFINISHED=""
case "$MATCH" in
  *MISSING-role*) UNFINISHED="role" ;;
esac
case "$MATCH" in
  *"sys: ?"*) UNFINISHED="${UNFINISHED:+$UNFINISHED, }sys" ;;
esac
case "$MATCH" in
  *"dep?:"*) UNFINISHED="${UNFINISHED:+$UNFINISHED, }dep (script draft, unconfirmed)" ;;
esac
case "$MATCH" in
  *"crit: ?"*) UNFINISHED="${UNFINISHED:+$UNFINISHED, }crit" ;;
esac
case "$MATCH" in
  STALE\ *) UNFINISHED="${UNFINISHED:+$UNFINISHED, }STALE marker (content changed since the line was written)" ;;
esac

[ -z "$UNFINISHED" ] && exit 0
exit_note "codemap: the line for $REL is unfinished — $UNFINISHED. Complete it in this turn while the code is still in front of you; a guessed line is worse than no line."
