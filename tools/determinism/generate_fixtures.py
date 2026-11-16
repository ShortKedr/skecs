"""Regenerate or check the saved version 1 integer operation journals."""

import argparse
import json
from pathlib import Path


SEEDS = (1, 27, 7331, 4294967295)
MASK = 0xFFFFFFFF


def xorshift32(seed):
    state = seed
    while True:
        state ^= (state << 13) & MASK
        state ^= state >> 17
        state ^= (state << 5) & MASK
        yield state


def journal(seed):
    words = xorshift32(seed)
    operations = []
    live = []
    moving = set()
    next_key = 1

    def append(kind, key=None, a=None, b=None):
        operation = {"kind": kind}
        for name, value in (("key", key), ("a", a), ("b", b)):
            if value is not None:
                operation[name] = value
        operations.append(operation)

    def signed(word):
        return word if word < 0x80000000 else word - 0x100000000

    for _ in range(1993):
        choice = next(words) % 18
        if not live or (len(live) < 24 and choice < 4):
            kind = ("create", "direct", "factory", "batch")[choice % 4]
            count = 2 if kind == "batch" else 1
            if kind == "create":
                append(kind, next_key)
            else:
                append(kind, next_key, signed(next(words)), signed(next(words)))
                moving.update(range(next_key, next_key + count))
            live.extend(range(next_key, next_key + count))
            next_key += count
            continue

        key = live[next(words) % len(live)]
        if choice < 6:
            append("destroy", key)
            live.remove(key)
            moving.discard(key)
        elif choice == 6:
            append("set-transform", key, signed(next(words)), signed(next(words)))
        elif choice == 7:
            append("set-motion" if key in moving else "add-motion", key, signed(next(words)), signed(next(words)))
            moving.add(key)
        elif choice == 8:
            append("remove-motion", key)
            moving.discard(key)
        elif choice == 9:
            append("set-marker", key, next(words) & 255, signed(next(words)))
        elif choice == 10:
            append("remove-marker", key)
        elif choice == 11:
            append("remove-transform", key)
        elif choice < 15:
            append("probe", 1 + next(words) % (next_key - 1))
        else:
            append("tick")

    # Exercise the same future allocations after the preceding storage history.
    append("direct", next_key, 2147483647, -2147483648)
    append("factory", next_key + 1, -1, 0)
    append("destroy", next_key + 1)
    append("destroy", next_key)
    append("batch", next_key + 2, -2147483648, 2147483647)
    append("probe", next_key + 1)
    append("tick")

    header = '{"formatVersion":1,"capacity":3,"operations":[\n'
    rows = ",\n".join(json.dumps(op, separators=(",", ":")) for op in operations)
    return (header + rows + "\n]}\n").encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--write", action="store_true")
    args = parser.parse_args()
    directory = Path(__file__).resolve().with_name("fixtures")

    known = xorshift32(1)
    if [next(known) for _ in range(3)] != [270369, 67634689, 2647435461]:
        raise RuntimeError("Unexpected xorshift32 version 1 test vector")

    for seed in SEEDS:
        path = directory / f"churn-{seed}.json"
        expected = journal(seed)
        if args.write:
            path.write_bytes(expected)
        elif not path.is_file() or path.read_bytes() != expected:
            parser.exit(1, f"Saved operation journal differs: {path}\n")


if __name__ == "__main__":
    main()
