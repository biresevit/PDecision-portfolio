"""
Minimal heads-up NLH test to exercise the remote Texas solver.

Plays one hand, bot vs. human, nobody else seated, so the hand is heads-up from the first
card: on NLH, turn and river decisions always go to the remote solver (see README's game ->
solver table), so this reliably produces a request to the Texas solver server.

Requires PDecision to already be running (it is NOT started by this script) and a Texas
solver server to be running and reachable via the same RabbitMQUri as PDecision's
appsettings.json. Watch both services' logs while this runs.

    python tests/heads_up_solver_test.py
    python tests/heads_up_solver_test.py --base http://127.0.0.1:5005
"""
import argparse
import json
import random
import socket
import time
import urllib.error
import urllib.request

BOT = "BluffBot01"
HUMAN = "Human_HU"
SEATS = 6
PLATFORM_RUN_YOUR_OWN = 9
GAME_NLH = 2
TABLE_CASH = 1
SB, BB = 0.5, 1.0
START_STACK = 100.0  # overridden by --stack
RANKS = "23456789tjqka"
SUITS = "cdhs"
FOLD, CALL, CHECK, BET, RAISE = 0, 1, 2, 3, 4
ACTION_NAMES = {FOLD: "folds", CALL: "calls", CHECK: "checks", BET: "bets", RAISE: "raises"}


class RequestTimeout(RuntimeError):
    """The PDecision request did not complete within the client timeout."""


class RequestFailed(RuntimeError):
    """The request could not be sent or returned an invalid response."""


def request(base, method, path, body, timeout=60, api_key=""):
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(base + path, data=data, method=method,
                                  headers={"Content-Type": "application/json",
                                           **({"X-Api-Key": api_key} if api_key else {})})
    started = time.monotonic()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as reply:
            return reply.status, reply.read().decode("utf-8"), time.monotonic() - started
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8"), time.monotonic() - started
    except (socket.timeout, TimeoutError) as e:
        raise RequestTimeout(f"{method} {path} timed out after {timeout}s") from e
    except urllib.error.URLError as e:
        if isinstance(e.reason, (socket.timeout, TimeoutError)) or "timed out" in str(e.reason).lower():
            raise RequestTimeout(f"{method} {path} timed out after {timeout}s") from e
        raise RequestFailed(f"{method} {path} failed: {e.reason}") from e


class Player:
    def __init__(self, name, seat):
        self.name, self.seat = name, seat
        self.stack, self.bet, self.in_hand = START_STACK, 0.0, True
        self.hole = []

    @property
    def all_in(self):
        return self.in_hand and self.stack <= 1e-9


class HeadsUpTable:
    def __init__(self, base, hand_id, rng, log, api_key="", timeout=60):
        self.base, self.hand_id, self.rng, self.log = base, hand_id, rng, log
        self.api_key, self.timeout = api_key, timeout
        self.players = {0: Player(BOT, 0), 1: Player(HUMAN, 1)}
        self.dealer = 0  # bot is button/SB first hand
        self.board, self.pot = [], 0.0
        self.solver_streets = []

    def other(self, seat):
        return 1 - seat

    def view(self, hero_seat, new_hand, my_turn_seat):
        def rot(fn):
            # Preserve the physical six-seat rotation used by the REST client.
            order = [(hero_seat + i) % SEATS for i in range(SEATS)]
            return [fn(self.players.get(s)) for s in order]

        hero = self.players[hero_seat]
        cards = [hero.hole[0], hero.hole[1]] + self.board + [""] * (5 - len(self.board))
        names = rot(lambda p: p.name if p else "")
        return {
            "TableID": "heads-up-test", "TableName": "heads-up-test", "TableHandle": 0,
            "HandID": self.hand_id, "IsNewHand": new_hand, "IsMyTurn": my_turn_seat == hero_seat,
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
            "Showdowns": [""] * SEATS, "Winners": [0] * SEATS,
            "PlayersLeft": 0, "PlayersTotal": 0, "NumPlacesPaid": 0,
        }

    def send_frames(self, new_hand=False, my_turn_seat=None):
        for seat in (0, 1):
            status, text, elapsed = request(self.base, "POST", "/api/handhistory",
                                            self.view(seat, new_hand, my_turn_seat),
                                            timeout=self.timeout, api_key=self.api_key)
            if status != 200 or '"accepted":false' in text:
                self.log(f"  handhistory for seat {seat} -> HTTP {status}: {text[:200]}")

    def bot_decision(self, street, to_call, max_bet, min_raise):
        state = self.view(0, False, 0)
        self.log(f"  asking /api/decision (street cards {state['Cards'][2:]}, pot {self.pot})")
        status, text, elapsed = request(self.base, "POST", "/api/decision", state,
                                        timeout=self.timeout, api_key=self.api_key)
        if status != 200:
            raise RequestFailed(f"/api/decision on {street} returned HTTP {status}: {text[:300]}")
        try:
            d = json.loads(text)
            action, size = int(d["Action"]), float(d["BetSize"])
        except (ValueError, TypeError, KeyError, json.JSONDecodeError) as e:
            raise RequestFailed(f"/api/decision on {street} returned invalid JSON: {text[:300]}") from e
        if action not in ACTION_NAMES:
            raise RequestFailed(f"/api/decision on {street} returned unknown Action={action}")
        self.log(f"  decision reply ({elapsed:.1f}s): {text}")
        if street in ("turn", "river"):
            self.solver_streets.append((street, elapsed, action))

        # This is a transport test, not a strategy test. Keep the scripted
        # hand alive after a fold so both turn and river are exercised.
        if action == FOLD:
            self.log(f"  {street}: solver returned fold; continuing as "
                     f"{'check' if to_call <= 0 else 'call'} to reach the next street")
            return (CHECK, 0) if to_call <= 0 else (CALL, 0)
        if action == CHECK:
            return (CHECK, 0) if to_call <= 0 else (CALL, 0)
        if action == CALL:
            return (CHECK, 0) if to_call <= 0 else (CALL, 0)
        if action in (BET, RAISE):
            if size <= max_bet or (size - max_bet < min_raise and size < self.players[0].bet + self.players[0].stack):
                size = max_bet + max(min_raise, BB)
            return (BET if max_bet == 0 else RAISE), size
        return (CHECK, 0) if to_call <= 0 else (FOLD, 0)

    def human_action(self, to_call, max_bet):
        # human always calls/checks through to showdown, so we reliably reach turn and river
        return (CHECK, 0) if to_call <= 0 else (CALL, 0)

    def betting_round(self, first_seat, street):
        order = [first_seat, self.other(first_seat)]
        pending = [s for s in order if self.players[s].in_hand and not self.players[s].all_in]
        min_raise = BB
        while pending and sum(p.in_hand for p in self.players.values()) > 1:
            seat = pending.pop(0)
            p = self.players[seat]
            if not p.in_hand or p.all_in:
                continue
            max_bet = max(q.bet for q in self.players.values())
            to_call = max_bet - p.bet
            if seat == 0:
                kind, target = self.bot_decision(street, to_call, max_bet, min_raise)
            else:
                kind, target = self.human_action(to_call, max_bet)

            if kind == FOLD:
                p.in_hand = False
            elif kind == CHECK:
                pass
            elif kind == CALL:
                amount = min(to_call, p.stack)
                p.stack -= amount
                p.bet += amount
            else:
                target = min(target, p.bet + p.stack)
                min_raise = max(min_raise, target - max_bet)
                p.stack -= target - p.bet
                p.bet = target
                other = self.other(seat)
                if self.players[other].in_hand and not self.players[other].all_in:
                    pending = [other]
            self.log(f"  {p.name} {ACTION_NAMES[kind]}" + (f" to {target}" if kind in (BET, RAISE) else ""))
            if sum(p.in_hand for p in self.players.values()) > 1:
                nxt = next((s for s in pending if self.players[s].in_hand and not self.players[s].all_in), None)
                self.send_frames(my_turn_seat=nxt)

    def collect_bets(self):
        for p in self.players.values():
            self.pot += p.bet
            p.bet = 0.0

    def play(self):
        deck = [r + s for r in RANKS for s in SUITS]
        self.rng.shuffle(deck)
        for p in self.players.values():
            p.stack, p.bet, p.in_hand, p.hole = START_STACK, 0.0, True, [deck.pop(), deck.pop()]
        self.board, self.pot = [], 0.0

        sb_seat, bb_seat = self.dealer, self.other(self.dealer)  # heads-up: dealer posts SB, acts first preflop
        self.players[sb_seat].stack -= SB
        self.players[sb_seat].bet = SB
        self.players[bb_seat].stack -= BB
        self.players[bb_seat].bet = BB

        self.log(f"=== hand {self.hand_id}: dealer/SB={self.players[sb_seat].name}, BB={self.players[bb_seat].name}")
        self.send_frames(new_hand=True, my_turn_seat=sb_seat)

        for street, name in enumerate(["preflop", "flop", "turn", "river"]):
            if street > 0:
                self.collect_bets()
                self.board += [deck.pop() for _ in range(3 if street == 1 else 1)]
                self.log(f"--- {name}: board {' '.join(self.board)}")
                self.send_frames(my_turn_seat=bb_seat)
            if sum(p.in_hand for p in self.players.values()) == 1:
                break
            self.betting_round(sb_seat if street == 0 else bb_seat, name)
            if sum(p.in_hand for p in self.players.values()) == 1:
                break

        alive = [p for p in self.players.values() if p.in_hand]
        self.log(f"hand {self.hand_id} finished, board {' '.join(self.board)}, alive: {[p.name for p in alive]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--base", default="http://127.0.0.1:5005", help="PDecision API address")
    ap.add_argument("--hands", type=int, default=1)
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--stack", type=float, default=100.0, help="starting stack in BB-equivalent units (default 100)")
    ap.add_argument("--timeout", type=float, default=60.0,
                    help="HTTP timeout for decisions and hand-history frames (default 60s)")
    ap.add_argument("--api-key", default="", help="X-Api-Key when PDecision:Api:ApiKey is configured")
    args = ap.parse_args()

    global START_STACK
    START_STACK = args.stack

    seed = args.seed if args.seed is not None else random.randrange(10**6)
    print(f"seed {seed}")
    rng = random.Random(seed)
    base_id = rng.randrange(10**8, 9 * 10**8)

    def log(msg):
        print(msg, flush=True)

    try:
        with urllib.request.urlopen(args.base + "/api/health", timeout=5) as reply:
            health = json.loads(reply.read())
            log(f"PDecision health: {health}")
            if reply.status != 200 or health.get("status") != "ready":
                log("PDecision is not ready; start it fully before running this test")
                return 1
    except Exception as e:
        log(f"PDecision not reachable at {args.base}: {e}")
        return 1

    try:
        for i in range(args.hands):
            table = HeadsUpTable(args.base, str(base_id + i), rng, log,
                                 api_key=args.api_key, timeout=args.timeout)
            table.play()
            streets = {street for street, _, _ in table.solver_streets}
            if streets != {"turn", "river"}:
                raise RequestFailed("the hand did not produce both turn and river decisions; "
                                    f"observed {sorted(streets)}")
            log("solver transport OK: turn and river replies received")
    except RequestTimeout as e:
        log(f"SOLVER TIMEOUT: {e}")
        return 2
    except RequestFailed as e:
        log(f"SOLVER/API FAILURE: {e}")
        return 1
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as e:
        log(f"TEST FAILURE: {e}")
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
