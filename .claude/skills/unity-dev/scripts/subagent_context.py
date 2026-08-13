#!/usr/bin/env python3
"""subagent_context.py — SubagentStart hook: the map contract, for every subagent.

Why this exists. The built-in Explore and Plan agents skip the CLAUDE.md
hierarchy — documented behaviour, not a bug, and there is no frontmatter field
that changes it. So the main conversation can hand "find where X lives" to an
agent that has never heard of index.md and answers it with a repo-wide grep.
The write gate still holds inside a subagent (settings hooks run there), so
nothing unsafe happens; what leaks is the discipline, and with it the postflight
line that claims the task located rather than scanned.

A SubagentStart hook is the only lever that reaches the built-ins: it cannot be
declined, it does not replace their tuned prompts, and it lives in
settings.json, where the skill keeps every other guarantee.
`.claude/agents/Explore.md` overrides the built-in on top of this; the two are
independent, and this one survives that file being deleted.

Tone matters. The text below is written as project facts, not as orders. Hook
output framed as out-of-band instructions trips Claude's prompt-injection
defences and gets surfaced to the user instead of used, which would make the
hook worse than nothing.

Usage: python3 subagent_context.py [project_root]   (reads the hook payload on stdin)
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from session_context import build_report, emit, project_root   # noqa: E402

# Fires once per subagent, so it is priced accordingly: a contract short enough
# to be cheap, sitting in front of a health report that is already computed.
MAX_CHARS = 3000

CONTRACT = """This project resolves code locations from maps rather than by searching.
- .claude/index.md: system name -> shard, entry files, scenes, prefabs, data.
- .claude/blueprint.md: the architecture plan — systems, dependency arrows, folder layout.
- .claude/codemap-<shard>.md: one line per file — role, sys, api, deps, callers, criticality.
- .claude/unitymap.md: scene/prefab tree. .claude/assetmap.md: .asset inventory.
Read in that order. Those files already hold what a repo-wide grep would have to
rediscover, and they name the shard a path belongs to, so a search that starts from
them returns a file set instead of a list of matches. Grep and Glob are useful inside
the paths they point at; across the whole repository they mostly re-derive index.md.
When the maps run out of information, the useful answer names the step that ran out
and what the map is missing — that gap is repairable, a guess is not."""


def main() -> int:
    root = project_root()
    report = build_report(root)
    if report is None:
        return 0   # not a unity-dev project; stay silent

    emit([CONTRACT, "", "Current map state:"] + report,
         "SubagentStart", limit=MAX_CHARS)
    return 0


if __name__ == "__main__":
    sys.exit(main())
