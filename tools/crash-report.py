"""Answers one question: was Ossuary involved in a Slay the Spire 2 crash?

The game ships Sentry's native crash handler, which leaves a Windows minidump
per crash under

    %APPDATA%\\SlayTheSpire2\\sentry\\reports\\*.dmp

alongside a MessagePack event describing the machine. A minidump records the
exception, the loaded module list, and the faulting thread's stack, which is
enough to say where a crash happened without a debugger or symbols.

    python tools/crash-report.py

For each crash it prints the exception, the module containing the faulting
instruction, and every module with an address on the faulting thread's stack.
Ossuary is called out explicitly, because "is this mod at fault" is the reason
anyone runs this.

What it cannot tell you: a mod can leave the engine in a state that makes it
fault later, with no mod frame anywhere near the crash. Absence of Ossuary here
is evidence, not proof. The decisive test is still to rename the mods folder
and see whether the crash follows.
"""

import collections
import glob
import io
import os
import struct
import sys

EXCEPTION_STREAM = 6
MODULE_LIST_STREAM = 4
THREAD_LIST_STREAM = 3

MINIDUMP_SIGNATURE = 0x504D444D

# The handful worth naming. Everything else is reported by module name anyway.
OURS = {"ossuary.dll", "0harmony.dll"}

# Noise on every stack; listing them crowds out the frames that mean something.
UNINTERESTING = {"ntdll.dll", "kernelbase.dll", "kernel32.dll", "ucrtbase.dll"}

EXCEPTION_CODES = {
    0xC0000005: "ACCESS_VIOLATION",
    0xC000001D: "ILLEGAL_INSTRUCTION",
    0xC0000094: "INTEGER_DIVIDE_BY_ZERO",
    0xC00000FD: "STACK_OVERFLOW",
    0xC0000374: "HEAP_CORRUPTION",
    0xC0000409: "STACK_BUFFER_OVERRUN",
    0xE0434352: "MANAGED (.NET) EXCEPTION",
    0x80000003: "BREAKPOINT",
}


def _string(buf, rva):
    (length,) = struct.unpack_from("<I", buf, rva)
    return buf[rva + 4: rva + 4 + length].decode("utf-16-le", "replace")


def _streams(buf):
    signature, _version, count, directory = struct.unpack_from("<IIII", buf, 0)
    if signature != MINIDUMP_SIGNATURE:
        raise ValueError("not a minidump")

    found = {}
    for i in range(count):
        kind, size, rva = struct.unpack_from("<III", buf, directory + i * 12)
        found[kind] = (size, rva)
    return found


def _modules(buf, rva):
    (count,) = struct.unpack_from("<I", buf, rva)
    out = []
    for i in range(count):
        offset = rva + 4 + i * 108
        base, size, _checksum, _stamp, name_rva = struct.unpack_from("<QIIII", buf, offset)
        out.append((base, size, os.path.basename(_string(buf, name_rva))))
    return out


def _owner(modules, address):
    for base, size, name in modules:
        if base <= address < base + size:
            return name, address - base
    return None, 0


def _faulting_stack(buf, streams, thread_id):
    if THREAD_LIST_STREAM not in streams:
        return None

    rva = streams[THREAD_LIST_STREAM][1]
    (count,) = struct.unpack_from("<I", buf, rva)
    for i in range(count):
        offset = rva + 4 + i * 48
        if struct.unpack_from("<I", buf, offset)[0] != thread_id:
            continue
        _start, size, memory_rva = struct.unpack_from("<QII", buf, offset + 24)
        return buf[memory_rva: memory_rva + size]
    return None


def report(path):
    buf = io.open(path, "rb").read()
    streams = _streams(buf)
    modules = _modules(buf, streams[MODULE_LIST_STREAM][1])

    print(os.path.basename(path))
    print("  when        :", _when(path))

    if EXCEPTION_STREAM not in streams:
        print("  no exception record in this dump")
        return None

    rva = streams[EXCEPTION_STREAM][1]
    thread_id = struct.unpack_from("<I", buf, rva)[0]
    code, _flags, _record, address, parameters = struct.unpack_from("<IIQQI", buf, rva + 8)

    print("  exception   : 0x%08X  %s" % (code, EXCEPTION_CODES.get(code, "unknown")))

    if code == 0xC0000005 and parameters >= 2:
        info = struct.unpack_from("<15Q", buf, rva + 40)
        operation = {0: "read", 1: "write", 8: "execute"}.get(info[0], info[0])
        print("  faulted     : %s of 0x%X" % (operation, info[1]))
        if info[1] < 0x10000:
            print("                (a null-pointer dereference: a field read off an object that was not there)")

    name, offset = _owner(modules, address)
    print("  crashed in  : %s +0x%X" % (name or "<unmapped address>", offset))

    stack = _faulting_stack(buf, streams, thread_id)
    if stack is None:
        print("  the faulting thread's stack was not captured")
        return name

    seen = collections.Counter()
    for at in range(0, len(stack) - 8, 8):
        (value,) = struct.unpack_from("<Q", stack, at)
        if value < 0x10000:
            continue
        owner, _ = _owner(modules, value)
        if owner:
            seen[owner] += 1

    print("  stack holds addresses in:")
    for module, count in seen.most_common():
        if module.lower() in UNINTERESTING:
            continue
        print("      %-44s %4d" % (module, count))

    ours = sorted(m for m in seen if m.lower() in OURS)
    if ours:
        print("  >> OSSUARY IS ON THIS STACK: %s" % ", ".join(ours))
    else:
        print("  >> no Ossuary or Harmony address anywhere on this stack")

    return name


def _when(path):
    import datetime
    return datetime.datetime.fromtimestamp(os.path.getmtime(path)).strftime("%Y-%m-%d %H:%M:%S")


def main() -> int:
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.environ.get("APPDATA", ""), "SlayTheSpire2", "sentry", "reports")

    dumps = sorted(glob.glob(os.path.join(root, "*.dmp")), key=os.path.getmtime)
    if not dumps:
        print("No crash reports in %s" % root)
        print("Either the game has not crashed, or Steam has already uploaded and cleared them.")
        return 0

    print("%d crash report(s) in %s\n" % (len(dumps), root))

    blamed = []
    for path in dumps:
        try:
            blamed.append(report(path))
        except Exception as ex:
            print("  could not read: %s" % ex)
        print()

    culprits = collections.Counter(b for b in blamed if b)
    if culprits:
        print("Faulting module across all reports:")
        for module, count in culprits.most_common():
            print("  %-44s %d" % (module, count))

    return 0


if __name__ == "__main__":
    sys.exit(main())
