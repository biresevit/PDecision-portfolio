"""
Multi-table integration test for the PDecision REST API.

Plays NLH hands on two tables in parallel. The same three bot accounts sit at both tables, together with one
scripted human per table. Every bot behaves like its own client: after each change it sends its view of the table
(itself as seat 0) to /api/handhistory, and when it is to act it asks /api/decision and the action is played.

Then it checks the hand histories the service saved: one copy per hand and bot, with the right players, actions,
board and winners, and no mix-up between the tables.

By default the script starts PDecision itself with the Azure upload switched off and a local copy of every saved
hand enabled, so no test hands reach the shared hand-history pool:

    python tests/multitable_test.py
    python tests/multitable_test.py --hands 10 --exe path/to/PDecision.exe

The bot names must exist in dbo.BotAccount for casino BluffPoker, and appsettings.json must map
RunYourOwnPokerSite to BluffPoker (Api:PlatformCasinos).
"""
import argparse
import itertools
import json
import os
import random
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor

BOTS = ["BluffBot01", "BluffBot02", "BluffBot03"]
SEATS = 6
PLATFORM_RUN_YOUR_OWN = 9      # mapped to BluffPoker in appsettings.json
GAME_NLH = 2
TABLE_CASH = 1
SB, BB, START_STACK = 0.5, 1.0, 100.0
RANKS = "23456789tjqka"
SUITS = "cdhs"
FOLD, CALL, CHECK, BET, RAISE = 0, 1, 2, 3, 4
ACTION_NAMES = {FOLD: "folds", CALL: "calls", CHECK: "checks", BET: "bets", RAISE: "raises"}

DEFAULT_EXE = os.path.join(os.path.dirname(__file__), "..", "PDecision", "bin", "Debug", "net8.0-windows", "PDecision.exe")


# ---------------------------------------------------------------- HTTP

class Api:
    def __init__(self, base):
        self.base = base
        self.lock = threading.Lock()
        self.calls = Counter()
        self.errors = []

    def request(self, method, path, body=None, timeout=60):
        data = None if body is None else json.dumps(body).encode("utf-8")
        req = urllib.request.Request(self.base + path, data=data, method=method,
                                     headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=timeout) as reply:
                status, text = reply.status, reply.read().decode("utf-8")
        except urllib.error.HTTPError as e:
            status, text = e.code, e.read().decode("utf-8")
        with self.lock:
            self.calls[(path, status)] += 1
            if status != 200:
                self.errors.append(f"{path} -> HTTP {status}: {text[:200]}")
        return status, text

    def health(self):
        try:
            with urllib.request.urlopen(self.base + "/api/health", timeout=5) as reply:
                return json.loads(reply.read())["status"]
        except urllib.error.HTTPError as e:
            return json.loads(e.read()).get("status", "unknown")
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            return "down"


# ---------------------------------------------------------------- hand evaluation

def hand_rank(cards):
    """Rank of the best 5-card hand out of 5-7 cards, as a comparable tuple."""
    best = None
    for combo in itertools.combinations(cards, 5):
        values = sorted((RANKS.index(c[0]) for c in combo), reverse=True)
        counts = Counter(values)
        groups = sorted(counts.items(), key=lambda kv: (kv[1], kv[0]), reverse=True)
        flush = len({c[1] for c in combo}) == 1
        unique = sorted(set(values), reverse=True)
        straight_high = None
        if len(unique) == 5 and unique[0] - unique[4] == 4:
            straight_high = unique[0]
        elif unique == [12, 3, 2, 1, 0]:
            straight_high = 3
        if straight_high is not None and flush:
            rank = (8, straight_high)
        elif groups[0][1] == 4:
            rank = (7, groups[0][0], groups[1][0])
        elif groups[0][1] == 3 and groups[1][1] == 2:
            rank = (6, groups[0][0], groups[1][0])
        elif flush:
            rank = (5, *values)
        elif straight_high is not None:
            rank = (4, straight_high)
        elif groups[0][1] == 3:
            rank = (3, groups[0][0], *[v for v, _ in groups[1:]])
        elif groups[0][1] == 2 and groups[1][1] == 2:
            rank = (2, groups[0][0], groups[1][0], groups[2][0])
        elif groups[0][1] == 2:
            rank = (1, groups[0][0], *[v for v, _ in groups[1:]])
        else:
            rank = (0, *values)
        best = rank if best is None or rank > best else best
    return best


# ---------------------------------------------------------------- table simulation

class Player:
    def __init__(self, name, seat, is_bot):
        self.name, self.seat, self.is_bot = name, seat, is_bot
        self.stack = START_STACK
        self.bet = 0.0
        self.in_hand = True
        self.hole = []

    @property
    def all_in(self):
        return self.in_hand and self.stack <= 1e-9


class Table:
    def __init__(self, table_id, human, api, rng, hand_ids, log):
        self.table_id, self.api, self.rng, self.log = table_id, api, rng, log
        self.hand_ids = hand_ids
        # 6-max table: three bots, one human, two empty seats
        self.players = {0: Player(BOTS[0], 0, True), 1: Player(BOTS[1], 1, True),
                        3: Player(BOTS[2], 3, True), 4: Player(human, 4, False)}
        self.dealer = 4
        self.expected = []          # one dict per played hand
        self.anomalies = []
        self.decisions = 0
        self.frames = 0
        self.dump = None                  # optional file for every hand-history frame sent (--dump-frames)
        self.evenbet_settlement = False   # settle hands as EvenBet does (--evenbet-settlement)
        self.rejected = Counter()         # bot -> frames answered with accepted:false
        self.first_rejected = {}          # (hand, bot) -> first rejected frame
        self.pool = ThreadPoolExecutor(len(BOTS))

    # ---- state for one bot's point of view
    def view(self, hero_seat, hand_id, new_hand_for, my_turn_seat, showdowns=None, winners=None):
        def rot(fn):
            return [fn(self.players.get((hero_seat + i) % SEATS)) for i in range(SEATS)]

        hero = self.players[hero_seat]
        cards = [hero.hole[0], hero.hole[1]] + self.board + [""] * (5 - len(self.board))
        names = rot(lambda p: p.name if p else "")
        return {
            "TableID": self.table_id, "TableName": self.table_id, "TableHandle": 0,
            "HandID": hand_id, "IsNewHand": hero_seat in new_hand_for, "IsMyTurn": my_turn_seat == hero_seat,
            "Platform": PLATFORM_RUN_YOUR_OWN, "GameType": GAME_NLH, "TableType": TABLE_CASH,
            "BBSize": BB, "SBSize": SB, "Ante": 0, "Straddle": 0,
            "DealerID": (self.dealer - hero_seat) % SEATS,
            "Names": names, "OriginalNames": list(names),
            "EmptySeats": rot(lambda p: p is None),
            "InGame": rot(lambda p: bool(p and p.in_hand)),
            "Stacks": rot(lambda p: round(p.stack, 2) if p else 0),
            "Bets": rot(lambda p: round(p.bet, 2) if p else 0),
            "Pot": round(self.pot, 2),
            "Cards": cards, "OmahaCards": [],
            "Showdowns": rot(lambda p: "".join(p.hole) if p and showdowns and p.seat in showdowns else ""),
            "Winners": rot(lambda p: round(winners.get(p.seat, 0), 2) if p and winners else 0),
            "PlayersLeft": 0, "PlayersTotal": 0, "NumPlacesPaid": 0,
        }

    def send_frames(self, hand_id, new_hand_for=(), my_turn_seat=None, showdowns=None, winners=None, complete=False):
        """Every bot sends its own view to /api/handhistory, in parallel like separate clients."""
        bots = [p.seat for p in self.players.values() if p.is_bot]
        states = [self.view(s, hand_id, new_hand_for, my_turn_seat, showdowns, winners) for s in bots]
        replies = list(self.pool.map(lambda st: self.api.request("POST", "/api/handhistory", st), states))
        if complete:
            done = list(self.pool.map(lambda st: self.api.request("POST", "/api/handhistory/complete", st), states))
            for st, (status, text) in zip(states, done):
                if status != 200 or '"accepted":true' not in text:
                    self.anomalies.append(f"{st['OriginalNames'][0]} hand {hand_id}: complete not accepted: {status} {text[:100]}")
        self.frames += len(replies)
        if self.dump:
            for st in states:
                self.dump.write(json.dumps(st) + "\n")
        for st, (status, text) in zip(states, replies):
            if status == 200 and '"accepted":false' in text:
                self.rejected[st["OriginalNames"][0]] += 1
                key = (hand_id, st["OriginalNames"][0])
                if key not in self.first_rejected:
                    self.first_rejected[key] = st

    # ---- one hand
    def occupied_after(self, seat):
        for i in range(1, SEATS + 1):
            s = (seat + i) % SEATS
            if s in self.players:
                return s

    def play_hand(self, hand_id):
        deck = [r + s for r in RANKS for s in SUITS]
        self.rng.shuffle(deck)
        for p in self.players.values():
            p.stack, p.bet, p.in_hand, p.hole = START_STACK, 0.0, True, [deck.pop(), deck.pop()]
        self.board, self.pot = [], 0.0
        self.dealer = self.occupied_after(self.dealer)
        sb_seat = self.occupied_after(self.dealer)
        bb_seat = self.occupied_after(sb_seat)
        for seat, amount in ((sb_seat, SB), (bb_seat, BB)):
            self.players[seat].stack -= amount
            self.players[seat].bet = amount
        record = {"hand_id": hand_id, "table": self.table_id, "players": sorted(p.name for p in self.players.values()),
                  "actions": [], "board": [], "winners": {}}

        first = self.occupied_after(bb_seat)
        self.send_frames(hand_id, new_hand_for={p.seat for p in self.players.values()}, my_turn_seat=self.first_to_act(first))
        for street in range(4):
            if street > 0:
                # next street: bets go to the pot, the new cards are dealt, then the table shows it
                self.collect_bets()
                self.board += [deck.pop() for _ in range(3 if street == 1 else 1)]
                first = self.occupied_after(self.dealer)
                betting = self.can_act_count() > 1
                self.send_frames(hand_id, my_turn_seat=self.first_to_act(first) if betting else None)
            if street == 0 or self.can_act_count() > 1:
                self.betting_round(hand_id, first, street, record)
            if self.active_count() == 1:
                break
        record["board"] = list(self.board)

        # the last frame keeps the street's bets in front of the players, like the table does until the pot is pushed
        total = self.pot + sum(p.bet for p in self.players.values())
        alive = [p for p in self.players.values() if p.in_hand]
        if len(alive) == 1:
            winners = {alive[0].seat: total}
            showdowns = None
        else:
            # run out the board when players are all in before the river
            while len(self.board) < 5:
                self.board.append(deck.pop())
            record["board"] = list(self.board)
            ranks = {p.seat: hand_rank(p.hole + self.board) for p in alive}
            top = max(ranks.values())
            best = [s for s, r in ranks.items() if r == top]
            winners = {s: total / len(best) for s in best}
            showdowns = {p.seat for p in alive}
        record["winners"] = {self.players[s].name: round(v, 2) for s, v in winners.items()}
        record["shows"] = sorted(self.players[s].name for s in showdowns) if showdowns else []
        if self.evenbet_settlement:
            # As EvenBet settles: bets swept, pot already paid into the winners' stacks, then the
            # client reports the settlement frame to /api/handhistory and /api/handhistory/complete.
            for p in self.players.values():
                p.bet = 0.0
            self.pot = 0.0
            for s, amount in winners.items():
                self.players[s].stack += amount
            self.send_frames(hand_id, showdowns=showdowns, winners=winners, complete=True)
        else:
            self.send_frames(hand_id, showdowns=showdowns, winners=winners)
        self.expected.append(record)
        self.log(f"{self.table_id} hand {hand_id}: {len(record['actions'])} actions, board {' '.join(self.board) or '-'}, "
                 f"winners {record['winners']}")

    def active_count(self):
        return sum(p.in_hand for p in self.players.values())

    def can_act_count(self):
        return sum(p.in_hand and not p.all_in for p in self.players.values())

    def first_to_act(self, seat):
        for i in range(SEATS):
            s = (seat + i) % SEATS
            p = self.players.get(s)
            if p and p.in_hand and not p.all_in:
                return s
        return None

    def collect_bets(self):
        for p in self.players.values():
            self.pot += p.bet
            p.bet = 0.0

    def betting_round(self, hand_id, first, street, record):
        order = [s for s in ((first + i) % SEATS for i in range(SEATS)) if s in self.players]
        pending = [s for s in order if self.players[s].in_hand and not self.players[s].all_in]
        min_raise = BB
        while pending and self.active_count() > 1:
            seat = pending.pop(0)
            p = self.players[seat]
            if not p.in_hand or p.all_in:
                continue
            max_bet = max(q.bet for q in self.players.values())
            to_call = max_bet - p.bet
            if p.is_bot:
                kind, target = self.bot_action(hand_id, p, to_call, max_bet, min_raise)
            else:
                kind, target = self.human_action(p, to_call, max_bet, min_raise)

            if kind == FOLD:
                p.in_hand = False
            elif kind == CHECK:
                pass
            elif kind == CALL:
                amount = min(to_call, p.stack)
                p.stack -= amount
                p.bet += amount
            else:   # BET / RAISE to target
                target = min(target, p.bet + p.stack)
                min_raise = max(min_raise, target - max_bet)
                p.stack -= target - p.bet
                p.bet = target
                # everybody else still in the hand acts again
                idx = order.index(seat)
                pending = [s for s in order[idx + 1:] + order[:idx]
                           if self.players[s].in_hand and not self.players[s].all_in]
            record["actions"].append((p.name, ACTION_NAMES[kind]))

            if self.active_count() > 1:
                nxt = next((s for s in pending if self.players[s].in_hand and not self.players[s].all_in), None)
                self.send_frames(hand_id, my_turn_seat=nxt)

    def human_action(self, p, to_call, max_bet, min_raise):
        if to_call <= 0:
            if self.rng.random() < 0.25:
                return (BET if max_bet == 0 else RAISE), max_bet + max(BB, round(self.pot * 0.5, 2))
            return CHECK, 0
        return (CALL, 0) if to_call <= 4 * BB else (FOLD, 0)

    def bot_action(self, hand_id, p, to_call, max_bet, min_raise):
        state = self.view(p.seat, hand_id, (), p.seat)
        status, text = self.api.request("POST", "/api/decision", state)
        self.decisions += 1
        if status != 200:
            self.anomalies.append(f"{p.name} hand {hand_id}: decision HTTP {status}")
            return (CHECK, 0) if to_call <= 0 else (FOLD, 0)
        d = json.loads(text)
        action, size = d["Action"], d["BetSize"]
        if action == FOLD:
            if to_call <= 0:
                self.anomalies.append(f"{p.name} hand {hand_id}: FOLD when check was free, played as check")
                return CHECK, 0
            return FOLD, 0
        if action == CHECK:
            if to_call > 0:
                self.anomalies.append(f"{p.name} hand {hand_id}: CHECK facing a bet, played as fold")
                return FOLD, 0
            return CHECK, 0
        if action == CALL:
            return (CHECK, 0) if to_call <= 0 else (CALL, 0)
        if action in (BET, RAISE):
            if size <= max_bet:
                self.anomalies.append(f"{p.name} hand {hand_id}: {ACTION_NAMES[action]} to {size} not above {max_bet}, played as call/check")
                return (CHECK, 0) if to_call <= 0 else (CALL, 0)
            if size - max_bet < min_raise and size < p.bet + p.stack:
                self.anomalies.append(f"{p.name} hand {hand_id}: raise to {size} below min raise, played as min raise")
                size = max_bet + min_raise
            return (BET if max_bet == 0 else RAISE), size
        self.anomalies.append(f"{p.name} hand {hand_id}: action {action}, played as check/fold")
        return (CHECK, 0) if to_call <= 0 else (FOLD, 0)

    def run(self, num_hands):
        for _ in range(num_hands):
            self.play_hand(self.hand_ids.pop(0))
        # a hand is saved when the next hand starts, so start one more hand to flush the last one
        flush_id = self.hand_ids.pop(0)
        deck = [r + s for r in RANKS for s in SUITS]
        self.rng.shuffle(deck)
        for p in self.players.values():
            p.stack, p.bet, p.in_hand, p.hole = START_STACK, 0.0, True, [deck.pop(), deck.pop()]
        self.board, self.pot = [], 0.0
        self.dealer = self.occupied_after(self.dealer)
        sb_seat = self.occupied_after(self.dealer)
        bb_seat = self.occupied_after(sb_seat)
        self.players[sb_seat].stack -= SB
        self.players[sb_seat].bet = SB
        self.players[bb_seat].stack -= BB
        self.players[bb_seat].bet = BB
        self.send_frames(flush_id, new_hand_for={p.seat for p in self.players.values()})


# ---------------------------------------------------------------- checks

ACTION_RE = re.compile(r"^(\S+) (folds|checks|calls|bets|raises)\b", re.M)
WIN_RE = re.compile(r"^(\S+) wins \$([\d.]+) USD", re.M)
SHOWS_RE = re.compile(r"^(\S+) shows \[", re.M)
SEAT_RE = re.compile(r"^Seat \d+: (\S+) \(", re.M)
BOARD_RE = re.compile(r"\*\* Dealing (?:Flop|Turn|River) \*\* \[ ([^\]]*) \]")


def parse_hh(text):
    board = []
    for match in BOARD_RE.findall(text):
        board += [c.strip().lower() for c in match.split(",") if c.strip()]
    return {
        "players": sorted(SEAT_RE.findall(text)),
        "actions": ACTION_RE.findall(text),
        "board": board,
        "winners": {n: float(v) for n, v in WIN_RE.findall(text)},
        "shows": sorted(set(SHOWS_RE.findall(text))),
    }


def check(tables, hh_dir, results):
    ok = True

    def fail(msg):
        nonlocal ok
        ok = False
        results.append("FAIL  " + msg)

    for table in tables:
        for exp in table.expected:
            files = [f for f in os.listdir(hh_dir) if f.startswith(exp["hand_id"] + "_")] if os.path.isdir(hh_dir) else []
            if len(files) != len(BOTS):
                fail(f"{table.table_id} hand {exp['hand_id']}: {len(files)} saved copies, expected {len(BOTS)} (one per bot): {files}")
                if not files:
                    continue
            parsed = {}
            for f in files:
                with open(os.path.join(hh_dir, f), encoding="utf-8") as fh:
                    text = fh.read()
                if f"#Game No : {exp['hand_id']}" not in text:
                    fail(f"{f}: hand ID line missing")
                parsed[f] = parse_hh(text)
                hh = parsed[f]
                if hh["players"] != exp["players"]:
                    fail(f"{f}: players {hh['players']}, expected {exp['players']}")
                if hh["actions"] != exp["actions"]:
                    fail(f"{f}: actions differ\n        saved:    {hh['actions']}\n        expected: {exp['actions']}")
                if hh["board"] != exp["board"]:
                    fail(f"{f}: board {hh['board']}, expected {exp['board']}")
                if exp.get("shows") and not set(exp["shows"]) <= set(hh["shows"]):
                    fail(f"{f}: shown cards for {hh['shows']}, expected at least {exp['shows']}")
                if set(hh["winners"]) != set(exp["winners"]):
                    fail(f"{f}: winners {hh['winners']}, expected {exp['winners']}")
                else:
                    for name, amount in exp["winners"].items():
                        # the saved amount may have rake taken off
                        if not (amount * 0.9 - 0.01 <= hh["winners"][name] <= amount + 0.01):
                            fail(f"{f}: {name} wins {hh['winners'][name]}, expected about {amount}")
            # copies from the three bots must describe the same hand
            views = {json.dumps(v, sort_keys=True) for v in parsed.values()}
            if len(views) > 1:
                fail(f"{table.table_id} hand {exp['hand_id']}: the bots' copies differ: {list(parsed)}")
    return ok


# ---------------------------------------------------------------- main

def start_service(exe, hh_copy_dir, log, upload=False):
    env = dict(os.environ)
    env["PDECISION_HandHistory__UploadToAzure"] = "true" if upload else "false"
    env["PDECISION_HandHistory__LocalCopyDir"] = hh_copy_dir
    exe = os.path.abspath(exe)
    log(f"starting {exe}")
    return subprocess.Popen([exe], cwd=os.path.dirname(exe), env=env)


def main():
    global BOTS
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--hands", type=int, default=6, help="hands per table (default 6)")
    ap.add_argument("--bots", nargs=3, help="three existing bot names for the registered customer's casino")
    ap.add_argument("--base", default="http://127.0.0.1:5005", help="API address")
    ap.add_argument("--exe", default=DEFAULT_EXE, help="PDecision.exe to start")
    ap.add_argument("--no-start", action="store_true",
                    help="use an already running service; it must run with HandHistory:LocalCopyDir set to --hh-dir")
    ap.add_argument("--hh-dir", help="local hand-history copy folder (default: a new temp folder)")
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--keep", action="store_true", help="keep the hand-history copies after the test")
    ap.add_argument("--upload", action="store_true",
                    help="also upload the hands to this machine's customer area in Azure (for testing the sharing between a customer's machines)")
    ap.add_argument("--evenbet-settlement", action="store_true",
                    help="settle hands as EvenBet does: the settlement frame has the pot already paid out and is also sent to /api/handhistory/complete")
    ap.add_argument("--dump-frames", help="write every hand-history frame sent to this folder, one JSON-lines file per table")
    args = ap.parse_args()
    if args.bots:
        BOTS = args.bots

    started = time.time()
    lock = threading.Lock()

    def log(msg):
        with lock:
            print(f"[{time.time() - started:6.1f}s] {msg}", flush=True)

    seed = args.seed if args.seed is not None else random.randrange(10**6)
    hh_root = args.hh_dir or tempfile.mkdtemp(prefix="pdecision_hh_")
    # PDecision writes local copies under a folder named after the configured casino (e.g. "Evenbet").
    hh_dir = os.path.join(hh_root, "BluffPoker")
    log(f"seed {seed}, hand-history copies under {hh_root}")

    api = Api(args.base)
    proc = None
    if not args.no_start:
        if api.health() != "down":
            sys.exit("a PDecision service already answers on " + args.base + ": stop it, or use --no-start")
        proc = start_service(args.exe, hh_root, log, upload=args.upload)
    try:
        while True:
            status = api.health()
            if status == "ready":
                break
            if status == "failed" or (proc and proc.poll() is not None):
                sys.exit(f"service did not start: {status}")
            if time.time() - started > 1800:
                sys.exit("service not ready after 30 minutes")
            time.sleep(5)
        log("service ready")

        base_id = random.Random(seed).randrange(10**8, 9 * 10**8)
        tables = [
            Table("test-A", "Human_A", api, random.Random(seed), [str(base_id + i) for i in range(args.hands + 1)], log),
            Table("test-B", "Human_B", api, random.Random(seed + 1), [str(base_id + 1000 + i) for i in range(args.hands + 1)], log),
        ]
        for t in tables:
            t.evenbet_settlement = args.evenbet_settlement
        if args.dump_frames:
            os.makedirs(args.dump_frames, exist_ok=True)
            for t in tables:
                t.dump = open(os.path.join(args.dump_frames, t.table_id + ".jsonl"), "w", encoding="utf-8")
        t0 = time.time()
        with ThreadPoolExecutor(len(tables)) as ex:
            for f in [ex.submit(t.run, args.hands) for t in tables]:
                f.result()
        elapsed = time.time() - t0
        log(f"played {args.hands} hands on each of {len(tables)} tables in {elapsed:.0f}s")

        results = []
        casinos = [d for d in os.listdir(hh_root) if os.path.isdir(os.path.join(hh_root, d))]
        if len(casinos) == 1:
            hh_dir = os.path.join(hh_root, casinos[0])
        passed = check(tables, hh_dir, results)
        print()
        print("==== results")
        for (path, status), n in sorted(api.calls.items()):
            print(f"  {path:<18} HTTP {status}: {n}")
        print(f"  decisions asked: {sum(t.decisions for t in tables)}")
        for t in tables:
            print(f"  {t.table_id}: {t.frames} hand-history frames, rejected (accepted:false) per bot: {dict(t.rejected) or 'none'}")
            for (hand, bot), st in list(t.first_rejected.items())[:3]:
                print(f"    first rejected frame, hand {hand}, {bot}: board {st['Cards'][2:]}, bets {st['Bets']}, "
                      f"stacks {st['Stacks']}, pot {st['Pot']}, in game {st['InGame']}, new hand {st['IsNewHand']}")
        for t in tables:
            for a in t.anomalies:
                print("  NOTE  " + a)
        for line in results:
            print("  " + line)
        if api.errors:
            passed = False
            for e in api.errors[:10]:
                print("  FAIL  " + e)
        saved = len(os.listdir(hh_dir)) if os.path.isdir(hh_dir) else 0
        print(f"  saved hand-history copies: {saved} (expected {len(BOTS) * args.hands * len(tables)})")
        print("PASSED" if passed else "FAILED")
        return 0 if passed else 1
    finally:
        if proc:
            proc.terminate()
            try:
                proc.wait(15)
            except subprocess.TimeoutExpired:
                proc.kill()
        if not args.keep and not args.hh_dir:
            shutil.rmtree(hh_root, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
