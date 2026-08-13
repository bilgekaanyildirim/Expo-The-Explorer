# postflight — end-of-task self-audit

Produced at the end of every task that wrote code. The output is a short
binary-flagged checklist; there is no "partially" — partially is NO, and it
requires an explanation.

## Format

```markdown
# Postflight: <task name>

- [Y/N] No procedure violations (the decisions of the applied procedures were followed)
- [Y/N] Located, not scanned (locate.md ran first; the step it answered at is
        named; no repo-wide grep/Glob happened — including inside any subagent
        the task delegated a search to, whose stopping step is named too)
- [Y/N] Invariant scan clean (root CLAUDE.md items, for the code that was touched)
- [Y/N] Proof standard met (production: the cost threshold was applied to every
        frequency-sensitive decision; shipping: a measurement is attached to
        every optimization)
- [Y/N] Codemap updated (a schema-conformant line for every file written,
        `sys:` filled, `dep?:` confirmed into `dep:`, no marker left behind)
- [Y/N] decisions.md updated (if a new architectural decision was made, with its
        `affects:` field; Y if none was)
- [Y/N] Assumptions closed (assumptions from the preflight that were verified
        are recorded in the fingerprint; the rest remain marked OPEN)
- [Y/N] Editor side synced (declared Editor tasks are done; the unitymap was
        regenerated and shows the expected wiring, with no new unassigned
        reference on a touched object; Y if the task had no editor side)
- [Y/N] Blueprint consistent — MACHINE-CHECKED, quote the output:
        `python3 .claude/hooks/check_blueprint.py` → `<the "n error(s), m warning(s)" line>`
        Y only when the error count is 0. Errors introduced by this task are
        fixed here; pre-existing ones are named and carried to the user.
- [Y/N] Shipping signals checked (if the release scope is content-complete or
        within ~one task of it, a phase-transition audit was proposed; Y when
        it is still clearly far)

For every NO item: <one-line reason + what will be done>
```

## Rules

- Postflight is never skipped; "it was a small change" is not an exemption.
- A postflight containing a NO does not close the task: either fix it or
  escalate it to the user.
- The blueprint item is the one line that is not self-assessment: it carries
  the script's own output. A postflight that claims Y without the quoted
  counts is not a postflight.
- Full example: `examples/02-postflight.md`.
