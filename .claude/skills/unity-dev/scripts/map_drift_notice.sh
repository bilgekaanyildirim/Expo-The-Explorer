#!/bin/bash
# map_drift_notice.sh — UserPromptSubmit hook: surface editor-side drift.
#
# FileChanged records drift (mark_map_stale.py) but cannot tell Claude about it:
# that event has no decision control and its stderr goes to the user only. This
# hook is the delivery step. UserPromptSubmit stdout on exit 0 becomes context,
# so the notice lands alongside the prompt that follows the user's Editor work.
#
# Pure bash, one file read: it runs before every prompt and blocks the turn
# until it returns (UserPromptSubmit's default timeout is 30s, shorter than
# other events), so it does not pay a Python interpreter start.
#
# It only reports. refresh_maps.py clears .claude/map-drift on Stop, once the
# maps have actually been rebuilt — clearing it here would hide the drift from
# the very turn that needs to know about it.

INPUT="$(cat 2>/dev/null)" || exit 0

json_field() {  # first match only: the real parameter precedes any content
  printf '%s' "$INPUT" | tr '\n' ' ' \
    | grep -o '"'"$1"'"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -n1 \
    | sed 's/^"[^"]*"[[:space:]]*:[[:space:]]*"\(.*\)"$/\1/'
}

PROJ="${CLAUDE_PROJECT_DIR:-$(json_field cwd)}"
[ -n "$PROJ" ] || PROJ="$(pwd)"
MARKER="$PROJ/.claude/map-drift"
[ -f "$MARKER" ] || exit 0

N="$(wc -l < "$MARKER" 2>/dev/null | tr -d ' ')"
[ -n "$N" ] && [ "$N" -gt 0 ] 2>/dev/null || exit 0

printf '[maps] %s scene/prefab/asset file(s) changed on disk since the last map refresh:\n' "$N"
# First 10 only: this is a pointer to the maps, not a replacement for them.
head -n 10 "$MARKER" | while IFS="$(printf '\t')" read -r path event; do
  printf '  %s (%s)\n' "$path" "${event:-change}"
done
[ "$N" -gt 10 ] && printf '  ... and %s more\n' "$((N - 10))"
printf 'unitymap.md and assetmap.md do not reflect these yet; they are rebuilt at the end of this turn.\n'
exit 0
