"""Generate synthetic OCR inputs; these files are not real school screenshots."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

DIRECTORY = Path(__file__).resolve().parent
FONT = Path("C:/Windows/Fonts/msyh.ttc")


def make_image(name, lines, height):
    image = Image.new("RGB", (1080, height), "white")
    draw = ImageDraw.Draw(image)
    font = ImageFont.truetype(str(FONT), 42)
    for index, line in enumerate(lines):
        draw.text((40, 40 + index * 88), line, font=font, fill="black")
    image.save(DIRECTORY / name)


if __name__ == "__main__":
    make_image("synthetic-notice.png", [
        "SYNTHETIC OCR TEST",
        "数学作业通知",
        "请于2026年10月12日18:00前提交习题。",
        "提交方式：上传至课程平台。",
        "仅供自动测试，不是真实学校通知。",
    ], 520)
    make_image("synthetic-long-notice.png", [
        "SYNTHETIC LONG OCR TEST",
        "课程作业补充说明",
        *[f"要求第{index}项：请独立完成并检查作业。" for index in range(1, 33)],
        "截止时间：2026年10月15日12:00。",
        "仅供自动测试，不是真实学校通知。",
    ], 3300)
    Image.new("RGB", (480, 320), "white").save(DIRECTORY / "synthetic-blank.png")
