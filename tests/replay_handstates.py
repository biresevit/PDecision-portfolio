"""Replay captured HandState JSON files against a running PDecision instance.

HandState files are discovered recursively.  Case directories are sorted first,
then their numbered files (0.json, 1.json, CONFIGURE_LOCALLY) are sent in order.

Examples:
    python tests/replay_handstates.py
    python tests/replay_handstates.py --directory C:/captures/HandStates
    python tests/replay_handstates.py --base http://127.0.0.1:5005 --timeout 90
"""

import argparse
import json
import socket
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


class ReplayTimeout(RuntimeError):
    """A request exceeded the configured timeout."""


def send(base: str, payload: object, timeout: float, api_key: str):
    request = urllib.request.Request(
        base.rstrip("/") + "/api/decision",
        data=json.dumps(payload, separators=(",", ":")).encode("utf-8"),
        method="POST",
        headers={
            "Content-Type": "application/json",
            **({"X-Api-Key": api_key} if api_key else {}),
        },
    )
    started = time.monotonic()
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            body = response.read().decode("utf-8", errors="replace")
            return response.status, body, time.monotonic() - started
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        return error.code, body, time.monotonic() - started
    except (socket.timeout, TimeoutError) as error:
        raise ReplayTimeout(f"request timed out after {timeout:g}s") from error
    except urllib.error.URLError as error:
        if isinstance(error.reason, (socket.timeout, TimeoutError)) or "timed out" in str(error.reason).lower():
            raise ReplayTimeout(f"request timed out after {timeout:g}s") from error
        raise RuntimeError(str(error.reason)) from error


def describe_decision(body: str):
    """Pulls the decision fields (Phase/Action/BetSize/Reason) out of a /api/decision response body.

    Returns a short human-readable string, or None if the body isn't a decision JSON object."""
    try:
        parsed = json.loads(body)
    except ValueError:
        return None
    if not isinstance(parsed, dict) or "Action" not in parsed:
        return None
    text = f"Phase={parsed.get('Phase')} Action={parsed.get('Action')} BetSize={parsed.get('BetSize')}"
    if parsed.get("Reason"):
        text += f" Reason={parsed['Reason']}"
    return text


def numbered_key(path: Path):
    try:
        return (0, int(path.stem), path.name.lower())
    except ValueError:
        return (1, 0, path.name.lower())


def discover(directory: Path):
    case_directories = sorted((path for path in directory.iterdir() if path.is_dir()), key=lambda path: path.name.lower())
    search_root = case_directories if case_directories else [directory]
    files = [path for root in search_root for path in root.rglob("*.json") if path.is_file()]
    return sorted(files, key=lambda path: (str(path.parent.relative_to(directory)).lower(), numbered_key(path)))


def main() -> int:
    parser = argparse.ArgumentParser(description="Replay captured HandState JSON files against PDecision")
    parser.add_argument(
        "--directory",
        type=Path,
        default=r"C:\Users\bires\projects\bot\PDecision\tests\illegal_raise\pv2-run109-100bot-b-3384b9-4115acf1-b865-4358-ba4b-67e1a795642e-29\Bot020_pv2-run109-100bot-b-3384b9-4115acf1-b865-4358-ba4b-67e1a795642e-29",
        help="root directory containing case subfolders and numbered HandState JSON files",
    )
    parser.add_argument("--base", default="http://127.0.0.1:5005", help="PDecision base URL")
    parser.add_argument("--timeout", type=float, default=90, help="per-request timeout in seconds")
    parser.add_argument("--api-key", default="", help="optional X-Api-Key value")
    parser.add_argument("--report", type=Path, help="optional JSONL report file")
    parser.add_argument("--stop-on-error", action="store_true", help="stop after the first HTTP/error response")
    parser.add_argument("--dry-run", action="store_true", help="list files without sending requests")
    args = parser.parse_args()

    directory = args.directory.expanduser().resolve()
    if not directory.is_dir():
        print(f"directory not found: {directory}", file=sys.stderr)
        return 2
    if args.timeout <= 0:
        print("--timeout must be positive", file=sys.stderr)
        return 2

    files = discover(directory)
    if not files:
        print(f"no JSON files found under {directory}", file=sys.stderr)
        return 2

    print(f"directory: {directory}")
    print(f"files: {len(files)}")
    if args.dry_run:
        for index, path in enumerate(files):
            print(f"{index:04d} {path.relative_to(directory)}")
        return 0

    report = args.report.open("w", encoding="utf-8") if args.report else None
    ok = 0
    failed = 0
    timed_out = 0
    skipped = 0
    try:
        for index, path in enumerate(files):
            relative = str(path.relative_to(directory))
            started = time.monotonic()
            record = {"index": index, "file": relative}
            try:
                with path.open("r", encoding="utf-8-sig") as input_file:
                    payload = json.load(input_file)
                record["hand_id"] = payload.get("HandID") if isinstance(payload, dict) else None
                if not (isinstance(payload, dict) and payload.get("IsMyTurn") is True):
                    skipped += 1
                    record["skipped"] = "IsMyTurn is not true"
                    print(f"[{index + 1}/{len(files)}] SKIP (IsMyTurn=false) {relative}")
                    if report:
                        report.write(json.dumps(record, separators=(",", ":")) + "\n")
                        report.flush()
                    continue
                status, body, elapsed = send(args.base, payload, args.timeout, args.api_key)
                decision = describe_decision(body)
                record.update({"status": status, "elapsed_ms": round(elapsed * 1000), "body": body})
                if decision:
                    record["decision"] = decision
                if 200 <= status < 300:
                    ok += 1
                    label = "OK"
                else:
                    failed += 1
                    label = "HTTP_ERROR"
                suffix = f" -> {decision}" if decision else ""
                print(f"[{index + 1}/{len(files)}] {label} {status} {elapsed:.3f}s {relative}{suffix}")
                if status >= 400 and args.stop_on_error:
                    break
            except ReplayTimeout as error:
                timed_out += 1
                record.update({"error": "timeout", "message": str(error), "elapsed_ms": round((time.monotonic() - started) * 1000)})
                print(f"[{index + 1}/{len(files)}] TIMEOUT {relative}: {error}")
                if args.stop_on_error:
                    break
            except Exception as error:  # keep replaying to expose all bad captures
                failed += 1
                record.update({"error": type(error).__name__, "message": str(error)})
                print(f"[{index + 1}/{len(files)}] ERROR {relative}: {error}")
                if args.stop_on_error:
                    break
            if report:
                report.write(json.dumps(record, separators=(",", ":")) + "\n")
                report.flush()
    finally:
        if report:
            report.close()

    print(f"completed: ok={ok}, http_or_parse_errors={failed}, timeouts={timed_out}, skipped={skipped}")
    return 0 if failed == 0 and timed_out == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
