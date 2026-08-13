#!/bin/bash
# test_enforcement.sh — regression suite for the enforcement and map layers.
#
# Covers, with synthetic hook input and synthetic Unity projects:
#   - preflight_guard.sh   gate states, token forgery, bash bypass heuristics
#   - preflight_approve.sh APPROVE token detection
#   - codemap_guard.sh     the PostToolUse nudge and its boundaries
#   - build_codemap.py     idempotence, STALE / ORPHAN / rename, shards, migration
#   - build_unitymap.py    hierarchy, wiring, missing scripts, variants
#   - build_assetmap.py    ScriptableObject types, runtime load surface
#   - build_index.py       the join, UNMAPPED / UNKNOWN-SYSTEM
#   - check_blueprint.py   drift detection and its exit code
#   - session_context.py   SessionStart payload shape and size
#   - settings.json.template   hook registration and exec form
#
# Pure bash + coreutils + python3 (python3 sections are skipped if absent).
# Exit code: 0 = all pass, 1 = at least one failure.
set -u
DIR="$(cd "$(dirname "$0")" && pwd)"
TPLDIR="$(cd "$DIR/../templates" && pwd)"
GUARD="$DIR/preflight_guard.sh"
APPROVE="$DIR/preflight_approve.sh"
CMGUARD="$DIR/codemap_guard.sh"
CODEMAP="$DIR/build_codemap.py"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
PASS=0; FAIL=0

check_decision() { # $1=name $2=expected(allow|deny|ask) $3=guard output
  local got="allow"
  case "$3" in
    *'"permissionDecision":"deny"'*) got="deny" ;;
    *'"permissionDecision":"ask"'*)  got="ask" ;;
  esac
  if [ "$got" = "$2" ]; then PASS=$((PASS+1)); echo "ok   - $1"
  else FAIL=$((FAIL+1)); echo "FAIL - $1 (expected $2, got $got)"; fi
}
check_true() { # $1=name $2=0/1 (1=pass)
  if [ "$2" = "1" ]; then PASS=$((PASS+1)); echo "ok   - $1"
  else FAIL=$((FAIL+1)); echo "FAIL - $1"; fi
}

hash_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

new_proj() { # $1=phase line value
  PROJ="$TMP/proj-$RANDOM$RANDOM"
  mkdir -p "$PROJ/.claude/preflight"
  printf '{"enforcement":true}\n' > "$PROJ/.claude/unity-dev.json"   # the marker
  printf '# Game\n\n## Phase\n\n- **Phase:** %s\n' "$1" > "$PROJ/CLAUDE.md"
}
write_preflight() {
  cat > "$PROJ/.claude/preflight/current.md" <<'EOF'
# Preflight: test

- Task: test

## Manifest
- Assets/Scripts/Gameplay/Foo.cs
- Assets/Data/Thing.asset
EOF
}
approve_current() { # $1=session id
  { hash_of "$PROJ/.claude/preflight/current.md"; printf '%s\n' "$1"; } \
    > "$PROJ/.claude/preflight/approved"
}
run_guard() { printf '%s' "$1" | CLAUDE_PROJECT_DIR="$PROJ" "$GUARD" 2>/dev/null; }
wjson() { printf '{"session_id":"S1","tool_name":"Write","cwd":"%s","tool_input":{"file_path":"%s","content":"x"}}' "$PROJ" "$1"; }
bjson() { printf '{"session_id":"S1","tool_name":"Bash","cwd":"%s","tool_input":{"command":"%s"}}' "$PROJ" "$1"; }
run_approve() { printf '%s' "$1" | CLAUDE_PROJECT_DIR="$PROJ" "$APPROVE" 2>/dev/null; }
ajson() { printf '{"session_id":"S9","cwd":"%s","prompt":"%s"}' "$PROJ" "$1"; }

echo "== guard: gate states =="
new_proj production
check_decision "non-protected file passes untouched" allow \
  "$(run_guard "$(wjson "$PROJ/README.md")")"

PROJ="$TMP/no-enforcement"; mkdir -p "$PROJ/.claude"
check_decision "no .claude/unity-dev.json -> not a unity-dev project, allow" allow \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"

# The regression that shipped in v2.0.0: the marker used to be the gitignored,
# always-empty .claude/preflight/ directory, which git cannot carry. A clone
# arrived with the hooks and the settings but without the marker, and the gate
# allowed every protected write. The marker is now a committed file, so a
# checkout that has it but no runtime state must FAIL CLOSED.
PROJ="$TMP/fresh-clone"; mkdir -p "$PROJ/.claude/hooks"
printf '{"enforcement":true}\n' > "$PROJ/.claude/unity-dev.json"
check_decision "fresh clone: marker present, no preflight/ dir -> deny" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"

new_proj production
check_decision "no current.md -> deny" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"

write_preflight
check_decision "current.md but no approval -> deny" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"

approve_current S1
echo "- extra line" >> "$PROJ/.claude/preflight/current.md"
check_decision "current.md changed after approval (hash lapse) -> deny" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"

write_preflight; approve_current OTHER-SESSION
check_decision "approval from another session -> deny" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"

approve_current S1
check_decision "approved + file in manifest -> allow" allow \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"
check_decision "approved + .asset in manifest -> allow" allow \
  "$(run_guard "$(wjson "$PROJ/Assets/Data/Thing.asset")")"
check_decision "approved but file outside manifest -> deny" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Bar.cs")")"
check_decision "protected .prefab outside manifest -> deny" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Prefabs/Enemy.prefab")")"

echo "== guard: no phase softens the gate =="
new_proj prototype; write_preflight
check_decision "leftover 'prototype' phase line still denies" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"
new_proj prototip; write_preflight
check_decision "Turkish 'prototip' phase line still denies" deny \
  "$(run_guard "$(wjson "$PROJ/Assets/Scripts/Gameplay/Foo.cs")")"

echo "== guard: token forgery =="
new_proj production
check_decision "Write targeting preflight/approved -> unconditional deny" deny \
  "$(run_guard "$(wjson "$PROJ/.claude/preflight/approved")")"
check_decision "Bash touching preflight/approved -> unconditional deny" deny \
  "$(run_guard "$(bjson "echo x > .claude/preflight/approved")")"

echo "== guard: bash bypass heuristics =="
new_proj production
check_decision "bash: redirect into .cs -> deny" deny \
  "$(run_guard "$(bjson "echo class > Assets/Foo.cs")")"
check_decision "bash: sed -i on .cs -> deny" deny \
  "$(run_guard "$(bjson "sed -i s/a/b/ Assets/Foo.cs")")"
check_decision "bash: tee into .cs -> deny" deny \
  "$(run_guard "$(bjson "tee Assets/Foo.cs")")"
check_decision "bash: mv onto .cs -> deny" deny \
  "$(run_guard "$(bjson "mv /tmp/x Assets/Foo.cs")")"
check_decision "bash: cp onto .cs -> deny" deny \
  "$(run_guard "$(bjson "cp /tmp/x Assets/Foo.cs")")"
check_decision "bash: git checkout -- .cs -> deny" deny \
  "$(run_guard "$(bjson "git checkout -- Assets/Foo.cs")")"
check_decision "bash: git restore .cs -> deny" deny \
  "$(run_guard "$(bjson "git restore Assets/Foo.cs")")"
check_decision "bash: mv of a .csv is NOT blocked (boundary)" allow \
  "$(run_guard "$(bjson "mv data.csv archive/data.csv")")"
check_decision "bash: cp of a .csproj is NOT blocked (boundary)" allow \
  "$(run_guard "$(bjson "cp Game.csproj /tmp/")")"
check_decision "bash: grep read of .cs stays allowed" allow \
  "$(run_guard "$(bjson "grep -n Update Assets/Foo.cs 2>/dev/null")")"
check_decision "bash: cat read of .cs stays allowed" allow \
  "$(run_guard "$(bjson "cat Assets/Foo.cs")")"

echo "== guard: PowerShell path and JSON safety =="
new_proj production; write_preflight; approve_current S1
# PowerShell is a separate tool name; a "Bash" matcher never covers it, and this
# guard is bash, so it cannot verify anything on that path. Refuse, don't wave through.
check_decision "PowerShell write to .cs -> deny" deny \
  "$(run_guard "$(printf '{"session_id":"S1","tool_name":"PowerShell","cwd":"%s","tool_input":{"command":"Set-Content Assets/Foo.cs x"}}' "$PROJ")")"
check_decision "PowerShell read command -> deny too (no bash, no verification)" deny \
  "$(run_guard "$(printf '{"session_id":"S1","tool_name":"PowerShell","cwd":"%s","tool_input":{"command":"Get-Content README.md"}}' "$PROJ")")"

# A path holding a quote or a backslash is legal on POSIX. The reason string is
# hand-interpolated into JSON, so an unsanitised one produced malformed output,
# Claude Code parsed no decision out of it, and the write went through: a
# fail-OPEN inside the fail-closed gate.
ODD="$(run_guard "$(printf '{"session_id":"S1","tool_name":"Write","cwd":"%s","tool_input":{"file_path":"%s/Assets/Scripts/Ba\\\\d.cs","content":"x"}}' "$PROJ" "$PROJ")")"
check_decision "path with a backslash still denies" deny "$ODD"
json_safe() { # 1 when the reason carries no character that would break the envelope
  case "$1" in
    *\\*) echo 0 ;;
    *) case "$1" in *'"}}'*) echo 1 ;; *) echo 0 ;; esac ;;
  esac
}
check_true "guard output stays valid JSON for an odd path" "$(json_safe "$ODD")"

echo "== approve: token detection =="
new_proj production; write_preflight
run_approve "$(ajson "APPROVE")" >/dev/null
check_true "standalone APPROVE writes the approved file" \
  "$([ -f "$PROJ/.claude/preflight/approved" ] && echo 1 || echo 0)"
check_true "approved carries hash + session id" \
  "$([ "$(sed -n 1p "$PROJ/.claude/preflight/approved")" = "$(hash_of "$PROJ/.claude/preflight/current.md")" ] \
     && [ "$(sed -n 2p "$PROJ/.claude/preflight/approved")" = "S9" ] && echo 1 || echo 0)"

new_proj production; write_preflight
run_approve "$(ajson "ONAY")" >/dev/null
check_true "a word that is not the exact token must NOT approve" \
  "$([ ! -f "$PROJ/.claude/preflight/approved" ] && echo 1 || echo 0)"

# The body used to be cut at the first quote of any kind, so a message that
# quoted anything before its APPROVE line silently failed to approve.
new_proj production; write_preflight
run_approve "$(ajson 'Use the \"fast\" path.\nAPPROVE')" >/dev/null
check_true "a quoted phrase before the token does not swallow the APPROVE line" \
  "$([ -f "$PROJ/.claude/preflight/approved" ] && echo 1 || echo 0)"

new_proj production; write_preflight
run_approve "$(ajson "please APPROVE this when ready")" >/dev/null
check_true "APPROVE inside free text does NOT approve" \
  "$([ ! -f "$PROJ/.claude/preflight/approved" ] && echo 1 || echo 0)"

new_proj production; write_preflight
run_approve "$(ajson "looks good\nAPPROVE\nthanks")" >/dev/null
check_true "APPROVE on its own line in a multi-line message approves" \
  "$([ -f "$PROJ/.claude/preflight/approved" ] && echo 1 || echo 0)"

new_proj production
OUT="$(run_approve "$(ajson "APPROVE")")"
NOPRE=0
if printf '%s' "$OUT" | grep -q "no preflight" && [ ! -f "$PROJ/.claude/preflight/approved" ]; then NOPRE=1; fi
check_true "APPROVE without a preflight reports, writes nothing" "$NOPRE"

echo "== postflight nudge: codemap_guard (PostToolUse) =="
new_proj production
mkdir -p "$PROJ/.claude"
cat > "$PROJ/.claude/codemap-gameplay.md" <<'EOF'
<!-- stamp: no-git 2026-01-01 status: DEGRADED 0 stale, 0 orphan, 1 missing-role -->
Assets/Scripts/Gameplay/Done.cs | tidy role here | sys: Combat | api: Tick() | dep: - | used: - | crit: K2 | note: - | h:aaaaaaaa
Assets/Scripts/Gameplay/Half.cs | MISSING-role | sys: ? | api: Tick() | dep?: - | used: ? | crit: ? | note: auto-added; AI must complete | h:bbbbbbbb
ORPHAN Assets/Scripts/Gameplay/Gone.cs | gone role | sys: Combat | api: - | dep: - | used: - | crit: K3 | note: - | h:cccccccc
EOF
run_cmguard() { printf '%s' "$1" | CLAUDE_PROJECT_DIR="$PROJ" "$CMGUARD" 2>/dev/null; echo "rc=$?"; }
pjson() { printf '{"session_id":"S1","tool_name":"Write","cwd":"%s","tool_input":{"file_path":"%s","content":"x"}}' "$PROJ" "$1"; }

check_true "complete codemap line -> no nudge (rc 0)" \
  "$([ "$(run_cmguard "$(pjson "$PROJ/Assets/Scripts/Gameplay/Done.cs")")" = "rc=0" ] && echo 1 || echo 0)"
check_true "unfinished line -> nudge (rc 2)" \
  "$([ "$(run_cmguard "$(pjson "$PROJ/Assets/Scripts/Gameplay/Half.cs")")" = "rc=2" ] && echo 1 || echo 0)"
check_true "ORPHAN line -> nudge (rc 2)" \
  "$([ "$(run_cmguard "$(pjson "$PROJ/Assets/Scripts/Gameplay/Gone.cs")")" = "rc=2" ] && echo 1 || echo 0)"
check_true "no line at all -> nudge (rc 2)" \
  "$([ "$(run_cmguard "$(pjson "$PROJ/Assets/Scripts/Gameplay/New.cs")")" = "rc=2" ] && echo 1 || echo 0)"
check_true ".csv write -> silent (rc 0, boundary)" \
  "$([ "$(run_cmguard "$(pjson "$PROJ/Assets/Data/table.csv")")" = "rc=0" ] && echo 1 || echo 0)"
check_true ".csproj write -> silent (rc 0, boundary)" \
  "$([ "$(run_cmguard "$(pjson "$PROJ/Game.csproj")")" = "rc=0" ] && echo 1 || echo 0)"
PROJ="$TMP/no-claude-dir"; mkdir -p "$PROJ"
check_true "project without .claude -> nudge stays silent" \
  "$([ "$(run_cmguard "$(pjson "$PROJ/Assets/Foo.cs")")" = "rc=0" ] && echo 1 || echo 0)"

echo "== templates: hook registration =="
if command -v python3 >/dev/null 2>&1; then
  check_true "settings.json.template is valid JSON" \
    "$(python3 -c "import json;json.load(open('$TPLDIR/settings.json.template'))" >/dev/null 2>&1 && echo 1 || echo 0)"
  check_true "shards.json.template is valid JSON" \
    "$(python3 -c "import json;json.load(open('$TPLDIR/shards.json.template'))" >/dev/null 2>&1 && echo 1 || echo 0)"
  check_true "all seven hook events registered" \
    "$(python3 - <<PY
import json
h = json.load(open("$TPLDIR/settings.json.template"))["hooks"]
need = {"SessionStart","PreToolUse","PostToolUse","UserPromptSubmit",
        "SubagentStart","FileChanged","Stop"}
print(1 if need <= set(h) else 0)
PY
)"
  # PowerShell is a separate tool name; a bare "Bash" matcher leaves that path
  # ungated, and the guard is a bash script that cannot run there at all.
  check_true "the Bash gate matcher also covers PowerShell" \
    "$(python3 - <<PY
import json
h = json.load(open("$TPLDIR/settings.json.template"))["hooks"]
print(1 if any(e.get("matcher") == "Bash|PowerShell" for e in h["PreToolUse"]) else 0)
PY
)"
  check_true "worktree-isolated subagents are denied" \
    "$(python3 - <<PY
import json
s = json.load(open("$TPLDIR/settings.json.template"))
print(1 if "Agent(isolation:worktree)" in s.get("permissions", {}).get("deny", []) else 0)
PY
)"
  # SubagentStart must stay matcher-less: a matcher would have to name agent
  # types, and the built-in Explore/Plan are exactly the ones that need it.
  check_true "SubagentStart carries no matcher (covers every agent type)" \
    "$(python3 - <<PY
import json
h = json.load(open("$TPLDIR/settings.json.template"))["hooks"]
print(1 if all("matcher" not in e for e in h["SubagentStart"]) else 0)
PY
)"
  check_true "every command hook uses exec form (args present)" \
    "$(python3 - <<PY
import json
h = json.load(open("$TPLDIR/settings.json.template"))["hooks"]
ok = all("args" in hk for grp in h.values() for e in grp for hk in e["hooks"] if hk.get("type") == "command")
print(1 if ok else 0)
PY
)"
  check_true "the preflight gate carries NO 'if' filter (fail-open guard)" \
    "$(python3 - <<PY
import json
h = json.load(open("$TPLDIR/settings.json.template"))["hooks"]
guarded = [hk for e in h["PreToolUse"] for hk in e["hooks"]]
print(1 if guarded and not any("if" in hk for hk in guarded) else 0)
PY
)"
else
  echo "skip - python3 not found; template tests skipped"
fi

echo "== codemap: integrity =="
if command -v python3 >/dev/null 2>&1; then
  cm_proj() {
    PROJ="$TMP/cm-$RANDOM$RANDOM"
    mkdir -p "$PROJ/Assets/Scripts/Gameplay" "$PROJ/Assets/Scripts/UI" "$PROJ/Assets/Editor"
    printf 'public class Foo { public void Tick(int n) { var b = new Bar(); } }\n' > "$PROJ/Assets/Scripts/Gameplay/Foo.cs"
    printf 'public class Bar { public void Go() {} }\n' > "$PROJ/Assets/Scripts/Gameplay/Bar.cs"
    printf 'public class Panel { public void Draw() {} }\n' > "$PROJ/Assets/Scripts/UI/Panel.cs"
    printf 'public class Tool { public static void Run() {} }\n' > "$PROJ/Assets/Editor/Tool.cs"
    printf '{"name":"Game.Gameplay","references":["Game.Core"]}\n' > "$PROJ/Assets/Scripts/Gameplay/Game.Gameplay.asmdef"
    python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  }

  cm_proj
  cp "$PROJ/.claude/codemap-gameplay.md" "$TMP/cm-first"
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  check_true "second run produces no diff (idempotent)" \
    "$(cmp -s "$TMP/cm-first" "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"
  check_true "missing line tagged MISSING-role" \
    "$(grep -q 'MISSING-role' "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"
  check_true "new line carries sys: ? and a content hash" \
    "$(grep -q 'sys: ?' "$PROJ/.claude/codemap-gameplay.md" && grep -qE 'h:[0-9a-f]{8}' "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"
  check_true "Editor/ script lands in the editor shard, not core" \
    "$(grep -q 'Assets/Editor/Tool.cs' "$PROJ/.claude/codemap-editor.md" 2>/dev/null \
       && ! grep -q 'Assets/Editor/Tool.cs' "$PROJ/.claude/codemap-core.md" 2>/dev/null && echo 1 || echo 0)"
  check_true ".asmdef is mapped with its assembly name" \
    "$(grep -q 'asmdef Game.Gameplay' "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"
  check_true "dep?: draft names a project type actually used (Bar)" \
    "$(grep 'Foo.cs' "$PROJ/.claude/codemap-gameplay.md" | grep -q 'dep?: Bar' && echo 1 || echo 0)"
  check_true "stamp reports DEGRADED while lines are unfinished" \
    "$(head -1 "$PROJ/.claude/codemap-gameplay.md" | grep -q 'status: DEGRADED' && echo 1 || echo 0)"

  # STALE: content changes after the line was written
  printf 'public class Panel { public void Draw() {} public void Hide() {} }\n' > "$PROJ/Assets/Scripts/UI/Panel.cs"
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  check_true "changed file -> line marked STALE" \
    "$(grep -q '^STALE Assets/Scripts/UI/Panel.cs' "$PROJ/.claude/codemap-ui.md" && echo 1 || echo 0)"
  # AI clears the marker; the tool must not re-add it while content is unchanged
  sed 's/^STALE //' "$PROJ/.claude/codemap-ui.md" > "$TMP/ui-cleared" && cp "$TMP/ui-cleared" "$PROJ/.claude/codemap-ui.md"
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  check_true "cleared marker stays cleared (no mark/clear loop)" \
    "$(! grep -q '^STALE' "$PROJ/.claude/codemap-ui.md" && echo 1 || echo 0)"

  # ORPHAN: file removed, line kept and flagged
  rm "$PROJ/Assets/Scripts/Gameplay/Bar.cs"
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  check_true "deleted file -> line marked ORPHAN, never removed" \
    "$(grep -q '^ORPHAN Assets/Scripts/Gameplay/Bar.cs' "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"
  check_true "ORPHAN state is idempotent" \
    "$(cp "$PROJ/.claude/codemap-gameplay.md" "$TMP/orph-1"; python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1; \
       cmp -s "$TMP/orph-1" "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"
  # the file comes back unchanged -> the marker clears itself
  printf 'public class Bar { public void Go() {} }\n' > "$PROJ/Assets/Scripts/Gameplay/Bar.cs"
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  check_true "restored file -> ORPHAN marker cleared automatically" \
    "$(! grep -q '^ORPHAN Assets/Scripts/Gameplay/Bar.cs' "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"

  # rename: same public API under a new path
  cm_proj
  mv "$PROJ/Assets/Scripts/Gameplay/Bar.cs" "$PROJ/Assets/Scripts/Gameplay/Baz.cs"
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  check_true "rename produces a 'possible rename/move' note on the new line" \
    "$(grep 'Baz.cs' "$PROJ/.claude/codemap-gameplay.md" | grep -q 'possible rename/move of Assets/Scripts/Gameplay/Bar.cs' && echo 1 || echo 0)"

  # legacy v1 line (no sys:, no h:) must be migrated in place, not rewritten
  PROJ="$TMP/cm-legacy"; mkdir -p "$PROJ/Assets/Scripts/Gameplay" "$PROJ/.claude"
  printf 'public class Legacy { public void Tick() {} }\n' > "$PROJ/Assets/Scripts/Gameplay/Legacy.cs"
  printf '<!-- stamp: abc123 2026-01-01 -->\nAssets/Scripts/Gameplay/Legacy.cs | tick driver | api: Tick() | dep: - | used: - | crit: K2 | note: hand written\n' \
    > "$PROJ/.claude/codemap-gameplay.md"
  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  check_true "legacy line gains sys: ? and h: without losing its semantics" \
    "$(grep 'Legacy.cs' "$PROJ/.claude/codemap-gameplay.md" | grep -q 'tick driver' \
       && grep 'Legacy.cs' "$PROJ/.claude/codemap-gameplay.md" | grep -q 'sys: ?' \
       && grep 'Legacy.cs' "$PROJ/.claude/codemap-gameplay.md" | grep -qE 'h:[0-9a-f]{8}' \
       && grep 'Legacy.cs' "$PROJ/.claude/codemap-gameplay.md" | grep -q 'hand written' && echo 1 || echo 0)"
  check_true "migration does not accuse an untouched legacy line of being STALE" \
    "$(! grep -q '^STALE' "$PROJ/.claude/codemap-gameplay.md" && echo 1 || echo 0)"
else
  echo "skip - python3 not found; codemap tests skipped"
fi

echo "== unity maps: scene, asset, index, blueprint =="
if command -v python3 >/dev/null 2>&1; then
  PROJ="$TMP/uproj"
  mkdir -p "$PROJ/Assets/Scripts/Gameplay" "$PROJ/Assets/Scenes" "$PROJ/Assets/Prefabs" \
           "$PROJ/Assets/Data/Items" "$PROJ/Assets/Resources" "$PROJ/.claude"
  printf 'public class EnemyAI { public void Tick() {} }\n' > "$PROJ/Assets/Scripts/Gameplay/EnemyAI.cs"
  printf 'fileFormatVersion: 2\nguid: 22222222222222222222222222222222\n' > "$PROJ/Assets/Scripts/Gameplay/EnemyAI.cs.meta"
  printf 'public class ItemDef { }\n' > "$PROJ/Assets/Scripts/Gameplay/ItemDef.cs"
  printf 'fileFormatVersion: 2\nguid: 33333333333333333333333333333333\n' > "$PROJ/Assets/Scripts/Gameplay/ItemDef.cs.meta"
  cat > "$PROJ/Assets/Scenes/Boot.unity" <<'EOF'
%YAML 1.1
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
--- !u!1 &100
GameObject:
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  - component: {fileID: 103}
  m_Name: Systems
  m_IsActive: 1
--- !u!4 &101
Transform:
  m_GameObject: {fileID: 100}
  m_Children:
  - {fileID: 201}
  m_Father: {fileID: 0}
--- !u!114 &102
MonoBehaviour:
  m_GameObject: {fileID: 100}
  m_Script: {fileID: 11500000, guid: 22222222222222222222222222222222, type: 3}
  target: {fileID: 0}
  hud: {fileID: 202}
--- !u!114 &103
MonoBehaviour:
  m_GameObject: {fileID: 100}
  m_Script: {fileID: 0}
--- !u!1 &200
GameObject:
  m_Component:
  - component: {fileID: 201}
  m_Name: Audio
  m_IsActive: 1
--- !u!4 &201
Transform:
  m_GameObject: {fileID: 200}
  m_Children: []
  m_Father: {fileID: 101}
--- !u!21 &300
Material:
  m_Name: NotAGameObject
EOF
  cat > "$PROJ/Assets/Prefabs/Enemy.prefab" <<'EOF'
%YAML 1.1
--- !u!1 &500
GameObject:
  m_Component:
  - component: {fileID: 501}
  - component: {fileID: 502}
  m_Name: Enemy
  m_IsActive: 1
--- !u!4 &501
Transform:
  m_GameObject: {fileID: 500}
  m_Children: []
  m_Father: {fileID: 0}
--- !u!114 &502
MonoBehaviour:
  m_GameObject: {fileID: 500}
  m_Script: {fileID: 11500000, guid: 22222222222222222222222222222222, type: 3}
  target: {fileID: 0}
EOF
  printf 'fileFormatVersion: 2\nguid: 44444444444444444444444444444444\n' > "$PROJ/Assets/Prefabs/Enemy.prefab.meta"
  cat > "$PROJ/Assets/Prefabs/EnemyGrunt.prefab" <<'EOF'
%YAML 1.1
--- !u!1001 &1000
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: 500, guid: 44444444444444444444444444444444, type: 3}
      propertyPath: m_Name
      value: EnemyGrunt
      objectReference: {fileID: 0}
  m_SourcePrefab: {fileID: 100100000, guid: 44444444444444444444444444444444, type: 3}
EOF
  cat > "$PROJ/Assets/Data/Items/Sword.asset" <<'EOF'
%YAML 1.1
--- !u!114 &11400000
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: 33333333333333333333333333333333, type: 3}
  m_Name: Sword
EOF
  printf 'x\n' > "$PROJ/Assets/Resources/config.txt"

  python3 "$DIR/build_unitymap.py" "$PROJ" --quiet >/dev/null 2>&1
  UM="$PROJ/.claude/unitymap.md"
  check_true "unitymap: hierarchy nests Audio under Systems" \
    "$(grep -q '^  - Audio' "$UM" && echo 1 || echo 0)"
  check_true "unitymap: components are listed per object" \
    "$(grep -q 'Systems.*\[Transform, EnemyAI' "$UM" && echo 1 || echo 0)"
  check_true "unitymap: unassigned serialized slot reported as NULL" \
    "$(grep -q 'target=NULL' "$UM" && echo 1 || echo 0)"
  check_true "unitymap: assigned slot reported as set" \
    "$(grep -q 'hud=set' "$UM" && echo 1 || echo 0)"
  check_true "unitymap: missing script detected" \
    "$(grep -q 'MISSING SCRIPT | Assets/Scenes/Boot.unity' "$UM" && echo 1 || echo 0)"
  check_true "unitymap: prefab variant relationship resolved" \
    "$(grep -q 'EnemyGrunt.prefab   variant-of: Assets/Prefabs/Enemy.prefab' "$UM" && echo 1 || echo 0)"
  check_true "unitymap: a Material's m_Name is not counted as a GameObject" \
    "$(! grep -q 'NotAGameObject' "$UM" && grep -q 'Boot.unity   (2 object(s))' "$UM" && echo 1 || echo 0)"
  check_true "unitymap: script index cross-links to the codemap path" \
    "$(grep -q 'EnemyAI | Assets/Scripts/Gameplay/EnemyAI.cs' "$UM" && echo 1 || echo 0)"
  cp "$UM" "$TMP/um-1"
  python3 "$DIR/build_unitymap.py" "$PROJ" --quiet --if-stale >/dev/null 2>&1
  check_true "unitymap: --if-stale leaves an up-to-date file untouched" \
    "$(cmp -s "$TMP/um-1" "$UM" && echo 1 || echo 0)"
  printf '>> note: kept by hand\n' >> "$UM"
  python3 "$DIR/build_unitymap.py" "$PROJ" --quiet >/dev/null 2>&1
  check_true "unitymap: '>> note:' lines survive regeneration" \
    "$(grep -q '>> note: kept by hand' "$UM" && echo 1 || echo 0)"

  python3 "$DIR/build_assetmap.py" "$PROJ" --quiet >/dev/null 2>&1
  AM="$PROJ/.claude/assetmap.md"
  check_true "assetmap: .asset listed with its ScriptableObject type" \
    "$(grep -q 'Sword.asset | type: ItemDef' "$AM" && echo 1 || echo 0)"
  check_true "assetmap: Resources/ flagged as runtime load surface" \
    "$(grep -q 'Assets/Resources | 1 file(s)' "$AM" && echo 1 || echo 0)"

  python3 "$CODEMAP" "$PROJ" >/dev/null 2>&1
  cat > "$PROJ/.claude/blueprint.md" <<'EOF'
# blueprint

## Systems and dependencies

- Combat — enemies — depends on: Loot
- Loot — drops — depends on: Combat
- Audio — sound — depends on: -

## Scene inventory

- Boot — persistent — single — Systems

## Prefab inventory

- Enemy — Combat — variant-of: - — spawned by Spawner
- (none yet)
- <PlaceholderFromTemplate — System — variant-of: - — spawner>

## Folder layout

```
Assets/
  Scripts/Gameplay/    ← gameplay
  Scenes/              ← scenes
  Prefabs/             ← prefabs
  Data/<Type>/         ← data
```
EOF
  sed -i.bak 's#\(Assets/Scripts/Gameplay/EnemyAI.cs .*\)sys: ?#\1sys: Combat#' "$PROJ/.claude/codemap-gameplay.md"
  sed -i.bak 's#\(Assets/Scripts/Gameplay/ItemDef.cs .*\)sys: ?#\1sys: Ghost#' "$PROJ/.claude/codemap-gameplay.md"
  rm -f "$PROJ/.claude/codemap-gameplay.md.bak"
  python3 "$DIR/build_index.py" "$PROJ" --quiet >/dev/null 2>&1
  IX="$PROJ/.claude/index.md"
  check_true "index: system with code resolves to its shard and entry file" \
    "$(grep -q '| Combat | gameplay | Assets/Scripts/Gameplay/EnemyAI.cs' "$IX" && echo 1 || echo 0)"
  check_true "index: blueprint system with no code is UNMAPPED" \
    "$(grep '| Audio |' "$IX" | grep -q 'UNMAPPED' && echo 1 || echo 0)"
  check_true "index: sys: value with no blueprint line is UNKNOWN-SYSTEM" \
    "$(grep -q 'UNKNOWN-SYSTEM `Ghost`' "$IX" && echo 1 || echo 0)"
  check_true "index: prefab column comes from the blueprint owning system" \
    "$(grep '| Combat |' "$IX" | grep -q 'Enemy' && echo 1 || echo 0)"

  python3 "$DIR/check_blueprint.py" "$PROJ" > "$TMP/bp.txt" 2>&1; BPRC=$?
  check_true "check_blueprint: exits 1 when there is an ERROR" "$([ "$BPRC" = "1" ] && echo 1 || echo 0)"
  check_true "check_blueprint: dependency cycle reported" \
    "$(grep -q 'dependency cycle between systems' "$TMP/bp.txt" && echo 1 || echo 0)"
  check_true "check_blueprint: prefab on disk missing from the inventory reported" \
    "$(grep -q 'EnemyGrunt.prefab' "$TMP/bp.txt" && echo 1 || echo 0)"
  check_true "check_blueprint: undeclared folder on disk reported" \
    "$(grep -q 'Assets/Resources/' "$TMP/bp.txt" && echo 1 || echo 0)"
  check_true "check_blueprint: sys: with no blueprint line is an ERROR" \
    "$(grep -q 'codemap `sys: Ghost` has no system line' "$TMP/bp.txt" && echo 1 || echo 0)"
  check_true "check_blueprint: template stubs and '(none yet)' are not inventory entries" \
    "$(! grep -qi 'none yet\|PlaceholderFromTemplate' "$TMP/bp.txt" && echo 1 || echo 0)"
  # Unbounded output is the one path in this repo that can outgrow what a
  # postflight can quote. The cap keeps errors (they sort first) and says so.
  python3 "$DIR/check_blueprint.py" "$PROJ" --max-findings 1 > "$TMP/bp1.txt" 2>&1
  check_true "check_blueprint: --max-findings caps the list and announces the drop" \
    "$(grep -q 'more finding(s) not shown' "$TMP/bp1.txt" \
       && grep -q 'error(s)' "$TMP/bp1.txt" \
       && [ "$(grep -c '^\[blueprint\] ERROR' "$TMP/bp1.txt")" -ge 1 ] && echo 1 || echo 0)"

  echo '{"cwd":"'"$PROJ"'"}' | python3 "$DIR/session_context.py" "$PROJ" > "$TMP/sc.json" 2>/dev/null
  check_true "session_context: emits valid SessionStart JSON under 2 KB" \
    "$(python3 - <<PY
import json
d = json.load(open("$TMP/sc.json"))
t = d["hookSpecificOutput"]["additionalContext"]
print(1 if d["hookSpecificOutput"]["hookEventName"] == "SessionStart" and 0 < len(t) <= 2000 else 0)
PY
)"
  check_true "session_context: reports the degraded codemap it found" \
    "$(grep -q 'DEGRADED' "$TMP/sc.json" && echo 1 || echo 0)"
  # watchPaths is what makes FileChanged fire for scenes and prefabs at all.
  check_true "session_context: watchPaths lists absolute scene/prefab/asset paths" \
    "$(python3 - <<PY
import json
w = json.load(open("$TMP/sc.json"))["hookSpecificOutput"].get("watchPaths", [])
ok = (len(w) > 0 and len(w) <= 500
      and all(p.startswith("/") for p in w)
      and all(p.endswith((".unity", ".prefab", ".asset")) for p in w))
print(1 if ok else 0)
PY
)"

  echo "== subagent boundary and editor drift =="
  echo '{"cwd":"'"$PROJ"'","agent_type":"Explore"}' \
    | python3 "$DIR/subagent_context.py" "$PROJ" > "$TMP/sa.json" 2>/dev/null
  check_true "subagent_context: valid SubagentStart JSON, capped, states the map order" \
    "$(python3 - <<PY
import json
d = json.load(open("$TMP/sa.json"))["hookSpecificOutput"]
t = d["additionalContext"]
ok = (d["hookEventName"] == "SubagentStart" and 0 < len(t) <= 3000
      and "index.md" in t and "codemap-" in t and "blueprint.md" in t)
print(1 if ok else 0)
PY
)"
  # Hook text framed as out-of-band orders trips the prompt-injection defences
  # and gets surfaced to the user instead of used. Keep it in the indicative.
  check_true "subagent_context: contract reads as facts, not as orders" \
    "$(python3 - <<PY
import json, re
t = json.load(open("$TMP/sa.json"))["hookSpecificOutput"]["additionalContext"]
bad = re.search(r"(?im)^\s*(you must|never |always |do not |don't )", t)
print(0 if bad else 1)
PY
)"

  DRIFT="$PROJ/.claude/map-drift"
  rm -f "$DRIFT"
  printf '{"cwd":"%s","file_path":"%s/Assets/Scenes/Main.unity","event":"change"}' "$PROJ" "$PROJ" \
    | python3 "$DIR/mark_map_stale.py" "$PROJ" >/dev/null 2>&1
  printf '{"cwd":"%s","file_path":"%s/Assets/Scenes/Main.unity","event":"change"}' "$PROJ" "$PROJ" \
    | python3 "$DIR/mark_map_stale.py" "$PROJ" >/dev/null 2>&1
  check_true "mark_map_stale: records the changed path, de-duplicated" \
    "$([ "$(wc -l < "$DRIFT" | tr -d ' ')" = "1" ] \
       && grep -q 'Assets/Scenes/Main.unity' "$DRIFT" && echo 1 || echo 0)"
  printf '{"cwd":"%s","file_path":"/elsewhere/Other.unity","event":"change"}' "$PROJ" \
    | python3 "$DIR/mark_map_stale.py" "$PROJ" >/dev/null 2>&1
  check_true "mark_map_stale: ignores a path outside the project" \
    "$([ "$(wc -l < "$DRIFT" | tr -d ' ')" = "1" ] && echo 1 || echo 0)"

  printf '{"cwd":"%s","prompt":"go on"}' "$PROJ" \
    | CLAUDE_PROJECT_DIR="$PROJ" bash "$DIR/map_drift_notice.sh" > "$TMP/drift.txt" 2>/dev/null
  check_true "map_drift_notice: reports the drift to the next prompt" \
    "$(grep -q 'maps' "$TMP/drift.txt" && grep -q 'Main.unity' "$TMP/drift.txt" && echo 1 || echo 0)"

  python3 "$DIR/refresh_maps.py" "$PROJ" >/dev/null 2>&1
  check_true "refresh_maps: clears the drift marker once the maps caught up" \
    "$([ ! -f "$DRIFT" ] && echo 1 || echo 0)"
  QUIET="$(printf '{"cwd":"%s","prompt":"go on"}' "$PROJ" | CLAUDE_PROJECT_DIR="$PROJ" bash "$DIR/map_drift_notice.sh" 2>/dev/null)"
  check_true "map_drift_notice: silent when there is no drift" \
    "$([ -z "$QUIET" ] && echo 1 || echo 0)"

  PROJ="$TMP/not-a-unity-dev-project"; mkdir -p "$PROJ"
  echo '{"cwd":"'"$PROJ"'"}' | python3 "$DIR/session_context.py" "$PROJ" > "$TMP/sc2.txt" 2>/dev/null
  check_true "session_context: silent outside a unity-dev project" \
    "$([ ! -s "$TMP/sc2.txt" ] && echo 1 || echo 0)"
  echo '{"cwd":"'"$PROJ"'"}' | python3 "$DIR/subagent_context.py" "$PROJ" > "$TMP/sa2.txt" 2>/dev/null
  check_true "subagent_context: silent outside a unity-dev project" \
    "$([ ! -s "$TMP/sa2.txt" ] && echo 1 || echo 0)"

  echo "== install: marker, agent, and enforcement preconditions =="
  check_true "templates/agents/Explore.md exists and declares name: Explore" \
    "$(grep -q '^name: Explore$' "$TPLDIR/agents/Explore.md" && echo 1 || echo 0)"
  check_true "unity-dev.json.template is valid JSON with enforcement: true" \
    "$(python3 -c "import json;import sys;sys.exit(0 if json.load(open('$TPLDIR/unity-dev.json.template')).get('enforcement') is True else 1)" >/dev/null 2>&1 && echo 1 || echo 0)"
  # The marker must be committable; the gitignore template must not swallow it.
  check_true "gitignore template does not ignore the enforcement marker" \
    "$(! grep -v '^#' "$TPLDIR/gitignore.template" | grep -q 'unity-dev.json' \
       && grep -q '.claude/preflight/' "$TPLDIR/gitignore.template" && echo 1 || echo 0)"
  # When hooks are off, no hook can report that hooks are off. Catch it here.
  check_true "no settings source in this repo switches hooks off wholesale" \
    "$(python3 - <<PY
import json, os
hit = 0
for p in (os.path.expanduser("~/.claude/settings.json"),
          "$DIR/../.claude/settings.json", "$DIR/../.claude/settings.local.json"):
    try:
        if json.load(open(p)).get("disableAllHooks") is True:
            hit = 1
    except Exception:
        pass
print(0 if hit else 1)
PY
)"
else
  echo "skip - python3 not found; map tests skipped"
fi

echo
echo "passed: $PASS  failed: $FAIL"
[ "$FAIL" -eq 0 ]
