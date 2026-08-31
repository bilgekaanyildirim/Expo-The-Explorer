#!/usr/bin/env python3
"""Turn the playtest telemetry in Firestore into a report you can act on.

    python3 tools/telemetry_report.py --demo                  # fake data, no Firebase
    python3 tools/telemetry_report.py --key sa.json           # the real thing
    python3 tools/telemetry_report.py --key sa.json --env editor

Writes tools/telemetry-report.html and prints a summary. Open the HTML in a
browser; it is one self-contained file with no network calls.

WHY THIS EXISTS RATHER THAN THE FIREBASE CONSOLE: the console FILTERS but does
not AGGREGATE. It will show you every run where status == "failed"; it will not
tell you Day 8's completion rate, its median completion time, or how the star
curve moves across the campaign. BigQuery export would, but it needs the Blaze
plan and is far more machinery than a playtest deserves.

READ ACCESS IS DELIBERATELY NOT THE GAME'S. The security rules give clients
create+update on runs/{runId} and nothing else -- read and delete are false for
every client, forever (decisions.md D-148). This script reads with a SERVICE
ACCOUNT, which bypasses rules by design. That separation is the point: the game
never gains the ability to read other testers' data just because a balancing tool
needs to (telemetry-plan.md M.5).

Getting a key: Firebase console -> Project settings -> Service accounts ->
Generate new private key. It is a real credential: keep it out of the repo (it is
already in .gitignore) and off any shared drive.
"""

from __future__ import annotations

import argparse
import html
import json
import math
import os
import statistics
import sys
from collections import defaultdict
from datetime import datetime, timedelta, timezone

# --- palette -----------------------------------------------------------------
# Outcomes are a STATUS scale, not a categorical one: completed/failed/quit are
# states with meaning, so they take the reserved status colours rather than
# series slots. `abandoned` is deliberately NEUTRAL GREY and not a status colour
# -- it is not something the player did, it is something we do not know, and
# giving it a status hue would lend it a certainty it has not earned.
#
# THE PALETTE VALIDATOR FLAGS ONE REAL PROBLEM AND IT IS HANDLED: completed
# (green) against failed (red) measures deutan DeltaE 4.1, i.e. the most common
# colour blindness cannot separate the two most important segments. The fix is
# not a different hue -- the status palette is fixed -- it is SECONDARY ENCODING:
# `failed` carries a 45-degree hatch, segment order never changes, and the full
# numbers sit in a table below every chart. Identity never rests on colour alone.
C = {
    "completed": "#0ca30c",
    "failed": "#d03b3b",
    "quit": "#fab219",
    "abandoned": "#898781",
    "series1": "#2a78d6",
    "series1_dark": "#3987e5",
}

OUTCOMES = ["completed", "failed", "quit", "abandoned"]

STALE_MINUTES_DEFAULT = 10


# --- data --------------------------------------------------------------------

def fetch_runs(key_path: str, environment: str) -> list[dict]:
    """Every run document, as plain dicts."""
    try:
        from google.cloud import firestore  # noqa: F401
        from google.oauth2 import service_account
        from google.cloud.firestore import Client
    except ImportError:
        sys.exit(
            "google-cloud-firestore is not installed.\n"
            "    pip install google-cloud-firestore\n"
            "Or run with --demo to see the report shape without Firebase."
        )

    creds = service_account.Credentials.from_service_account_file(key_path)
    with open(key_path) as f:
        project_id = json.load(f)["project_id"]

    client = Client(project=project_id, credentials=creds)

    runs = []
    for doc in client.collection("runs").stream():
        d = doc.to_dict() or {}
        d["_id"] = doc.id
        runs.append(d)

    if environment:
        runs = [r for r in runs if r.get("environment") == environment]
    return runs


def demo_runs() -> list[dict]:
    """Plausible fake data, so the report can be judged before a playtest exists.

    Shaped to contain the things you would actually want the report to surface:
    a difficulty ramp, one Day that is a genuine wall (12), one rest Day (9),
    and players who drop out partway through the campaign.
    """
    import random

    random.seed(7)
    now = datetime.now(timezone.utc)
    runs: list[dict] = []

    players = [f"P_{i:06X}" for i in range(1, 10)]
    # How far each player got before losing interest.
    reach = {p: random.randint(5, 18) for p in players}
    reach[players[0]] = 18

    for p in players:
        for day in range(0, reach[p]):
            # Difficulty ramp, with day 12 as a wall and day 9 as a breather.
            base = 0.92 - day * 0.028
            if day == 12:
                base -= 0.30
            if day == 9:
                base += 0.15
            base = max(0.12, min(0.97, base))

            attempts = 1
            while True:
                won = random.random() < base
                elapsed = random.gauss(150 + day * 7, 28)
                elapsed = max(35.0, elapsed)
                mistakes = max(0, int(random.gauss(1.4 + day * 0.32, 1.1)))

                if won:
                    star_score = max(0.0, min(1.0, random.gauss(0.62 - day * 0.012, 0.16)))
                    stars = 3 if star_score >= 0.55 else (2 if star_score >= 0.30 else 1)
                    status = "completed"
                else:
                    star_score, stars = 0.0, 0
                    roll = random.random()
                    status = "failed" if roll < 0.6 else ("quit" if roll < 0.85 else "in_progress")

                age = timedelta(days=random.uniform(0.2, 6.0))
                runs.append({
                    "_id": f"R_{len(runs):06X}",
                    "schemaVersion": 1,
                    "environment": "playtest",
                    "runId": f"R_{len(runs):06X}",
                    "playerId": p,
                    "installationId": "I_DEMO0001",
                    "dayIndex": day,
                    "dayContentIndex": day,
                    "status": status,
                    "elapsedSeconds": round(elapsed, 1),
                    "mistakeCount": mistakes,
                    "wrongDeliveryCount": mistakes // 2,
                    "timeoutCount": mistakes - mistakes // 2,
                    "ticketsDelivered": random.randint(4, 12),
                    "score": int(random.gauss(1500 + day * 60, 240)),
                    "stars": stars,
                    "starScore": round(star_score, 3),
                    "livesDepletedCount": 0 if won else 1,
                    "buildVersion": "0.3.4",
                    "_lastUpdated": now - age,
                    "_attempt": attempts,
                })

                if won or attempts >= 3 or random.random() < 0.35:
                    break
                attempts += 1

    return runs


def resolve_outcome(run: dict, stale_before: datetime) -> str:
    """The four buckets a finished-with run falls into.

    `abandoned` is DERIVED, never written by the client (telemetry-plan.md K):
    a crash, a force-quit and an OS kill are indistinguishable from the game's
    side, so it writes `in_progress` and stops. A run still saying that long
    after its last heartbeat was abandoned. One still saying it a minute ago is
    simply being played right now, and is excluded from the report entirely --
    counting a live run as abandoned would punish a Day for being popular.
    """
    status = run.get("status", "in_progress")
    if status in ("completed", "failed", "quit"):
        return status

    last = run.get("_lastUpdated")
    if last is None:
        return "abandoned"
    return "abandoned" if last < stale_before else "live"


def analyze(runs: list[dict], stale_minutes: int) -> dict:
    stale_before = datetime.now(timezone.utc) - timedelta(minutes=stale_minutes)

    for r in runs:
        if "_lastUpdated" not in r:
            ts = r.get("lastUpdatedAt")
            r["_lastUpdated"] = ts if isinstance(ts, datetime) else None
        r["_outcome"] = resolve_outcome(r, stale_before)

    live = [r for r in runs if r["_outcome"] == "live"]
    runs = [r for r in runs if r["_outcome"] != "live"]

    # GROUPED BY dayContentIndex, NOT dayIndex, and that is load-bearing. The
    # first is the Day file's own number; the second is a POSITION in the
    # resolved catalog, which slides when a Day is added, removed or fails to
    # parse. Grouping by position would silently re-attribute old runs to
    # whichever Day now sits in that slot (decisions.md D-149).
    by_day: dict[int, list[dict]] = defaultdict(list)
    for r in runs:
        by_day[r.get("dayContentIndex", -1)].append(r)

    days = []
    for day in sorted(by_day):
        rs = by_day[day]
        counts = {o: sum(1 for r in rs if r["_outcome"] == o) for o in OUTCOMES}
        done = [r for r in rs if r["_outcome"] == "completed"]
        players = {r.get("playerId") for r in rs}

        # Attempts per player, so retry and first-attempt rates are real rather
        # than inferred from a client-side counter that could drift.
        per_player: dict[str, list[dict]] = defaultdict(list)
        for r in rs:
            per_player[r.get("playerId")].append(r)
        retried = sum(1 for v in per_player.values() if len(v) > 1)

        first_results = []
        for v in per_player.values():
            v_sorted = sorted(v, key=lambda x: (x.get("_attempt", 0), x.get("_id", "")))
            first_results.append(v_sorted[0]["_outcome"] == "completed")

        days.append({
            "day": day,
            "runs": len(rs),
            "players": len(players),
            "counts": counts,
            "completion": counts["completed"] / len(rs) if rs else 0.0,
            "abandon": (counts["abandoned"] + counts["quit"]) / len(rs) if rs else 0.0,
            "median_time": statistics.median([r.get("elapsedSeconds", 0) for r in done]) if done else None,
            "mean_time": statistics.mean([r.get("elapsedSeconds", 0) for r in done]) if done else None,
            "mean_mistakes": statistics.mean([r.get("mistakeCount", 0) for r in rs]) if rs else 0.0,
            "mean_score": statistics.mean([r.get("score", 0) for r in done]) if done else None,
            # STARS ONLY FROM COMPLETED RUNS. A failed or abandoned run carries
            # stars=0, and folding those in would drag the curve down for a
            # reason that has nothing to do with how well the Day plays.
            "mean_stars": statistics.mean([r.get("stars", 0) for r in done]) if done else None,
            "star_hist": {s: sum(1 for r in done if r.get("stars") == s) for s in (1, 2, 3)},
            "retry_rate": retried / len(per_player) if per_player else 0.0,
            "first_completion": (sum(first_results) / len(first_results)) if first_results else 0.0,
        })

    # Per-player star curve: one point per Day the player actually completed.
    curves: dict[str, dict[int, float]] = defaultdict(dict)
    for r in runs:
        if r["_outcome"] != "completed":
            continue
        d = r.get("dayContentIndex", -1)
        s = r.get("stars", 0)
        # Best result on that Day, since a Day can be replayed for a better score.
        curves[r.get("playerId")][d] = max(curves[r.get("playerId")].get(d, 0), s)

    return {
        "days": days,
        "curves": dict(curves),
        "total_runs": len(runs),
        "live_runs": len(live),
        "players": len({r.get("playerId") for r in runs}),
        "builds": sorted({r.get("buildVersion", "?") for r in runs}),
    }


# --- svg ---------------------------------------------------------------------

def esc(s) -> str:
    return html.escape(str(s), quote=True)


def svg_open(w: int, h: int, label: str) -> list[str]:
    return [f'<svg viewBox="0 0 {w} {h}" width="100%" height="{h}" role="img" '
            f'aria-label="{esc(label)}" class="chart">']


def axis_bits(x0, y0, x1, y1, ticks, fmt, w) -> list[str]:
    """Recessive grid + baseline, muted labels. Grid never competes with marks."""
    out = []
    for v, y in ticks:
        out.append(f'<line class="grid" x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}"/>')
        out.append(f'<text class="tick" x="{x0 - 8}" y="{y + 4:.1f}" text-anchor="end">{esc(fmt(v))}</text>')
    out.append(f'<line class="baseline" x1="{x0}" y1="{y1}" x2="{x1}" y2="{y1}"/>')
    return out


def chart_outcomes(days: list[dict]) -> str:
    """Day x outcome, stacked. The report's headline: where the campaign breaks.

    Absolute counts rather than 100% stacks, because n is small in a playtest and
    a percentage built from three runs is a confident-looking lie. Volume and
    proportion are both visible this way; the table underneath carries the rest.
    """
    if not days:
        return '<p class="empty">No finished runs yet.</p>'

    w, h = 900, 300
    x0, y0, x1, y1 = 52, 20, w - 16, h - 46
    n = len(days)
    slot = (x1 - x0) / n
    bw = min(46, slot * 0.62)
    top = max(d["runs"] for d in days) or 1

    ticks = [(round(top * f), y1 - (y1 - y0) * f) for f in (0, 0.25, 0.5, 0.75, 1.0)]
    out = svg_open(w, h, "Run outcomes for each Day")
    out.append(
        '<defs><pattern id="hatchFail" width="6" height="6" patternUnits="userSpaceOnUse" '
        'patternTransform="rotate(45)">'
        f'<rect width="6" height="6" fill="{C["failed"]}"/>'
        '<line x1="0" y1="0" x2="0" y2="6" stroke="rgba(255,255,255,.55)" stroke-width="2.5"/>'
        '</pattern></defs>'
    )
    out += axis_bits(x0, y0, x1, y1, ticks, lambda v: f"{v:g}", w)

    for i, d in enumerate(days):
        cx = x0 + slot * (i + 0.5)
        y = y1
        for o in OUTCOMES:
            c = d["counts"][o]
            if not c:
                continue
            seg = (y1 - y0) * (c / top)
            y -= seg
            fill = "url(#hatchFail)" if o == "failed" else C[o]
            # 2px surface gap between segments -- the spacer that keeps stacked
            # fills from reading as one mass.
            out.append(
                f'<rect x="{cx - bw / 2:.1f}" y="{y:.1f}" width="{bw:.1f}" '
                f'height="{max(0, seg - 2):.1f}" fill="{fill}" rx="2">'
                f'<title>Day {d["day"]} — {o}: {c} of {d["runs"]} runs</title></rect>'
            )
        out.append(
            f'<text class="tick" x="{cx:.1f}" y="{y1 + 16}" text-anchor="middle">{d["day"]}</text>'
        )
        out.append(
            f'<text class="nlabel" x="{cx:.1f}" y="{y - 6:.1f}" text-anchor="middle">n={d["runs"]}</text>'
        )

    out.append(f'<text class="axis-title" x="{x0}" y="{h - 8}">Day (content index)</text>')
    out.append('</svg>')
    return "".join(out)


def chart_stars(days: list[dict], curves: dict[str, dict[int, float]]) -> str:
    """Star score across the campaign: every player faint, the mean on top.

    PLAYERS DELIBERATELY GET NO COLOUR. The categorical palette has eight slots
    and cycling them is forbidden; more to the point, twenty coloured lines is a
    plate of spaghetti nobody can read. Faint uniform traces plus one emphasised
    mean shows the two things actually being asked -- the shape of the curve, and
    how wide the spread is -- and it works identically at 5 players or 50.
    """
    if not days:
        return '<p class="empty">No completed runs yet.</p>'

    w, h = 900, 300
    x0, y0, x1, y1 = 52, 20, w - 16, h - 46
    xs = [d["day"] for d in days]
    lo, hi = min(xs), max(xs)
    span = max(1, hi - lo)

    def px(day):
        return x0 + (x1 - x0) * (day - lo) / span

    def py(stars):
        return y1 - (y1 - y0) * (stars / 3.0)

    ticks = [(s, py(s)) for s in (0, 1, 2, 3)]
    out = svg_open(w, h, "Average stars for each Day, per player and overall")
    out += axis_bits(x0, y0, x1, y1, ticks, lambda v: f"{v:g}★", w)

    for pid, curve in sorted(curves.items()):
        pts = [(px(d), py(s)) for d, s in sorted(curve.items())]
        if len(pts) < 2:
            continue
        path = " ".join(f"{'M' if i == 0 else 'L'}{x:.1f},{y:.1f}" for i, (x, y) in enumerate(pts))
        out.append(f'<path class="trace" d="{path}"><title>{esc(pid)}</title></path>')

    mean_pts = [(px(d["day"]), py(d["mean_stars"])) for d in days if d["mean_stars"] is not None]
    if len(mean_pts) >= 2:
        path = " ".join(f"{'M' if i == 0 else 'L'}{x:.1f},{y:.1f}" for i, (x, y) in enumerate(mean_pts))
        out.append(f'<path class="mean" d="{path}"/>')
    for d in days:
        if d["mean_stars"] is None:
            continue
        out.append(
            f'<circle class="meandot" cx="{px(d["day"]):.1f}" cy="{py(d["mean_stars"]):.1f}" r="4.5">'
            f'<title>Day {d["day"]} — mean {d["mean_stars"]:.2f}★ '
            f'from {d["counts"]["completed"]} completed run(s)</title></circle>'
        )

    step = max(1, len(days) // 12)
    for i, d in enumerate(days):
        if i % step:
            continue
        out.append(f'<text class="tick" x="{px(d["day"]):.1f}" y="{y1 + 16}" '
                   f'text-anchor="middle">{d["day"]}</text>')

    out.append(f'<text class="axis-title" x="{x0}" y="{h - 8}">Day (content index)</text>')
    out.append('</svg>')
    return "".join(out)


def chart_bars(days, key, label, fmt, color, note=""):
    """One measure per Day. Separate charts rather than a second y-axis: two
    scales on one frame is the single most misread chart there is."""
    vals = [(d["day"], d[key]) for d in days if d[key] is not None]
    if not vals:
        return f'<p class="empty">No data for {esc(label)} yet.</p>'

    w, h = 900, 230
    x0, y0, x1, y1 = 52, 18, w - 16, h - 46
    n = len(vals)
    slot = (x1 - x0) / n
    bw = min(40, slot * 0.6)
    top = max(v for _, v in vals) or 1

    ticks = [(top * f, y1 - (y1 - y0) * f) for f in (0, 0.5, 1.0)]
    out = svg_open(w, h, label)
    out += axis_bits(x0, y0, x1, y1, ticks, fmt, w)

    for i, (day, v) in enumerate(vals):
        cx = x0 + slot * (i + 0.5)
        bh = (y1 - y0) * (v / top)
        out.append(
            f'<rect x="{cx - bw / 2:.1f}" y="{y1 - bh:.1f}" width="{bw:.1f}" height="{bh:.1f}" '
            f'fill="{color}" rx="3"><title>Day {day} — {fmt(v)}</title></rect>'
        )
        out.append(f'<text class="tick" x="{cx:.1f}" y="{y1 + 16}" text-anchor="middle">{day}</text>')

    out.append('</svg>')
    return "".join(out) + (f'<p class="note">{esc(note)}</p>' if note else "")


def small_multiples(curves: dict[str, dict[int, float]], days) -> str:
    """One mini curve per player -- the per-player breakdown, as a form that
    survives having many players. Shared scales, so the panels are comparable."""
    if not curves:
        return '<p class="empty">No completed runs yet.</p>'

    xs = [d["day"] for d in days]
    lo, hi = min(xs), max(xs)
    span = max(1, hi - lo)
    w, h = 210, 92
    x0, y0, x1, y1 = 6, 8, w - 6, h - 18

    cards = []
    for pid, curve in sorted(curves.items()):
        pts = [((x0 + (x1 - x0) * (d - lo) / span), (y1 - (y1 - y0) * (s / 3.0)))
               for d, s in sorted(curve.items())]
        body = svg_open(w, h, f"{pid} star curve")
        body.append(f'<line class="baseline" x1="{x0}" y1="{y1}" x2="{x1}" y2="{y1}"/>')
        if len(pts) >= 2:
            path = " ".join(f"{'M' if i == 0 else 'L'}{x:.1f},{y:.1f}" for i, (x, y) in enumerate(pts))
            body.append(f'<path class="mean sm" d="{path}"/>')
        for (x, y), (d, s) in zip(pts, sorted(curve.items())):
            body.append(f'<circle class="meandot sm" cx="{x:.1f}" cy="{y:.1f}" r="2.6">'
                        f'<title>Day {d}: {s:g}★</title></circle>')
        body.append('</svg>')
        reached = max(curve) if curve else 0
        cards.append(
            f'<figure class="sm-card">{"".join(body)}'
            f'<figcaption>{esc(pid)} <span class="muted">· {len(curve)} Days done, '
            f'reached {reached}</span></figcaption></figure>'
        )
    return f'<div class="sm-grid">{"".join(cards)}</div>'


# --- html --------------------------------------------------------------------

CSS = """
:root{--surface:#fcfcfb;--page:#f9f9f7;--ink:#0b0b0b;--ink2:#52514e;--muted:#898781;
--grid:#e1e0d9;--base:#c3c2b7;--series1:#2a78d6;--border:rgba(11,11,11,.10)}
@media (prefers-color-scheme:dark){:root{--surface:#1a1a19;--page:#0d0d0d;--ink:#fff;
--ink2:#c3c2b7;--muted:#898781;--grid:#2c2c2a;--base:#383835;--series1:#3987e5;
--border:rgba(255,255,255,.10)}}
*{box-sizing:border-box}
body{margin:0;background:var(--page);color:var(--ink);
font:15px/1.55 ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif}
.wrap{max-width:980px;margin:0 auto;padding:28px 20px 80px}
h1{font-size:26px;margin:0 0 4px;letter-spacing:-.02em}
h2{font-size:17px;margin:34px 0 4px;letter-spacing:-.01em}
.sub{color:var(--ink2);margin:0 0 22px}
.banner{background:#fab219;color:#0b0b0b;padding:10px 14px;border-radius:6px;
font-weight:600;margin:0 0 20px}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:1px;
background:var(--border);border:1px solid var(--border);border-radius:6px;overflow:hidden;margin:0 0 8px}
.tile{background:var(--surface);padding:14px 16px}
.tile .k{font-size:11px;letter-spacing:.09em;text-transform:uppercase;color:var(--muted)}
.tile .v{font-size:25px;font-variant-numeric:tabular-nums;letter-spacing:-.02em;margin-top:2px}
figure{margin:0 0 6px;background:var(--surface);border:1px solid var(--border);
border-radius:6px;padding:12px 12px 4px}
.chart{display:block}
.grid{stroke:var(--grid);stroke-width:1}
.baseline{stroke:var(--base);stroke-width:1}
.tick{fill:var(--muted);font-size:11px;font-variant-numeric:tabular-nums}
.nlabel{fill:var(--muted);font-size:10px;font-variant-numeric:tabular-nums}
.axis-title{fill:var(--muted);font-size:11px}
.trace{fill:none;stroke:var(--muted);stroke-width:1.25;opacity:.34}
.trace:hover{stroke:var(--ink);opacity:1;stroke-width:2}
.mean{fill:none;stroke:var(--series1);stroke-width:2.5;stroke-linejoin:round;stroke-linecap:round}
.mean.sm{stroke-width:1.8}
.meandot{fill:var(--series1);stroke:var(--surface);stroke-width:2}
.meandot.sm{stroke-width:1.2}
.legend{display:flex;flex-wrap:wrap;gap:14px;margin:8px 0 0;font-size:13px;color:var(--ink2)}
.legend i{width:13px;height:13px;border-radius:3px;display:inline-block;vertical-align:-2px;margin-right:6px}
.note{color:var(--muted);font-size:12.5px;margin:6px 0 0}
.empty{color:var(--muted);padding:22px;text-align:center;background:var(--surface);
border:1px dashed var(--border);border-radius:6px}
table{width:100%;border-collapse:collapse;background:var(--surface);
border:1px solid var(--border);border-radius:6px;overflow:hidden;font-size:13.5px}
th,td{padding:7px 10px;text-align:right;border-bottom:1px solid var(--grid)}
th:first-child,td:first-child{text-align:left}
th{font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted);font-weight:600}
td{font-variant-numeric:tabular-nums;color:var(--ink2)}
tbody tr:last-child td{border-bottom:0}
.sm-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:10px}
.sm-card{padding:6px 6px 2px}
figcaption{font-size:12px;color:var(--ink2);padding:2px 4px 6px}
.muted{color:var(--muted)}
.warn{border-left:3px solid #fab219;padding-left:12px;margin:18px 0;color:var(--ink2)}
"""


def render_html(a: dict, demo: bool, env: str, stale: int) -> str:
    days = a["days"]
    total_players = a["players"]

    tiles = [
        ("Players", total_players),
        ("Runs", a["total_runs"]),
        ("Days seen", len(days)),
        ("Live now", a["live_runs"]),
        ("Builds", len(a["builds"])),
    ]
    tiles_html = "".join(
        f'<div class="tile"><div class="k">{esc(k)}</div><div class="v">{esc(v)}</div></div>'
        for k, v in tiles)

    legend = (
        '<div class="legend">'
        f'<span><i style="background:{C["completed"]}"></i>completed</span>'
        f'<span><i style="background:repeating-linear-gradient(45deg,{C["failed"]},'
        f'{C["failed"]} 2px,rgba(255,255,255,.55) 2px,rgba(255,255,255,.55) 4px)"></i>failed</span>'
        f'<span><i style="background:{C["quit"]}"></i>quit</span>'
        f'<span><i style="background:{C["abandoned"]}"></i>abandoned (stale &gt;{stale}m)</span>'
        '</div>'
    )

    rows = []
    for d in days:
        st = d["star_hist"]
        rows.append(
            f'<tr><td>Day {d["day"]}</td><td>{d["runs"]}</td><td>{d["players"]}</td>'
            f'<td>{d["completion"]*100:.0f}%</td>'
            f'<td>{d["first_completion"]*100:.0f}%</td>'
            f'<td>{d["abandon"]*100:.0f}%</td>'
            f'<td>{d["retry_rate"]*100:.0f}%</td>'
            f'<td>{("%.0fs" % d["median_time"]) if d["median_time"] else "—"}</td>'
            f'<td>{d["mean_mistakes"]:.1f}</td>'
            f'<td>{("%.2f" % d["mean_stars"]) if d["mean_stars"] is not None else "—"}</td>'
            f'<td>{st[1]}/{st[2]}/{st[3]}</td></tr>'
        )

    banner = ('<p class="banner">DEMO DATA — this is generated, not your playtest. '
              'Run without --demo once runs exist.</p>') if demo else ""

    small_n = total_players and total_players < 12
    caution = ('<p class="warn"><strong>Small sample.</strong> With '
               f'{total_players} player(s), read the ORDER of Days, not the exact numbers: '
               '“Day 12 is harder than Day 11” is reliable here; “Day 12 completes at 41%” '
               'is not. Star histograms in particular are a handful of runs each.</p>'
               ) if small_n else ""

    return f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Expo the Explorer — playtest telemetry</title><style>{CSS}</style></head><body>
<div class="wrap">
<h1>Playtest telemetry</h1>
<p class="sub">environment <strong>{esc(env or "all")}</strong> · builds {esc(", ".join(a["builds"]) or "—")}
 · generated {esc(datetime.now().strftime("%Y-%m-%d %H:%M"))}</p>
{banner}
<div class="tiles">{tiles_html}</div>
{caution}

<h2>Where the campaign breaks</h2>
<p class="sub">Every finished run, by how it ended. This is the chart to read first:
a Day whose bar leans away from green is doing something the others are not.</p>
<figure>{chart_outcomes(days)}{legend}</figure>

<h2>Star score across the campaign</h2>
<p class="sub">Each faint line is one player; the blue line is the mean. Only
<em>completed</em> runs count — a failed run scores zero stars for reasons that have
nothing to do with how the Day plays.</p>
<figure>{chart_stars(days, a["curves"])}</figure>

<h2>Each player, Day by Day</h2>
<p class="sub">Same data, one panel per player. Shared scales, so the panels compare.</p>
{small_multiples(a["curves"], days)}

<h2>How long a Day takes</h2>
<figure>{chart_bars(days, "median_time", "Median completion time", lambda v: f"{v:.0f}s", C["series1"],
"Median rather than mean: one player who walked away mid-Day would drag an average badly.")}</figure>

<h2>Mistakes per run</h2>
<figure>{chart_bars(days, "mean_mistakes", "Mean mistakes", lambda v: f"{v:.1f}", C["series1"],
"A mistake is a wrong delivery or a ticket that timed out — the two things that cost a life.")}</figure>

<h2>Every number</h2>
<table><thead><tr><th>Day</th><th>Runs</th><th>Players</th><th>Completed</th>
<th>1st try</th><th>Left</th><th>Retried</th><th>Median</th><th>Mistakes</th>
<th>Stars</th><th>1/2/3★</th></tr></thead><tbody>{"".join(rows)}</tbody></table>
<p class="note">“Left” = quit + abandoned. “1st try” = players who completed on their
first attempt at that Day. Stars are the mean over completed runs only.</p>
</div></body></html>"""


# --- terminal ----------------------------------------------------------------

def print_summary(a: dict, stale: int) -> None:
    print(f"\n  {a['players']} players · {a['total_runs']} finished runs · "
          f"{a['live_runs']} in progress right now\n")
    if not a["days"]:
        print("  No finished runs yet.\n")
        return

    print(f"  {'Day':>4}  {'runs':>5} {'done':>6} {'left':>6} {'median':>8} {'mist':>6} {'stars':>6}")
    print(f"  {'-'*4}  {'-'*5} {'-'*6} {'-'*6} {'-'*8} {'-'*6} {'-'*6}")
    for d in a["days"]:
        med = f"{d['median_time']:.0f}s" if d["median_time"] else "—"
        st = f"{d['mean_stars']:.2f}" if d["mean_stars"] is not None else "—"
        print(f"  {d['day']:>4}  {d['runs']:>5} {d['completion']*100:>5.0f}% "
              f"{d['abandon']*100:>5.0f}% {med:>8} {d['mean_mistakes']:>6.1f} {st:>6}")

    # The one thing worth saying out loud rather than leaving in a table: which
    # Days are outliers against their neighbours. A campaign is allowed to be
    # hard; it is the STEPS between Days that read as unfair.
    worst = sorted((d for d in a["days"] if d["runs"] >= 3), key=lambda d: d["completion"])[:3]
    if worst:
        print("\n  Hardest by completion:", ", ".join(
            f"Day {d['day']} ({d['completion']*100:.0f}%, n={d['runs']})" for d in worst))
    leavers = sorted((d for d in a["days"] if d["runs"] >= 3), key=lambda d: -d["abandon"])[:3]
    if leavers:
        print("  Most left behind:      ", ", ".join(
            f"Day {d['day']} ({d['abandon']*100:.0f}%, n={d['runs']})" for d in leavers))
    print()


def main() -> None:
    p = argparse.ArgumentParser(description="Playtest telemetry report.")
    p.add_argument("--key", help="service account JSON from the Firebase console")
    p.add_argument("--demo", action="store_true", help="generate fake data instead")
    p.add_argument("--env", default="playtest",
                   help="environment filter; '' for all (default: playtest)")
    p.add_argument("--stale-minutes", type=int, default=STALE_MINUTES_DEFAULT,
                   help="an in_progress run older than this counts as abandoned")
    p.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "telemetry-report.html"))
    args = p.parse_args()

    if args.demo:
        runs = demo_runs()
    elif args.key:
        runs = fetch_runs(args.key, args.env)
    else:
        p.error("pass --key <service-account.json>, or --demo to see the report shape")

    a = analyze(runs, args.stale_minutes)
    print_summary(a, args.stale_minutes)

    with open(args.out, "w", encoding="utf-8") as f:
        f.write(render_html(a, args.demo, args.env, args.stale_minutes))
    print(f"  Wrote {args.out}\n")


if __name__ == "__main__":
    main()
