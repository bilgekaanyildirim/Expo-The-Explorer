# bootstrap — project setup from scratch

Input: the user's description of the game. Output: the artifacts below + user
approval. This procedure runs once, on day zero.

## Outputs to produce (in order)

1. `scope.md` draft — what the game is, core loop, win/lose, the
   vertical-slice boundary, the release scope, **things explicitly out of
   scope**. Template: `templates/scope.template.md`.
2. `fingerprint.md` draft — space model, determinism, data authorities, scale
   magnitudes (n values), persistence, network model, target
   platform/version. Template: `templates/fingerprint.template.md`. Any
   unanswered field is marked `OPEN`; progress does not stop.
3. Root `CLAUDE.md` — invariant candidates + phase line + fingerprint
   summary. Template: `templates/CLAUDE.md.template`. The AI proposes a
   candidate list; the user prunes it.
4. `.claude/rules/` skeleton — per-domain conventions (ui/gameplay/data).
   They cannot be derived from code (there is none); they are decided
   together with the user.
5. `blueprint.md` draft — the architecture plan in one file: systems and
   the arrows between them, the scene inventory (boot scene first), the
   prefab inventory, hierarchy conventions, and the folder layout.
   Template: `templates/blueprint.template.md`. **Arrows must be
   one-directional**; if a bidirectional arrow appears, there is an
   ownership problem — apply `ownership.md`. The folder layout and
   `.claude/shards.json` describe the same tree from two sides: if the
   project uses different script folders, edit `shards.json` in the same
   step, or every file lands in the catch-all shard.
6. Installation — run `scripts/init_project.py`; then verify registration
   with `/hooks` and `/memory`, and run `scripts/test_enforcement.sh` (do
   not move to the first task before all three pass).
7. First map pass — `init_project.py` builds codemap/unitymap/assetmap/index
   once. Verify it with `python3 .claude/hooks/check_blueprint.py`: on a
   fresh project the only findings should be INFO lines for folders not
   created yet. An ERROR at bootstrap means the blueprint and the disk
   already disagree.
8. Phase is written as `production` — there is no prototype phase; the
   first task is already written at release quality.

## Question discipline

The questions are constructed here by the AI, per game — this file contains
no canned question text. Rules:

- Do not ask what can be inferred from the description.
- Do not ask anything whose answer would not change the architecture.
- For irreversible decisions (space model, determinism, persistence schema,
  networking), leave no ambiguity — these are never skipped without asking.
- Anything left unanswered is written into `fingerprint.md` as `OPEN`; it
  surfaces as an assumption in the preflight of the first task that touches
  that area.

## Design contribution

This procedure is not secretarial work. If the core loop is weak, the release
scope is oversized relative to the team and timeline, or a genre-critical
system is missing (e.g. a progression game with no progression save), say so
explicitly. The product decision belongs to the user; once decided, it is
written to `decisions.md` with its rationale and is not re-litigated.

## No reduced mode

There is no light version of this flow: every project this skill manages is
built to ship, and all eight outputs are produced every time. If the user
explicitly orders a reduced setup for a throwaway experiment, record that
order in `decisions.md` together with its consequence — without the full
bootstrap there is no enforcement, no fingerprint, and no cost-model inputs,
so none of the skill's guarantees apply until a full bootstrap runs.
