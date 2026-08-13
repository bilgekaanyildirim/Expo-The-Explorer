# fingerprint — project profile

<!-- The AI drafts it, the USER VERIFIES it (mandatory — this file is about
     the project's intent). Any unanswered field is written as OPEN; progress
     does not stop, and the field surfaces as an assumption in the preflight
     of the first task that touches it. The summary is copied into the root
     CLAUDE.md. -->

- **Space model:** <2D/3D; grid or free; is the tile/unit size fixed>
- **Determinism:** <is replay/lockstep/sync required; float tolerance>
- **Data authorities:** <data → single-owner list; the procedures read from here>
- **Scale magnitudes (n):** <how many units/bullets/tiles/UI elements at once —
  the cost model's n comes from here>
- **Persistence:** <what is saved, format, versioning scheme>
- **Network model:** <none | ...if present, the authority model — it changes
  the ownership procedure fundamentally>
- **Performance budget:** <per target platform: target frame rate → ms/frame,
  memory ceiling, load-time ceiling — the cost model's "frame budget" and the
  shipping phase's measurements read from here>
- **Target platform/version:** <platforms; Unity version — engine-facts
  version pinning binds to this>
