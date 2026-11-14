"""
Checks the hand histories PDecision saved during a PokerTable simulator run against what really happened on the table.

The simulator (PokerTable, REST mode) writes the true order of actions, the players and the board of every finished
hand to %TEMP%/pokertable_rest/<table>_expected.jsonl. PDecision, started with HandHistory:LocalCopyDir, saves one
copy of every hand per bot at the table. Every copy must match the expected hand.

  python tests/check_simulator_hh.py                       # newest simulator run, copies in %TEMP%/pokertable_rest/hh
  python tests/check_simulator_hh.py --expected <file> --hh-dir <dir>
"""
import argparse
import glob
import json
import os
import sys

from multitable_test import parse_hh

DEFAULT_DIR = os.path.join(os.environ.get("TEMP", "."), "pokertable_rest")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--expected", help="expected.jsonl of the run (default: the newest one)")
    ap.add_argument("--hh-dir", default=os.path.join(DEFAULT_DIR, "hh"), help="PDecision HandHistory:LocalCopyDir")
    ap.add_argument("--max-failures", type=int, default=20)
    args = ap.parse_args()

    expected_file = args.expected or max(glob.glob(os.path.join(DEFAULT_DIR, "*_expected.jsonl")), key=os.path.getmtime)
    with open(expected_file, encoding="utf-8") as fh:
        hands = [json.loads(line) for line in fh if line.strip()]
    copies = glob.glob(os.path.join(args.hh_dir, "**", "*.txt"), recursive=True)
    print(f"{expected_file}: {len(hands)} hands, {len(copies)} saved copies in {args.hh_dir}")

    failures = []
    checked = 0
    for exp in hands:
        files = [f for f in copies if os.path.basename(f).startswith(exp["hand_id"] + "_")]
        # every player is a bot, so every player saves a copy
        if len(files) != len(exp["players"]):
            failures.append(f"hand {exp['hand_id']}: {len(files)} saved copies, expected {len(exp['players'])}: "
                            f"{sorted(os.path.basename(f) for f in files)}")
        want_actions = [(name, verb) for name, verb, _street in exp["actions"]]
        for f in files:
            with open(f, encoding="utf-8") as fh:
                text = fh.read()
            if f"Table {exp['table']} " not in text:
                continue  # same hand ID from another simulator session
            checked += 1
            hh = parse_hh(text)
            name = os.path.basename(f)
            if hh["players"] != exp["players"]:
                failures.append(f"{name}: players {hh['players']}, expected {exp['players']}")
            if hh["actions"] != want_actions:
                failures.append(f"{name}: actions differ\n    saved:    {hh['actions']}\n    expected: {want_actions}")
            if hh["board"] != exp["board"]:
                failures.append(f"{name}: board {hh['board']}, expected {exp['board']}")
            if not hh["winners"]:
                failures.append(f"{name}: no winner line")

    for msg in failures[:args.max_failures]:
        print("FAIL  " + msg)
    if len(failures) > args.max_failures:
        print(f"CONFIGURE_LOCALLY and {len(failures) - args.max_failures} more")
    print(f"{checked} copies checked, {len(failures)} failures")
    return 1 if failures or not checked else 0


if __name__ == "__main__":
    sys.exit(main())
