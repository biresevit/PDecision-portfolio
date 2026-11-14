"""Replays the decision requests of the Fifty50 SNG (tournament 128901414, table 128360738, 2 Nov 2026)
that showed PDecision keeping the first blind level's blinds.

The frames are exactly what PokerController sent to /api/decision (saved by PDecision under
C:\\PokerSuite\\HandStates). Per bot, the first file is a level-1 hand (blinds 0.5/1), which creates the
table's DecisionMaker; the second is a level-2 hand (blinds 1/2).

    python replay.py --exe <path to PDecision.exe>      # starts it on a test port (default 5015)
    python replay.py --base http://127.0.0.1:5015       # uses a PDecision that is already running

Never point --base at the live 5005 of a playing PC: the replay creates DecisionMakers for john and andrew.
"""
import argparse
import glob
import json
import os
import subprocess
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))


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
        for path in sorted(glob.glob(os.path.join(HERE, "*.json"))):
            frame = json.load(open(path, encoding="utf-8-sig"))
            decision = call(args.base, "/api/decision", frame)
            kind = {0: "FOLD", 1: "CALL", 2: "CHECK", 3: "BET", 4: "RAISE", 6: "EXCEPTION"}.get(decision.get("Action"), decision.get("Action"))
            print(f"{os.path.basename(path)}: blinds {frame['SBSize']}/{frame['BBSize']}, hero bet {frame['Bets'][0]}, "
                  f"bets {frame['Bets']} -> {kind} {decision.get('BetSize')}")
    finally:
        if proc:
            proc.kill()


if __name__ == "__main__":
    main()
