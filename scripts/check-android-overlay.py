#!/usr/bin/env python3
"""Exercise the tracker overlay using a dedicated Android emulator."""
import argparse
import csv
import io
import re
import struct
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path

TRACKER = "org.phibkro.hdt.android.fixture"
DRAFT = "org.phibkro.hdt.testarena"
ORIGINAL = ("Fiery War Axe", "Leeroy Jenkins", "King Krush")
UPDATED = ("Fireball", "Leeroy Jenkins", "King Krush")
SINGLE_APP = ("Share one app", "A single app", "Record one app")
ENTIRE_SCREEN = ("Share entire screen", "Share full screen", "Entire screen")
OVERLAY_TITLE = b"HDT Arena overlay"
CARD_NAMES = {"fiery war axe", "fireball", "leeroy jenkins", "king krush"}


def words(text):
    return " ".join(re.findall(r"[a-z0-9]+", text.lower()))


def wait_for(message, read, timeout=45):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = read()
        if result:
            return result
        time.sleep(0.5)
    raise AssertionError(message)


class Device:
    def __init__(self, serial, evidence):
        self.serial = serial
        self.evidence = evidence

    def adb(self, *arguments, check=True):
        return subprocess.run(
            ["adb", "-s", self.serial, *map(str, arguments)],
            capture_output=True,
            check=check,
            timeout=100,
        )

    def launch(self, package):
        result = self.adb(
            "shell", "am", "start", "-W", "-a", "android.intent.action.MAIN",
            "-c", "android.intent.category.LAUNCHER", package,
        ).stdout.decode()
        if "Error:" in result:
            raise AssertionError(result)

    def tree(self):
        remote = "/data/local/tmp/hdt-overlay.xml"
        self.adb("shell", "rm", "-f", remote)
        if self.adb("shell", "uiautomator", "dump", remote, check=False).returncode:
            return []
        output = self.adb("exec-out", "cat", remote, check=False).stdout
        if not output.startswith(b"<?xml"):
            return []
        (self.evidence / "latest-window.xml").write_bytes(output)
        return list(ET.fromstring(output).iter("node"))

    def screen(self):
        png = self.adb("exec-out", "screencap", "-p").stdout
        (self.evidence / "latest-screen.png").write_bytes(png)
        return png

    def read_text(self, *, overlay=False):
        png = self.screen()
        width, height = struct.unpack_from(">II", png, 16)
        left = width // 2 if overlay else 0
        crop_height = height // 4 if overlay else height
        if overlay and width <= height:
            return []
        crop = subprocess.run(
            ["convert", "-", "-crop", f"{width - left}x{crop_height}+{left}+0",
             "+repage", "-resize", "200%", "png:-"],
            input=png, capture_output=True, check=True, timeout=10,
        ).stdout
        tsv = subprocess.run(
            ["tesseract", "stdin", "stdout", "--psm", "6" if overlay else "11", "tsv"],
            input=crop, capture_output=True, check=True, timeout=10,
        ).stdout.decode()
        groups = {}
        for row in csv.DictReader(io.StringIO(tsv), delimiter="\t", quoting=csv.QUOTE_NONE):
            if row["level"] != "5" or not row["text"].strip():
                continue
            key = tuple(row[field] for field in ("block_num", "par_num", "line_num"))
            groups.setdefault(key, []).append(row)
        lines = []
        for group in groups.values():
            text = " ".join(row["text"] for row in group)
            first = group[0]
            x = left + (int(first["left"]) + int(first["width"]) // 2) // 2
            y = (int(first["top"]) + int(first["height"]) // 2) // 2
            lines.append((text, (x, y)))
        return sorted(lines, key=lambda line: line[1][1])

    def tap(self, point):
        self.adb("shell", "input", "tap", *point)

    def find(self, labels, *, package=None, partial=False, fallback=False):
        expected = [words(label) for label in labels]
        for node in self.tree():
            if (package and node.get("package") != package) or node.get("enabled") == "false":
                continue
            for attr in ("text", "content-desc"):
                label = words(node.get(attr, ""))
                if label in expected or (partial and any(item in label for item in expected)):
                    box = re.findall(r"\d+", node.get("bounds", ""))
                    if len(box) == 4:
                        x, y, right, bottom = map(int, box)
                        if x < right and y < bottom:
                            return ((x + right) // 2, (y + bottom) // 2)
        if fallback:
            for text, point in self.read_text():
                label = words(text)
                if label in expected or (partial and any(item in label for item in expected)):
                    return point
        return None

    def click(self, labels, **options):
        point = wait_for(f"Could not find {labels}", lambda: self.find(labels, **options))
        self.tap(point)

    def consent(self):
        # The emulator is dedicated to this test. Always select app-only capture.
        def choose():
            next_button = self.find(("Next",), package="com.android.systemui")
            if next_button:
                return next_button
            entire = self.find(ENTIRE_SCREEN, package="com.android.systemui")
            if entire:
                self.tap(entire)
                self.click(SINGLE_APP, package="com.android.systemui")
                return None
            return None

        self.tap(wait_for("Could not confirm single-app screen sharing", choose))
        self.click(("Arena Test Screen",), partial=True)

    def overlay_window_exists(self):
        return OVERLAY_TITLE in self.adb("shell", "dumpsys", "window", "windows").stdout

    def expect_overlay(self, offers):
        expected = tuple(map(words, offers))

        def read():
            if not self.overlay_window_exists():
                return None
            lines = self.read_text(overlay=True)
            # The fixture paints no titles in this corner. These are the
            # native TextView's rendered lines, including their display order.
            names = [(words(text), point) for text, point in lines
                     if words(text) in CARD_NAMES]
            return names[0][1] if tuple(name for name, _ in names) == expected else None

        return wait_for(f"Overlay did not display {offers}", read, timeout=75)

    def expect_hidden(self):
        def hidden():
            return not self.overlay_window_exists() and not any(
                words(text) in CARD_NAMES for text, _ in self.read_text(overlay=True)
            )

        # A portrait or launcher frame is insufficient evidence. The test
        # also returns to the landscape fixture after the session is stopped.
        wait_for("Overlay still displays draft cards", hidden)

    def run(self, tracker_apk, draft_apk):
        self.adb("shell", "wm", "dismiss-keyguard")
        self.adb("install", "-r", tracker_apk)
        self.adb("install", "-r", draft_apk)
        self.adb("shell", "appops", "set", TRACKER, "SYSTEM_ALERT_WINDOW", "allow")
        self.launch(DRAFT)
        self.launch(TRACKER)
        tracker_ui = {"package": TRACKER, "fallback": True}
        self.click(("Start tracking",), **tracker_ui)
        wait_for("Screen sharing dialog did not open", lambda: self.find(
            ("Cancel",), package="com.android.systemui"))
        self.adb("shell", "input", "keyevent", "KEYCODE_BACK")
        wait_for("Cancel did not return the tracker to idle", lambda: self.find(
            ("Screen sharing was canceled.",), **tracker_ui))

        self.click(("Start tracking",), **tracker_ui)
        self.consent()
        print("Confirming rendered overlay above the draft", flush=True)
        point = self.expect_overlay(ORIGINAL)
        # Tap an actual overlay text pixel. The fixture beneath the overlay
        # should still receive the event and change its first title.
        self.tap(point)
        self.expect_overlay(UPDATED)
        self.adb("shell", "input", "keyevent", "KEYCODE_HOME")
        self.expect_hidden()
        self.launch(DRAFT)
        self.expect_overlay(UPDATED)
        self.launch(TRACKER)
        self.click(("Stop tracking",), **tracker_ui)
        wait_for("Stop did not return the tracker to idle", lambda: self.find(
            ("Tracking is off.",), **tracker_ui))
        self.launch(DRAFT)
        self.expect_hidden()

        self.launch(TRACKER)
        self.click(("Start tracking",), **tracker_ui)
        self.consent()
        self.expect_overlay(UPDATED)
        self.launch(TRACKER)
        self.click(("Stop tracking",), **tracker_ui)
        self.expect_hidden()
        print("Passed cancel, recognition, tap-through, background/resume, stop, and restart.", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", default="emulator-5554")
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("tracker", type=Path)
    parser.add_argument("draft", type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"emulator-\d+", args.serial):
        parser.error("Use a dedicated Android emulator serial.")
    args.evidence.mkdir(parents=True, exist_ok=True)
    device = Device(args.serial, args.evidence)
    try:
        device.run(args.tracker, args.draft)
    finally:
        (args.evidence / "logcat.txt").write_bytes(
            device.adb("logcat", "-d", "-t", "1500", check=False).stdout)


if __name__ == "__main__":
    main()
