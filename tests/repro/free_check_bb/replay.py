"""Replays the big-blind decisions where every opponent still in the hand is all-in for no more than the
hero's blind (BluffPoker table 15b47093CONFIGURE_LOCALLY, hand 18, 3 Nov 2026) and checks the answers.

Each case folder holds the /api/decision request exactly as PokerController sent it (player names masked
B1..B6, chips not normalised). Hero is seat 0 in the big blind with 2s 7c unless the folder says otherwise.

    python replay.py --exe <path to PDecision.exe>      # starts it on a test port (default 5015)
    python replay.py --base http://127.0.0.1:5015       # uses a PDecision that is already running

The same folders also work with tests/replay_handstates.py --directory <this folder>.
Never point --base at the live 5005 of a playing PC.
"""
import argparse
import json
import os
import subprocess
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
CHECK = 2

# case folder -> (what the situation is, whether the answer must be a check)
CASES = {
    "1_live_sb_allin_150": ("SB all-in for 150 < BB 200, nothing to call", True),
    "2_sb_allin_equals_bb": ("SB all-in for exactly the BB (200), nothing to call", True),
    "3_aces_sb_allin_150": ("as 1 but hero holds As Ah: a raise would only be returned, so check", True),
    "4_sb_limp_chips_behind": ("SB limped to 200 with chips behind: normal strategy (checks already)", True),
    "5_sb_allin_above_bb": ("SB all-in for 500 > BB: hero must call or fold, never the free check", False),
}


def call(base, path, body=None):
    request = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None,
                                     headers={"Content-Type": "application/json"}, method="POST" if body is not None else "GET")
    with urllib.request.urlopen(request, timeout=120) as response:
        return json.loads(response.read())


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--exe", help="PDecision.exe to start on the test port")
    parser.add_argument("--base", default="http://127.0.0.1:5015")
    args = parser.parse_args()

    proc = None
    if args.exe:
        env = dict(os.environ, PDECISION_Api__Urls=args.base, PDECISION_Api__HumanLikeDelays="false",
                   PDECISION_HandHistory__UploadToAzure="false",
                   PDECISION_HandHistory__LocalCopyDir=os.path.join(os.environ.get("TEMP", HERE), "pdecision_repro_hh"))
        proc = subprocess.Popen([os.path.abspath(args.exe)], cwd=os.path.dirname(os.path.abspath(args.exe)), env=env)
    failures = 0
    try:
        for _ in range(360):
            try:
                if call(args.base, "/api/health").get("status") == "ready":
                    break
            except Exception:
                pass
            time.sleep(5)
        else:
            raise SystemExit("PDecision did not become ready")
        for case, (what, must_check) in CASES.items():
            frame = json.load(open(os.path.join(HERE, case, "0.json"), encoding="utf-8-sig"))
            decision = call(args.base, "/api/decision", frame)
            kind = {0: "FOLD", 1: "CALL", 2: "CHECK", 3: "BET", 4: "RAISE", 6: "EXCEPTION"}.get(decision.get("Action"), decision.get("Action"))
            passed = (decision.get("Action") == CHECK) == must_check
            failures += not passed
            print(f"{'PASS' if passed else 'FAIL'} {case}: bets {frame['Bets']} -> {kind} {decision.get('BetSize')}  ({what})")
    finally:
        if proc:
            proc.kill()
    if failures:
        raise SystemExit(f"{failures} case(s) failed")


if __name__ == "__main__":
    main()
