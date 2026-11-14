# Tournament blinds kept at the first level

Decision requests from the Fifty50 SNG (tournament 128901414, table 128360738) on 2 Nov 2026, exactly as
PokerController sent them to `/api/decision` (PDecision saved them under `C:\PokerSuite\HandStates`).
Seat 0 is the hero; amounts are table chips / 100.

| File | Hand | Blinds | What happens |
|---|---|---|---|
| `1_john_163055468_level1_decision.json` | 163055468 | 0.5/1 | First decision at this table: creates john's DecisionMaker with BB 1 |
| `2_john_163055471_level2_decision.json` | 163055471 | 1/2 | john is BB with 2 in; three players limped for 2, SB has 1 |
| `3_andrew_163055468_level1_decision.json` | 163055468 | 0.5/1 | Creates andrew's DecisionMaker with BB 1 |
| `4_andrew_163055476_level2_decision.json` | 163055476 | 1/2 | andrew is BB with 2 in; SB completed to 2 |

In PDecision up to 1.0.0.39 the DecisionMaker keeps the blinds it was created with (0.5/1), although the
frames carry 1/2. The level-2 bets of 2 then read as raises to 2bb:

- john: "vs open raise" looks for `holdemOldCharts\cold_calls\2x\5vs5.txt` (and `rangesTournament\CONFIGURE_LOCALLY\BB vs BB.txt`),
  which cannot exist, and returns an exception (`Action` 6) for this and every later decision of the hand.
- andrew: answers `CALL 2` in the big blind of an unraised pot; EvenBet only offers fold/check/raise, and the
  PokerController table worker stopped on the illegal action (fixed separately in PokerController).

Fixed in `DecisionMaker.UpdateBlinds` (DecisionMaking, commit accb5a8): each frame's blinds replace the stored ones.

Reproduce:

```
python replay.py --exe <path to PDecision.exe>
```
