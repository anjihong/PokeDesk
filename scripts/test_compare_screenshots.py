"""Regression checks for the cross-platform screenshot gate (no network or desktop)."""

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from PIL import Image, ImageDraw


SCRIPT = Path(__file__).with_name("compare-screenshots.py")
SPEC = importlib.util.spec_from_file_location("comparison", SCRIPT)
COMPARISON = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPARISON)


class ComparisonTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.inputs = self.root / "inputs"
        self.image = Image.new("RGBA", (100, 100), (40, 40, 40, 255))
        self.draw_fixture(self.image)
        self.layout = [{"path": "0", "type": "Border", "bounds": [0, 0, 100, 100], "background": "#282828"},
                       {"path": "0/0", "type": "TextBlock", "bounds": [8, 8, 60, 12], "text": "Pokemon", "font": "fixture", "foreground": "#FFFFFF"}]
        for platform in COMPARISON.PLATFORMS:
            directory = self.inputs / f"tests-{platform}" / "screenshots"
            directory.mkdir(parents=True)
            for name in COMPARISON.REQUIRED_SCREENSHOTS:
                path = directory / name
                self.image.save(path)
                path.with_suffix(".layout.json").write_text(json.dumps(self.layout))
        self.candidate = self.inputs / "tests-osx-arm64/screenshots/starter-1x.png"

    @staticmethod
    def draw_fixture(image, text_x=8):
        draw = ImageDraw.Draw(image)
        draw.text((text_x, 8), "Pokemon", fill="white")
        draw.rectangle((30, 40, 69, 79), fill=(200, 160, 40, 255))

    def run_comparison(self, expected):
        output = self.root / "report"
        result = subprocess.run([sys.executable, str(SCRIPT), "--artifacts-root", str(self.inputs),
                                 "--output", str(output)], capture_output=True, text=True)
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        report = json.loads((output / "report.json").read_text())
        self.assertEqual(report["passed"], expected == 0)
        return report

    def test_identical_images_and_layout_pass(self):
        report = self.run_comparison(0)
        self.assertEqual(len(report["comparisons"]), 33)

    def test_one_pixel_text_move_cannot_hide_in_antialias_tolerance(self):
        shifted = Image.new("RGBA", self.image.size, (40, 40, 40, 255))
        self.draw_fixture(shifted, text_x=9)
        shifted.save(self.candidate)
        self.layout[1]["bounds"][0] += 1
        self.candidate.with_suffix(".layout.json").write_text(json.dumps(self.layout))
        report = self.run_comparison(1)
        self.assertTrue(any(row.get("layout_equal") is False for row in report["comparisons"]))

    def test_color_change_fails_even_with_unchanged_layout(self):
        changed = self.image.copy()
        ImageDraw.Draw(changed).rectangle((30, 40, 69, 79), fill=(0, 80, 255, 255))
        changed.save(self.candidate)
        report = self.run_comparison(1)
        self.assertTrue(any(row.get("non_aa_different_percent", 0) > 1 for row in report["comparisons"]))

    def test_image_position_change_fails_even_with_unchanged_layout(self):
        changed = self.image.copy()
        draw = ImageDraw.Draw(changed)
        draw.rectangle((30, 40, 69, 79), fill=(40, 40, 40, 255))
        draw.rectangle((35, 40, 74, 79), fill=(200, 160, 40, 255))
        changed.save(self.candidate)
        self.run_comparison(1)

    def test_transparency_change_is_detected_on_both_backgrounds(self):
        changed = self.image.copy()
        ImageDraw.Draw(changed).rectangle((30, 40, 69, 79), fill=(255, 255, 255, 0))
        changed.save(self.candidate)
        self.run_comparison(1)

    def test_missing_layout_fails(self):
        self.candidate.with_suffix(".layout.json").unlink()
        self.assertTrue(self.run_comparison(1)["errors"])

    def test_recorded_color_change_fails_even_when_image_matches(self):
        self.layout[1]["foreground"] = "#EEEEEE"
        self.candidate.with_suffix(".layout.json").write_text(json.dumps(self.layout))
        self.run_comparison(1)

    def test_missing_screenshot_fails(self):
        self.candidate.unlink()
        self.assertTrue(self.run_comparison(1)["errors"])

    def test_dimensions_change_fails(self):
        Image.new("RGBA", (101, 100)).save(self.candidate)
        report = self.run_comparison(1)
        self.assertTrue(any("dimensions differ" in row.get("error", "") for row in report["comparisons"]))


if __name__ == "__main__":
    unittest.main()
