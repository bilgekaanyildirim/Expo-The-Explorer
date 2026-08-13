# Example: canonical engine-facts block

Every block in the `engine-facts/` files has this shape (~70-100 tokens):

```
## Physics queries and allocation

FACT: The array-returning forms of the query APIs allocate a new array on
every call; the NonAlloc / pre-allocated-buffer forms do not.
THRESHOLD: At frame frequency, 1 allocation per call → GC pressure; at event
frequency it is negligible.
LIMIT: If the buffer size is exceeded, the result is silently truncated —
size the buffer from the scale (fingerprint n).
INVERSE: For an editor tool or a single load-time call, NonAlloc complexity
is unnecessary; the plain form wins readability.
SOURCE: Unity 6.0 Scripting API, Physics section, 2026-08.
```

Note: FACT is verifiable behavior, not opinion; the INVERSE field is
mandatory — every fact has a context in which it would be misapplied; SOURCE
pins the version.
