"""Judge a Rick and Morty Together lab run from each instance's rick-and-morty-together-lab.log.

Checks:
  * every RT-CHECK line passed, and each --expect'ed check appeared on the right instances
  * no mod errors (RT-MODERROR) at all
  * game errors do not keep growing after warm-up (slope over the last half of the run)
  * managed heap does not trend upward; the mod's own root objects stay within their bound
  * the mod's frame cost stays within budget (median modms)
  * desync: RT-STATE samples are taken on a shared host-time grid; every item and avatar seen by two copies at the
    same grid tick is compared. Resting items must agree within 5 cm; moving ones within 50 cm.

Usage: python -I verify-lab.py <mod root> [--expect name[@role] ...] [--out results.json]
"""
import json
import math
import re
import sys
from pathlib import Path

LABS = {"host": "lab-host", "c1": "lab-c1", "c2": "lab-c2", "c3": "lab-c3"}
METRIC = re.compile(r"RT-METRIC (.*)")
CHECK = re.compile(r"RT-CHECK (\S+) (PASS|FAIL) ?(.*)")
STATE = re.compile(r"RT-STATE (.*)")
REST_LIMIT, MOVING_LIMIT = 0.05, 0.5
MODMS_BUDGET, HEAP_SLOPE_BUDGET, ERROR_SLOPE_BUDGET = 2.0, 1.5, 0.5


def parse(path):
    metrics, checks, moderrors, states = [], [], [], []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = METRIC.search(line)
        if m:
            metrics.append(dict(kv.split("=", 1) for kv in m.group(1).split() if "=" in kv))
            continue
        s = STATE.search(line)
        if s:
            states.append(dict(kv.split("=", 1) for kv in s.group(1).split() if "=" in kv))
            continue
        c = CHECK.search(line)
        if c:
            checks.append((c.group(1), c.group(2) == "PASS", c.group(3)))
        if "RT-MODERROR" in line:
            moderrors.append(line.strip()[:400])
    return metrics, checks, moderrors, states


def slope(values):
    n = len(values)
    if n < 3:
        return 0.0
    mx, my = (n - 1) / 2, sum(values) / n
    den = sum((x - mx) ** 2 for x in range(n))
    return sum((x - mx) * (y - my) for x, y in zip(range(n), values)) / den if den else 0.0


def vec(text):
    return [float(v) for v in text.split(",")[:3]]


def dist(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def grid_states(states):
    """Grid tick (host time rounded to the 2 s grid) -> {entity: (position, info)} for one copy."""
    out = {}
    for s in states:
        tick = round(float(s.get("ht", 0)) / 2.0)
        me = s.get("id", "?")
        entry = out.setdefault(tick, {})
        for key, value in s.items():
            if key.startswith("item"):
                pos, _, owner, epoch, mode = value.split("|")
                entry[key] = (vec(pos), mode, owner, epoch)
            elif key in ("self.head", "self.R"):
                entry["player" + me + key[4:]] = (vec(value), "self", "", "")
            elif key.startswith("av") and (key.endswith(".head") or key.endswith(".R")):
                pid, part = key[2:].split(".")
                entry["player" + pid + "." + part] = (vec(value), "remote", "", "")
    return out


def main():
    root = Path(sys.argv[1])
    args = sys.argv[2:]
    expects, out = [], None
    i = 0
    while i < len(args):
        if args[i] == "--expect":
            expects.append(args[i + 1]); i += 2
        elif args[i] == "--out":
            out = args[i + 1]; i += 2
        else:
            i += 1
    results, failures, grids = {}, [], {}
    for role, folder in LABS.items():
        path = root / "private" / folder / "rick-and-morty-together-lab.log"
        if not path.exists():
            continue
        metrics, checks, moderrors, states = parse(path)
        info = {"checks": [{"name": n, "pass": p, "detail": d} for n, p, d in checks], "moderrors": moderrors[:5]}
        for n, p, d in checks:
            if not p:
                failures.append(f"{role}: check {n} failed: {d}")
        if moderrors:
            failures.append(f"{role}: {len(moderrors)} mod errors, first: {moderrors[0]}")
        if metrics:
            half = metrics[len(metrics) // 2:]
            err = [float(m.get("errors", 0)) for m in half]
            heap = [float(m.get("heapMB", 0)) for m in half]
            info.update({
                "samples": len(metrics),
                "errors_end": metrics[-1].get("errors"),
                "error_slope_per_sample": round(slope(err), 3),
                "heap_end_mb": metrics[-1].get("heapMB"),
                "heap_slope_mb_per_sample": round(slope(heap), 3),
                "fps_median": sorted(float(m.get("fps", 0)) for m in metrics)[len(metrics) // 2],
            })
            if slope(err) > ERROR_SLOPE_BUDGET:
                failures.append(f"{role}: game errors keep growing ({slope(err):.2f} per sample)")
            if slope(heap) > HEAP_SLOPE_BUDGET:
                failures.append(f"{role}: managed heap keeps growing ({slope(heap):.2f} MB per sample)")
            mod = sorted(float(m["modms"]) for m in metrics if "modms" in m)
            if mod:
                info["modms_median"], info["modms_max"] = mod[len(mod) // 2], mod[-1]
                if mod[len(mod) // 2] > MODMS_BUDGET:
                    failures.append(f"{role}: mod costs {mod[len(mod) // 2]:.2f} ms per frame (median), budget {MODMS_BUDGET} ms")
            mobj = [float(m["modobjects"]) for m in metrics if "modobjects" in m]
            peers = max(int(m.get("peers", 0)) for m in metrics)
            bound = 1 + peers + 2   # the runtime, one avatar per peer, and a spare for copies in flight
            info["modobjects_max"], info["modobjects_bound"] = (max(mobj) if mobj else 0), bound
            if mobj and max(mobj) > bound:
                failures.append(f"{role}: the mod holds {max(mobj):.0f} root objects, more than its bound of {bound}")
        grids[role] = grid_states(states)
        info["state_samples"] = len(states)
        results[role] = info
    if not results:
        failures.append("no lab logs found")
    # Desync between every pair of copies at the same grid tick.
    desync = {"rest_max": 0.0, "moving_max": 0.0, "pairs": 0, "rest_pairs": 0, "worst": ""}
    roles = list(grids)
    for a in range(len(roles)):
        for b in range(a + 1, len(roles)):
            ga, gb = grids[roles[a]], grids[roles[b]]
            for tick in set(ga) & set(gb):
                for key in set(ga[tick]) & set(gb[tick]):
                    pa, ma = ga[tick][key][0], ga[tick][key][1]
                    pb, mb = gb[tick][key][0], gb[tick][key][1]
                    d = dist(pa, pb)
                    desync["pairs"] += 1
                    resting = key.startswith("item") and sorted((ma, mb)) == ["follow", "rest"]
                    bucket = "rest_max" if resting else "moving_max"
                    desync["rest_pairs"] += 1 if resting else 0
                    if d > desync[bucket]:
                        desync[bucket] = d
                        if d > (REST_LIMIT if resting else MOVING_LIMIT):
                            desync["worst"] = f"{key} at tick {tick}: {roles[a]} {pa} vs {roles[b]} {pb} ({ma}/{mb})"
    desync["rest_max"], desync["moving_max"] = round(desync["rest_max"], 4), round(desync["moving_max"], 4)
    if desync["worst"]:
        failures.append("desync over budget: " + desync["worst"])
    for spec in expects:
        name, _, where = spec.partition("@")
        if where == "anyclient":
            if not any(c["pass"] and c["name"] == name for r, info in results.items() if r != "host" for c in info.get("checks", [])):
                failures.append(f"no client passed {name}")
            continue
        roles_for = [where] if where else list(results)
        for role in roles_for:
            names = [c["name"] for c in results.get(role, {}).get("checks", []) if c["pass"]]
            if name not in names:
                failures.append(f"{role}: expected passing check {name} not seen")
    summary = {"pass": not failures, "failures": failures, "desync": desync, "instances": results}
    text = json.dumps(summary, indent=2)
    if out:
        Path(out).write_text(text, encoding="utf-8")
    print(text)
    sys.exit(0 if not failures else 1)


if __name__ == "__main__":
    main()
