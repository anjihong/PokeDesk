#!/usr/bin/env python3
"""Compare deterministic Avalonia screenshots produced by native desktop CI jobs.

Each artifact must contain screenshots/*.png. Every platform pair must have the
same screenshot names and dimensions. Errors are measured in premultiplied RGBA
so arbitrary RGB values in fully transparent pixels cannot create false failures.
MAE is the mean absolute channel error on the 0..255 scale; a changed pixel has
at least one channel error greater than --pixel-delta.

This checks shared rendering, not native transparency, DPI changes, or input
permissions. Those remain in docs/cross-platform-qa.md.
"""

from __future__ import annotations

import argparse
import itertools
import json
from pathlib import Path
import sys

from PIL import Image, ImageChops, ImageStat


PLATFORMS = ("win-x64", "osx-arm64", "osx-x64")
REQUIRED_SCREENSHOTS = {
    "starter-1x.png",
    "starter-2x.png",
    "main-collapsed-1x.png",
    "main-collapsed-2x.png",
    "main-dex-1x.png",
    "main-dex-2x.png",
    "main-owned-1x.png",
    "main-owned-2x.png",
    "egg-result-2x.png",
}


def premultiplied(image: Image.Image) -> Image.Image:
    red, green, blue, alpha = image.convert("RGBA").split()
    return Image.merge("RGBA", (
        ImageChops.multiply(red, alpha),
        ImageChops.multiply(green, alpha),
        ImageChops.multiply(blue, alpha),
        alpha,
    ))


def on_background(image: Image.Image) -> Image.Image:
    background = Image.new("RGBA", image.size, (48, 48, 48, 255))
    return Image.alpha_composite(background, image.convert("RGBA")).convert("RGB")


def compare_images(reference: Path, candidate: Path, diff_path: Path, pixel_delta: int) -> dict:
    with Image.open(reference) as reference_file, Image.open(candidate) as candidate_file:
        reference_image = reference_file.convert("RGBA")
        candidate_image = candidate_file.convert("RGBA")
    if reference_image.size != candidate_image.size:
        return {
            "passed": False,
            "error": f"dimensions differ: {reference_image.size} vs {candidate_image.size}",
        }

    difference = ImageChops.difference(premultiplied(reference_image), premultiplied(candidate_image))
    mae = sum(ImageStat.Stat(difference).mean) / 4
    channels = difference.split()
    maximum = channels[0]
    for channel in channels[1:]:
        maximum = ImageChops.lighter(maximum, channel)
    histogram = maximum.histogram()
    changed = sum(histogram[pixel_delta + 1:])
    pixel_count = reference_image.width * reference_image.height
    changed_percent = 100 * changed / pixel_count

    # Reference | candidate | 4x amplified red error map on the same canvas.
    # RGB output makes the error map visible even when alpha channels agree.
    heat = maximum.point(lambda value: min(255, value * 4))
    heatmap = Image.merge("RGB", (heat, Image.new("L", heat.size), Image.new("L", heat.size)))
    comparison = Image.new("RGB", (reference_image.width * 3, reference_image.height))
    comparison.paste(on_background(reference_image), (0, 0))
    comparison.paste(on_background(candidate_image), (reference_image.width, 0))
    comparison.paste(heatmap, (reference_image.width * 2, 0))
    diff_path.parent.mkdir(parents=True, exist_ok=True)
    comparison.save(diff_path)
    return {
        "size": list(reference_image.size),
        "mae": mae,
        "different_pixels": changed,
        "different_percent": changed_percent,
        "max_channel_delta": maximum.getextrema()[1],
        "diff": diff_path.name,
    }


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts-root", type=Path, required=True,
                        help="Parent containing tests-win-x64, tests-osx-arm64 and tests-osx-x64")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--max-mae", type=float, default=1.0,
                        help="MAE must be strictly below this value (default: 1.0 out of 255)")
    parser.add_argument("--max-different-percent", type=float, default=1.0,
                        help="At most this percent of pixels may exceed the channel delta")
    parser.add_argument("--pixel-delta", type=int, default=25)
    args = parser.parse_args()
    if not (0 < args.max_mae <= 255):
        parser.error("--max-mae must be greater than 0 and at most 255")
    if not (0 <= args.max_different_percent <= 100):
        parser.error("--max-different-percent must be between 0 and 100")
    if not (0 <= args.pixel_delta <= 255):
        parser.error("--pixel-delta must be between 0 and 255")
    return args


def main() -> int:
    args = arguments()
    args.output.mkdir(parents=True, exist_ok=True)
    errors: list[str] = []
    images: dict[str, dict[str, Path]] = {}
    for platform in PLATFORMS:
        directory = args.artifacts_root / f"tests-{platform}" / "screenshots"
        images[platform] = {path.name: path for path in directory.glob("*.png")}
        missing = REQUIRED_SCREENSHOTS - images[platform].keys()
        if missing:
            errors.append(f"{platform}: missing screenshots: {', '.join(sorted(missing))}")

    comparisons = []
    for reference, candidate in itertools.combinations(PLATFORMS, 2):
        names = images[reference].keys() | images[candidate].keys()
        for name in sorted(names):
            row = {"reference": reference, "candidate": candidate, "screenshot": name}
            if name not in images[reference] or name not in images[candidate]:
                row.update(passed=False, error="screenshot is missing from one platform")
            else:
                diff_path = args.output / "diffs" / f"{reference}--{candidate}--{name}"
                try:
                    row.update(compare_images(images[reference][name], images[candidate][name],
                                              diff_path, args.pixel_delta))
                    if "error" not in row:
                        row["passed"] = (row["mae"] < args.max_mae and
                                         row["different_percent"] <= args.max_different_percent)
                except (OSError, ValueError) as error:
                    row.update(passed=False, error=f"could not compare image: {error}")
            comparisons.append(row)

    passed = not errors and bool(comparisons) and all(row["passed"] for row in comparisons)
    report = {
        "passed": passed,
        "thresholds": {
            "mae_exclusive": args.max_mae,
            "different_percent_inclusive": args.max_different_percent,
            "channel_delta_exclusive": args.pixel_delta,
        },
        "errors": errors,
        "comparisons": comparisons,
    }
    (args.output / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = [
        "# Windows/macOS shared UI screenshot comparison", "",
        f"Result: **{'PASS' if passed else 'FAIL'}**", "",
        f"Thresholds: premultiplied RGBA MAE < {args.max_mae:g}/255; "
        f"pixels with any channel delta > {args.pixel_delta} ≤ {args.max_different_percent:g}%.", "",
        "Each diff image shows reference, candidate, and 4× amplified error (red), left to right.", "",
        "These deterministic headless renders do not validate native windows, input permissions, or monitor DPI changes.", "",
    ]
    if errors:
        lines.extend(["## Missing inputs", ""] + [f"- {error}" for error in errors] + [""])
    lines.extend([
        "| Reference → candidate | Screenshot | MAE | Different pixels | Result |",
        "| --- | --- | ---: | ---: | --- |",
    ])
    for row in comparisons:
        pair = f"{row['reference']} → {row['candidate']}"
        if "error" in row:
            lines.append(f"| {pair} | {row['screenshot']} | — | — | FAIL: {row['error']} |")
        else:
            lines.append(f"| {pair} | {row['screenshot']} | {row['mae']:.4f} | "
                         f"{row['different_percent']:.4f}% | {'PASS' if row['passed'] else 'FAIL'} |")
    summary = "\n".join(lines) + "\n"
    (args.output / "summary.md").write_text(summary, encoding="utf-8")
    print(summary)
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
