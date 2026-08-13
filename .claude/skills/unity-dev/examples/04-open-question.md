# Example: a case where the procedure stops with an OPEN QUESTION

Game: turn-based tactics. Task: "write the system that computes hit damage."

`data-source.md` is applied → an authority is sought for the damage formula's
inputs (attack, defense, crit chance) → in the authorities section of
`fingerprint.md`, the authority for character stats is **not defined**
(marked OPEN).

Wrong behavior: assuming "stats usually live in a CharacterStats class" and
writing code.

Correct output:

```
OPEN QUESTION — no code was written.

The authority for character stats needed by the damage computation is
undefined. There are two realistic options, and they take the architecture
to different places:

1. Stats live in a data asset (SO), runtime modifications in a separate
   layer → the damage computation becomes a pure function, testable.
2. Stats live on the live character object, buffs write directly → the
   computation binds to the object, and the ownership procedure demands a
   single writer.

Which one? (The answer will be recorded in the fingerprint; all subsequent
combat-system decisions will rest on it.)
```

Note: the question was asked with options and consequences, not as a bare
"where are the stats?".
