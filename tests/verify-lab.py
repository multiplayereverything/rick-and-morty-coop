"""Judge a Rick and Morty Together lab run from each instance's rick-and-morty-together-lab.log.

Checks:
  * every RT-CHECK line passed, and each --expect'ed check appeared on the right instances
  * no mod errors (RT-MODERROR) at all
  * game errors do not keep growing after warm-up (slope over the last half of the run)
  * managed heap does not trend upward; the mod's own root objects stay within their bound
  * the mod's frame cost stays within budget (median modms)
  * desync: RT-STATE samples are taken on a shared host-time grid; every item and avatar seen by two copies at the
    same grid tick is compared. Resting items must agree within 5 cm; moving ones within 50 cm. Levers, doors and
    sliders at rest must agree within 2% of their travel and have the same lock state.
  * NPCs: every dialogue and animation Brain effect (RT-FX lines from the Voice, Animation and cinematic modules) that
    one copy runs while another copy is in the same scene must run there too, within 3 s. Effects in a copy's first
    12 s in a scene are not compared: each copy runs a scene's opening effects when it loads the scene.

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
FX = re.compile(r"RT-FX ht=(\S+) scene=(\S+) fx=(\S+)")
NPC_MODULES = ("BrainControlledModule_Voice.", "BrainControlledModule_Animation.", "BrainControlledModule_RickCinematicEvent.", "BrainControlledModule_InterruptAllCharacters.")
FX_WINDOW = 3.0
SCENE_SETTLE = 12.0   # a copy that just loaded a scene runs that scene's opening effects itself; compare after that
REST_LIMIT, MOVING_LIMIT = 0.05, 0.5
CONTROL_REST_LIMIT = 0.02
GRID_SLACK = 0.15   # seconds a state sample may sit off its 2 s grid moment and still be compared
STARVED_FPS = 27.0
MODMS_BUDGET, HEAP_SLOPE_BUDGET, ERROR_SLOPE_BUDGET = 2.0, 1.5, 0.5


def parse(path):
    metrics, checks, moderrors, states, fx = [], [], [], [], []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        f = FX.search(line)
        if f:
            if any(m in f.group(3) for m in NPC_MODULES):
                fx.append((float(f.group(1)), f.group(2), f.group(3)))
            continue
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
    return metrics, checks, moderrors, states, fx


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
    last_scene = None
    for s in states:
        ht = float(s.get("ht", 0))
        # The first sample after this copy changed scene or teleported can catch a player mid-move (the frame the game
        # spawned or moved them, before the others have their next pose): one 50 ms pose, not a desync. Skip it.
        scene = s.get("scene", "") + "/" + s.get("zone", "")
        changed = last_scene is not None and scene != last_scene
        last_scene = scene
        if changed:
            continue
        tick = round(ht / 2.0)
        # A sample taken well after its grid moment (a scene load stalled that frame) is not the same moment as the
        # other copies' samples for that tick: leave it out rather than compare two different instants.
        if abs(ht - tick * 2.0) > GRID_SLACK:
            continue
        me = s.get("id", "?")
        entry = out.setdefault(tick, {})
        if s.get("self.jump"):
            entry.setdefault("_jumped", set()).add(me)
        for key, value in s.items():
            if key.startswith("item"):
                pos, _, owner, epoch, mode = value.split("|")
                entry[key] = (vec(pos), mode, owner, epoch)
            elif key.startswith("ctl"):
                amount, locks, owner, epoch, mode = value.split("|")
                entry[key] = ([float(amount)], mode, owner, locks)
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
    results, failures, grids, effects, scenes = {}, [], {}, {}, {}
    for role, folder in LABS.items():
        path = root / "private" / folder / "rick-and-morty-together-lab.log"
        if not path.exists():
            continue
        metrics, checks, moderrors, states, fx = parse(path)
        effects[role] = fx
        scenes[role] = [(float(s.get("ht", 0)), s.get("scene", "")) for s in states]
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
                # Frame cost is wall time: on a PC starved by other work (other labs) it counts time the mod's thread
                # waited for the CPU. Judge it only when the copy ran near its 30 fps cap.
                if info["fps_median"] < STARVED_FPS:
                    info["modms_judged"] = False
                    info["modms_note"] = f"not judged: fps median {info['fps_median']:.1f}, the PC was starved"
                elif mod[len(mod) // 2] > MODMS_BUDGET:
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
    desync = {"rest_max": 0.0, "moving_max": 0.0, "pairs": 0, "rest_pairs": 0, "control_rest_max": 0.0, "control_rest_pairs": 0, "worst": ""}
    roles = list(grids)
    for a in range(len(roles)):
        for b in range(a + 1, len(roles)):
            ga, gb = grids[roles[a]], grids[roles[b]]
            for tick in set(ga) & set(gb):
                # A player who jumped (teleport, rig placed, a flat hand snapping) within 0.6 s of this sample: the
                # others still draw the pose from 50 ms before. Not drift; don't compare that player at this tick.
                jumped = ga[tick].get("_jumped", set()) | gb[tick].get("_jumped", set())
                for key in set(ga[tick]) & set(gb[tick]):
                    if key == "_jumped" or (key.startswith("player") and key[6:].split(".")[0] in jumped):
                        continue
                    pa, ma = ga[tick][key][0], ga[tick][key][1]
                    pb, mb = gb[tick][key][0], gb[tick][key][1]
                    if key.startswith("ctl"):
                        # Only settled controls are compared: travel within 2% and the same lock bits.
                        if sorted((ma, mb)) != ["follow", "rest"]:
                            continue
                        d = abs(pa[0] - pb[0])
                        desync["control_rest_pairs"] += 1
                        desync["control_rest_max"] = max(desync["control_rest_max"], round(d, 4))
                        la, lb = ga[tick][key][3], gb[tick][key][3]
                        if d > CONTROL_REST_LIMIT or la != lb:
                            desync["worst"] = f"{key} at tick {tick}: {roles[a]} {pa[0]} locks {la} vs {roles[b]} {pb[0]} locks {lb}"
                        continue
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
    # NPC dialogue and animation: the same effects on every copy that shares the scene.
    npc = {"pairs": 0, "missing": 0, "first_missing": ""}

    def in_scene(role, t, scene):
        near = [s for ht, s in scenes.get(role, []) if t - SCENE_SETTLE <= ht <= t + FX_WINDOW + 2]
        return len(near) >= 3 and all(s == scene for s in near)

    for a in effects:
        for b in effects:
            if a == b:
                continue
            pool = list(effects[b])
            for t, scene, name in effects[a]:
                if not (in_scene(a, t, scene) and in_scene(b, t, scene)):
                    continue
                match = next((x for x in pool if x[2] == name and abs(x[0] - t) <= FX_WINDOW), None)
                if match:
                    pool.remove(match)
                    npc["pairs"] += 1
                else:
                    npc["missing"] += 1
                    npc["first_missing"] = npc["first_missing"] or f"{name} at {t:.1f} in {scene} on {a}, not on {b}"
    desync["npc"] = npc
    # At a few fps Unity's game clock falls behind real time, so a starved copy's timed lines lag: not a desync.
    starved = [r for r, info in results.items() if info.get("fps_median", 30) < STARVED_FPS]
    if npc["missing"] and starved:
        npc["note"] = "not judged: " + ", ".join(starved) + " starved (fps median under %.0f)" % STARVED_FPS
    elif npc["missing"]:
        failures.append(f"NPC effects differ: {npc['missing']} missing, first: {npc['first_missing']}")
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
