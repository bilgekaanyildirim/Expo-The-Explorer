#!/bin/bash
# preflight_approve.sh — UserPromptSubmit hook
# Looks for the APPROVE token STANDING ALONE in the user's message. If found,
# writes the hash of .claude/preflight/current.md + the session id to the
# approved file. Pure bash + coreutils; zero external dependencies.
# The token is exactly APPROVE — no aliases, no localization; changing it
# breaks the documented contract.
#
# Definition of "standing alone" (line-based, over the JSON-escaped prompt):
#   - the whole message is APPROVE, or
#   - APPROVE is a line of its own (leading/trailing whitespace allowed).
# APPROVE inside free text ("before you APPROVE, ask this") does not match.
# On exit 0, stdout is added to context — a short status note is printed.

INPUT="$(cat 2>/dev/null)" || exit 0
[ -n "$INPUT" ] || exit 0

json_field() {
  printf '%s' "$INPUT" | tr '\n' ' ' | sed -n 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n1
}

# Extract the prompt value: the string body after the FIRST "prompt" key (awk
# match gives the first position; last-match could be poisoned by a fake key
# embedded in the text).
PROMPT_RAW="$(printf '%s' "$INPUT" | tr '\n' ' ' | awk 'match($0, /"prompt"[[:space:]]*:[[:space:]]*"/){ print substr($0, RSTART+RLENGTH) }')"

# Cut at the first UNESCAPED quote. Cutting at the first quote of any kind lost
# every message that quoted something before its APPROVE line — fail-closed, but
# it read as the hook being broken. Park the \" pairs on a byte that cannot
# appear in JSON text, cut, then put them back.
ESC=$'\001'
PROMPT_RAW="$(printf '%s' "$PROMPT_RAW" | sed 's/\\"/'"$ESC"'/g')"
PROMPT_RAW="${PROMPT_RAW%%\"*}"
PROMPT_RAW="$(printf '%s' "$PROMPT_RAW" | sed 's/'"$ESC"'/"/g')"

# Turn JSON-escaped \n into real newlines, scan line by line.
TOKEN="no"
while IFS= read -r line; do
  trimmed="$(printf '%s' "$line" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
  if [ "$trimmed" = "APPROVE" ]; then TOKEN="yes"; break; fi
done <<EOF2
$(printf '%s' "$PROMPT_RAW" | sed 's/\\\\n/\n/g; s/\\n/\n/g')
EOF2

[ "$TOKEN" = "yes" ] || exit 0   # no token — pass silently

PROJ="${CLAUDE_PROJECT_DIR:-$(json_field cwd)}"
[ -n "$PROJ" ] || PROJ="$(pwd)"
PRE="$PROJ/.claude/preflight"
CUR="$PRE/current.md"

if [ ! -f "$CUR" ]; then
  echo "[preflight] APPROVE received but $CUR does not exist — no preflight to approve. Write the preflight first."
  exit 0
fi

hash_file() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  elif command -v shasum   >/dev/null 2>&1; then shasum -a 256 "$1" | cut -d' ' -f1
  else echo ""; fi
}
H="$(hash_file "$CUR")"
if [ -z "$H" ]; then
  echo "[preflight] No hash tool found; approval could not be produced."
  exit 0
fi

S="$(json_field session_id)"
mkdir -p "$PRE" 2>/dev/null
{ printf '%s\n' "$H"; printf '%s\n' "$S"; } > "$PRE/approved" 2>/dev/null || {
  echo "[preflight] Could not write the approved file."
  exit 0
}

echo "[preflight] Approval recorded (hash=${H:0:12}, session=${S:0:8}). .cs writes outside the manifest will be blocked."
exit 0
