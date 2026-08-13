#!/bin/bash
# preflight_guard.sh — PreToolUse hook
# Pure bash + coreutils (sha256sum/shasum). Zero external deps (no jq/python).
# Best-effort fail-closed: every internal error emits deny JSON + exit 0.
# (Absolute fail-closed cannot be built with the hooks architecture: if bash
#  itself crashes, the action proceeds — known limit, spec C4/G.)
#
# Checks:
#  0) A write TARGETING .claude/preflight/approved → unconditional deny (token forgery).
#     For Edit/Write/MultiEdit the target = file_path (MENTIONING the path in content is fine);
#     for Bash the whole input is scanned (heuristic — content/command can't be separated).
#  1) Bash heuristic: a write pattern TARGETING a .cs file → deny. Covers
#     redirection, sed -i, tee, mv, cp, and git checkout/restore/apply/stash.
#     ".cs" must end the token (".csv"/".csproj" do not match).
#     (Reading is free: "grep x Foo.cs 2>/dev/null" is not blocked.)
#  2) PowerShell → unconditional deny. PowerShell is a separate tool name from
#     Bash, and this guard is a bash script: on the PowerShell path it cannot
#     verify anything, so it refuses rather than waving the call through.
#  3) Edit/Write/MultiEdit + protected target (.cs/.asmdef/.unity/.prefab/.asset):
#     - .claude/unity-dev.json absent → allow (not a unity-dev project)
#     - current.md missing → deny; approved missing → deny
#     - approved hash != current.md hash → deny (change after approval)
#     - approved session != this session → deny (stale approval)
#     - target not in manifest → deny
#     Violations always deny — no phase softens the gate (there is no
#     prototype mode; a leftover "prototype" phase line changes nothing).

# The reason string is interpolated into JSON by hand, so a double quote or a
# backslash in it — both legal in a POSIX file name — would emit malformed JSON.
# Claude Code cannot parse a decision out of that, and the tool call proceeds:
# a fail-OPEN in a fail-closed gate. Strip those characters and the newlines
# that would break the single-line contract.
emit() { # $1=decision $2=reason
  reason="$(printf '%s' "$2" | tr -d '"\\' | tr '\n\r\t' '   ')"
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"%s","permissionDecisionReason":"%s"}}\n' "$1" "$reason"
  exit 0
}
deny() { emit deny "$1"; }
allow() { exit 0; }

# --- input ---
INPUT="$(cat 2>/dev/null)" || deny "preflight_guard: could not read stdin; blocked for safety."
[ -n "$INPUT" ] || deny "preflight_guard: empty input; blocked for safety."

# Takes the FIRST match (the real tool parameter comes BEFORE any content in
# the input; last-match could be poisoned by a fake key embedded in content).
json_field() {
  printf '%s' "$INPUT" | tr '\n' ' ' \
    | grep -o '"'"$1"'"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -n1 \
    | sed 's/^"[^"]*"[[:space:]]*:[[:space:]]*"\(.*\)"$/\1/'
}

TOOL="$(json_field tool_name)"
SESSION="$(json_field session_id)"
FILE_PATH="$(json_field file_path)"

APPROVED_MSG="The approval token (.claude/preflight/approved) is written only by the UserPromptSubmit hook. Ask the user for a standalone APPROVE message."

case "$TOOL" in
  Bash)
    # 0) command touching approved (heuristic: whole input)
    case "$INPUT" in *"preflight/approved"*) deny "$APPROVED_MSG" ;; esac
    # 1) write patterns TARGETING .cs: redirection target is .cs, or .cs in
    #    sed -i/tee/mv/cp/git-rewrite arguments. ".cs" must end the token, so
    #    .csv/.csproj don't match. Pure reads (grep/cat, 2>/dev/null) pass.
    CS='\.cs([^[:alnum:]_.]|$)'
    if printf '%s' "$INPUT" | grep -Eq '>>?[[:space:]]*[^\" |;&]*'"$CS"'|sed[[:space:]]+-[a-zA-Z]*i[^|;&]*'"$CS"'|tee[[:space:]]+[^|;&]*'"$CS"'|(mv|cp)[[:space:]]+[^|;&]*'"$CS"'|git[[:space:]]+(checkout|restore|apply|stash)[^|;&]*'"$CS"; then
      deny "C# changes are not made via Bash; use the Edit/Write tools (the preflight manifest is verified on those tools)."
    fi
    allow
    ;;
  PowerShell)
    # PowerShell is its own tool name; a `Bash` matcher never covers it. This
    # guard is bash, so on Windows without Git Bash it would not run at all and
    # the gate would fail open silently. unity-dev declares Bash as a hard
    # requirement (see README) and refuses the PowerShell path outright.
    deny "PowerShell is not supported in this project; use the Bash tool. unity-dev requires bash (macOS, Linux, or Windows via WSL)."
    ;;
  Edit|Write|MultiEdit)
    : # continues below
    ;;
  *)
    allow
    ;;
esac

[ -n "$FILE_PATH" ] || deny "preflight_guard: could not read file path; blocked for safety."

# 0) is approved the TARGET (mentioning the path in content is fine; targeting it is blocked)
case "$FILE_PATH" in
  */.claude/preflight/approved|.claude/preflight/approved) deny "$APPROVED_MSG" ;;
esac

case "$FILE_PATH" in
  *.cs|*.asmdef|*.unity|*.prefab|*.asset) : ;;   # protected types
  *) allow ;;
esac

PROJ="${CLAUDE_PROJECT_DIR:-$(json_field cwd)}"
[ -n "$PROJ" ] || PROJ="$(pwd)"
PRE="$PROJ/.claude/preflight"

# --- enforcement marker ---
# .claude/unity-dev.json is COMMITTED to version control. It has to be, because
# this is the one branch that lets a write through untested: whatever marks the
# project must survive a `git clone` and a `git worktree add`.
# It used to be the .claude/preflight/ DIRECTORY, which is gitignored and empty
# — git can carry neither — so a fresh clone got the hooks and the settings but
# not the marker, and every protected write was allowed. Silently.
[ -f "$PROJ/.claude/unity-dev.json" ] || allow   # not a unity-dev project

# --- violations always deny: there is no prototype mode; the Phase line in
#     CLAUDE.md never softens this gate.
fail() { emit deny "$1"; }

CUR="$PRE/current.md"
APR="$PRE/approved"
[ -f "$CUR" ] || fail "No preflight. First write .claude/preflight/current.md in the gates/preflight.md format, then ask the user for APPROVE."
[ -f "$APR" ] || fail "Preflight not approved. Ask the user for a standalone APPROVE message; the hook produces the approval."

hash_file() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  elif command -v shasum   >/dev/null 2>&1; then shasum -a 256 "$1" | cut -d' ' -f1
  else echo ""; fi
}
CUR_HASH="$(hash_file "$CUR")"
[ -n "$CUR_HASH" ] || deny "preflight_guard: no hash tool found (sha256sum/shasum); blocked for safety."

APR_HASH="$(sed -n 1p "$APR" 2>/dev/null)"
APR_SESS="$(sed -n 2p "$APR" 2>/dev/null)"

[ "$APR_HASH" = "$CUR_HASH" ] || fail "Approval lapsed: current.md changed after approval. Ask the user to APPROVE the updated preflight again."
if [ -n "$SESSION" ] && [ -n "$APR_SESS" ] && [ "$SESSION" != "$APR_SESS" ]; then
  fail "Approval belongs to another session. Re-approve the preflight in this session (updating it if needed)."
fi

# --- manifest check ---
REL="${FILE_PATH#"$PROJ"/}"
MATCH="no"
IN_MANIFEST="no"
while IFS= read -r line; do
  case "$line" in
    "## Manifest"*) IN_MANIFEST="yes"; continue ;;
    "## "*) [ "$IN_MANIFEST" = "yes" ] && break ;;
  esac
  if [ "$IN_MANIFEST" = "yes" ]; then
    entry="${line#- }"
    entry="$(printf '%s' "$entry" | sed 's/[[:space:]]*$//')"
    [ -n "$entry" ] || continue
    if [ "$entry" = "$REL" ] || [ "$entry" = "$FILE_PATH" ]; then MATCH="yes"; break; fi
  fi
done < "$CUR"

[ "$MATCH" = "yes" ] || fail "File not in manifest: $REL. Update the preflight and get APPROVE again, or stay within the manifest files."

allow
