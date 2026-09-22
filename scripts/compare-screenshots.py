#!/usr/bin/env python3
"""Compare deterministic Avalonia screenshots produced by native desktop CI jobs.

Each artifact must contain screenshots/*.png and matching *.layout.json files.
Every platform pair must have the same names, dimensions, and exact layout JSON.
Raw errors are measured in premultiplied RGBA
so arbitrary RGB values in fully transparent pixels cannot create false failures.
MAE is the mean absolute channel error on the 0..255 scale; a changed pixel has
at least one channel error greater than --pixel-delta. Pixelmatch 0.4.0's standard
anti-alias detector excludes edge coverage differences from the image gate;
raw metrics and diffs remain available as evidence. No image is shifted/blurred.

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
from pixelmatch.contrib.PIL import pixelmatch


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
    "exp-tooltip-1x.png",
    "exp-tooltip-2x.png",
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


def antialias_masks(reference: Image.Image, candidate: Image.Image) -> tuple[Image.Image, Image.Image]:
    changed = Image.new("L", reference.size)
    antialiased = Image.new("L", reference.size)
    # Both backgrounds are required: compositing solely onto white would hide
    # a white pixel becoming transparent. Marker colors cannot collide with
    # pixelmatch's grayscale unchanged-pixel output.
    for background in (0, 255):
        canvas = Image.new("RGBA", reference.size, (background, background, background, 255))
        left = Image.alpha_composite(canvas, reference)
        right = Image.alpha_composite(canvas, candidate)
        markers = Image.new("RGBA", reference.size)
        pixelmatch(left, right, markers, threshold=0.1, includeAA=False)
        pixels = list(markers.getdata())
        difference_mask = Image.new("L", reference.size)
        difference_mask.putdata([255 if pixel[:3] == (255, 0, 0) else 0 for pixel in pixels])
        aa_mask = Image.new("L", reference.size)
        aa_mask.putdata([255 if pixel[:3] == (255, 255, 0) else 0 for pixel in pixels])
        changed = ImageChops.lighter(changed, difference_mask)
        antialiased = ImageChops.lighter(antialiased, aa_mask)
    # If either background identifies a real difference it must not be excluded.
    antialiased = ImageChops.subtract(antialiased, changed)
    return changed, antialiased


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
    non_aa_mask, aa_mask = antialias_masks(reference_image, candidate_image)
    non_aa_count = non_aa_mask.histogram()[255]
    aa_count = aa_mask.histogram()[255]
    retained = ImageChops.invert(aa_mask)
    residual_mae = sum(ImageStat.Stat(difference, retained).mean) / 4

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
    classification = Image.new("RGB", reference_image.size, (255, 255, 255))
    classification.paste((255, 255, 0), mask=aa_mask)
    classification.paste((255, 0, 0), mask=non_aa_mask)
    classification.save(diff_path.with_name(diff_path.stem + "--classification.png"))
    return {
        "size": list(reference_image.size),
        "mae": mae,
        "different_pixels": changed,
        "different_percent": changed_percent,
        "non_aa_different_pixels": non_aa_count,
        "non_aa_different_percent": 100 * non_aa_count / pixel_count,
        "aa_ignored_pixels": aa_count,
        "residual_mae": residual_mae,
        "max_channel_delta": maximum.getextrema()[1],
        "diff": diff_path.name,
    }


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts-root", type=Path, required=True,
                        help="Parent containing tests-win-x64, tests-osx-arm64 and tests-osx-x64")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--max-mae", type=float, default=1.0,
                        help="MAE after excluding detected AA pixels must be below this value")
    parser.add_argument("--max-non-aa-percent", type=float, default=1.0,
                        help="Maximum percent of pixels differing after standard AA detection")
    parser.add_argument("--pixel-delta", type=int, default=25)
    args = parser.parse_args()
    if not (0 < args.max_mae <= 255):
        parser.error("--max-mae must be greater than 0 and at most 255")
    if not (0 <= args.max_non_aa_percent <= 100):
        parser.error("--max-non-aa-percent must be between 0 and 100")
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
        for name, path in images[platform].items():
            if not path.with_suffix(".layout.json").is_file():
                errors.append(f"{platform}: missing layout: {Path(name).with_suffix('.layout.json')}")

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
                        left_layout = json.loads(images[reference][name].with_suffix(".layout.json").read_text())
                        right_layout = json.loads(images[candidate][name].with_suffix(".layout.json").read_text())
                        if not isinstance(left_layout, list) or not left_layout or not isinstance(right_layout, list) or not right_layout:
                            raise ValueError("layout snapshots must be nonempty JSON arrays")
                        row["layout_equal"] = left_layout == right_layout
                        row["passed"] = (row["layout_equal"] and row["residual_mae"] < args.max_mae and
                                         row["non_aa_different_percent"] <= args.max_non_aa_percent)
                except (OSError, ValueError) as error:
                    row.update(passed=False, error=f"could not compare image: {error}")
            comparisons.append(row)

    passed = not errors and bool(comparisons) and all(row["passed"] for row in comparisons)
    report = {
        "passed": passed,
        "thresholds": {
            "layout": "exact JSON equality",
            "residual_mae_exclusive": args.max_mae,
            "non_aa_percent_inclusive": args.max_non_aa_percent,
            "pixelmatch_threshold": 0.1,
            "pixelmatch_includeAA": False,
            "raw_channel_delta_exclusive": args.pixel_delta,
        },
        "errors": errors,
        "comparisons": comparisons,
    }
    (args.output / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = [
        "# Windows/macOS shared UI screenshot comparison", "",
        f"Result: **{'PASS' if passed else 'FAIL'}**", "",
        f"Gates: exact layout JSON and dimensions; pixelmatch 0.4.0 (threshold=0.1, includeAA=false) "
        f"differences ≤ {args.max_non_aa_percent:g}%; MAE excluding only detected AA pixels < {args.max_mae:g}/255.", "",
        f"Raw MAE and pixels with channel delta > {args.pixel_delta} are retained as evidence, not discarded.", "",
        "Each diff image shows reference, candidate, and 4× amplified error (red), left to right.", "",
        "Classification images mark ignored anti-alias edges yellow and counted differences red. Black and white backgrounds are both checked.", "",
        "The remaining tolerance covers OS glyph rasterization differences; this is not a claim of pixel-exact equality. Layout, text, font and recorded colors must match exactly.", "",
        "These deterministic headless renders do not validate native windows, input permissions, or monitor DPI changes.", "",
    ]
    if errors:
        lines.extend(["## Missing inputs", ""] + [f"- {error}" for error in errors] + [""])
    lines.extend([
        "| Reference → candidate | Screenshot | Raw MAE | Raw different | Non-AA different | Residual MAE | Layout | Result |",
        "| --- | --- | ---: | ---: | ---: | ---: | --- | --- |",
    ])
    for row in comparisons:
        pair = f"{row['reference']} → {row['candidate']}"
        if "error" in row:
            lines.append(f"| {pair} | {row['screenshot']} | — | — | — | — | — | FAIL: {row['error']} |")
        else:
            lines.append(f"| {pair} | {row['screenshot']} | {row['mae']:.4f} | "
                         f"{row['different_percent']:.4f}% | {row['non_aa_different_percent']:.4f}% | "
                         f"{row['residual_mae']:.4f} | {'same' if row['layout_equal'] else 'DIFFERENT'} | "
                         f"{'PASS' if row['passed'] else 'FAIL'} |")
    summary = "\n".join(lines) + "\n"
    (args.output / "summary.md").write_text(summary, encoding="utf-8")
    print(summary)
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
