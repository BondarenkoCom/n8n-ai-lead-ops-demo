#!/usr/bin/env python3

import json
import math
import os
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy.io import wavfile


WIDTH = 1280
HEIGHT = 720
FPS = 30
DURATION = 30
TOTAL_FRAMES = FPS * DURATION
SAMPLE_RATE = 44100

BG = (7, 12, 22)
PANEL = (13, 23, 38)
PANEL_2 = (18, 30, 48)
EDGE = (42, 68, 102)
TEAL = (26, 224, 190)
BLUE = (94, 184, 255)
GOLD = (255, 205, 102)
RED = (255, 110, 110)
WHITE = (238, 244, 255)
SLATE = (135, 153, 182)
MUTED = (88, 103, 128)
GREEN = (112, 225, 151)

PROJECT_ROOT = Path(__file__).resolve().parents[1]
OUTPUT_DIR = PROJECT_ROOT.parents[0] / "outputs"
OUTPUT_PATH = OUTPUT_DIR / "n8n_ai_lead_ops_upwork.mp4"
TMP_DIR = Path(tempfile.gettempdir()) / "n8n_lead_ops_video"
FRAME_DIR = TMP_DIR / "frames"
AUDIO_PATH = TMP_DIR / "audio.wav"
PREVIEW_DIR = TMP_DIR / "preview"

PAYLOAD_PATH = PROJECT_ROOT / "docs" / "sample-payloads" / "high-intent-b2b-website.json"
RESPONSE_PATH = PROJECT_ROOT / "test-data" / "demo-responses" / "high-intent-response.json"


def load_font(size: int, mono: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = []
    if mono:
        candidates.extend(
            [
                r"C:\Windows\Fonts\consola.ttf",
                r"C:\Windows\Fonts\lucon.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
            ]
        )
    else:
        candidates.extend(
            [
                r"C:\Windows\Fonts\segoeuib.ttf",
                r"C:\Windows\Fonts\segoeui.ttf",
                r"C:\Windows\Fonts\arialbd.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            ]
        )

    for candidate in candidates:
        if os.path.exists(candidate):
            try:
                return ImageFont.truetype(candidate, size)
            except Exception:
                continue
    return ImageFont.load_default()


FONT_H1 = load_font(42)
FONT_H2 = load_font(24)
FONT_BODY = load_font(22)
FONT_SMALL = load_font(18)
FONT_TINY = load_font(15)
FONT_MICRO = load_font(13)
FONT_METRIC = load_font(34)
FONT_MONO = load_font(18, mono=True)
FONT_MONO_SMALL = load_font(15, mono=True)


def ease_out_cubic(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return 1.0 - (1.0 - value) ** 3


def ease_in_out(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return 3 * value**2 - 2 * value**3


def blend(color: tuple[int, int, int], alpha: float, base: tuple[int, int, int] = BG) -> tuple[int, int, int]:
    alpha = max(0.0, min(1.0, alpha))
    return tuple(int(color[i] * alpha + base[i] * (1.0 - alpha)) for i in range(3))


def fade(frame: int, start: int, end: int) -> float:
    if frame <= start:
        return 0.0
    if frame >= end:
        return 1.0
    return ease_out_cubic((frame - start) / (end - start))


def fade_out(frame: int, start: int, end: int) -> float:
    return 1.0 - fade(frame, start, end)


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


PAYLOAD = load_json(PAYLOAD_PATH)
RESPONSE = load_json(RESPONSE_PATH)

NORMALIZED = {
    "full_name": PAYLOAD["name"],
    "company": PAYLOAD["company"],
    "email": PAYLOAD["email"],
    "service_interest": PAYLOAD["service"],
    "estimated_budget": 18000,
    "urgency": "high",
    "geography": PAYLOAD["location"],
    "intent": "purchase",
    "sentiment": "positive",
}


def draw_grid(image: Image.Image) -> None:
    draw = ImageDraw.Draw(image)
    grid_color = blend(WHITE, 0.04)
    for x in range(0, WIDTH, 64):
        draw.line([(x, 0), (x, HEIGHT)], fill=grid_color, width=1)
    for y in range(0, HEIGHT, 64):
        draw.line([(0, y), (WIDTH, y)], fill=grid_color, width=1)
    for y in range(0, HEIGHT, 4):
        draw.line([(0, y), (WIDTH, y)], fill=blend(WHITE, 0.015), width=1)


def draw_timestamp(draw: ImageDraw.ImageDraw, frame: int) -> None:
    seconds = frame / FPS
    label = f"{int(seconds // 60):02d}:{seconds % 60:04.1f}"
    draw.text((WIDTH - 86, HEIGHT - 30), label, font=FONT_TINY, fill=blend(SLATE, 0.9))


def glow_text(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int],
    text: str,
    font: ImageFont.ImageFont,
    color: tuple[int, int, int],
    glow_alpha: float = 0.16,
) -> None:
    x, y = xy
    halo = blend(color, glow_alpha)
    for dx, dy in [(-2, 0), (2, 0), (0, -2), (0, 2)]:
        draw.text((x + dx, y + dy), text, font=font, fill=halo)
    draw.text((x, y), text, font=font, fill=color)


def panel(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    fill: tuple[int, int, int] = PANEL,
    outline: tuple[int, int, int] = EDGE,
    width: int = 2,
) -> None:
    draw.rounded_rectangle(box, radius=22, fill=fill, outline=outline, width=width)


def pill(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    text: str,
    fill_color: tuple[int, int, int],
    text_color: tuple[int, int, int] = WHITE,
) -> None:
    draw.rounded_rectangle(box, radius=(box[3] - box[1]) // 2, fill=fill_color)
    font = fit_font_to_width(draw, text, box[2] - box[0] - 18, [18, 16, 15, 14, 13])
    bbox = draw.textbbox((0, 0), text, font=font)
    x = box[0] + ((box[2] - box[0]) - (bbox[2] - bbox[0])) // 2 - bbox[0]
    y = box[1] + ((box[3] - box[1]) - (bbox[3] - bbox[1])) // 2 - bbox[1]
    draw.text((x, y), text, font=font, fill=text_color)


def wrapped_text(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.ImageFont, width: int) -> list[str]:
    words = text.split()
    lines: list[str] = []
    current = ""
    for word in words:
        candidate = word if not current else f"{current} {word}"
        bbox = draw.textbbox((0, 0), candidate, font=font)
        if bbox[2] - bbox[0] <= width:
            current = candidate
        else:
            if current:
                lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


def fit_font_to_width(
    draw: ImageDraw.ImageDraw,
    text: str,
    max_width: int,
    sizes: list[int],
    mono: bool = False,
) -> ImageFont.ImageFont:
    for size in sizes:
        font = load_font(size, mono=mono)
        bbox = draw.textbbox((0, 0), text, font=font)
        if bbox[2] - bbox[0] <= max_width:
            return font
    return load_font(sizes[-1], mono=mono)


def truncate_to_width(
    draw: ImageDraw.ImageDraw,
    text: str,
    font: ImageFont.ImageFont,
    max_width: int,
) -> str:
    bbox = draw.textbbox((0, 0), text, font=font)
    if bbox[2] - bbox[0] <= max_width:
        return text

    trimmed = text
    while len(trimmed) > 1:
        trimmed = trimmed[:-1]
        candidate = f"{trimmed}..."
        bbox = draw.textbbox((0, 0), candidate, font=font)
        if bbox[2] - bbox[0] <= max_width:
            return candidate
    return "..."


def center_text_in_box(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    text: str,
    font: ImageFont.ImageFont,
    fill: tuple[int, int, int],
) -> None:
    bbox = draw.textbbox((0, 0), text, font=font)
    x = box[0] + ((box[2] - box[0]) - (bbox[2] - bbox[0])) // 2 - bbox[0]
    y = box[1] + ((box[3] - box[1]) - (bbox[3] - bbox[1])) // 2 - bbox[1]
    draw.text((x, y), text, font=font, fill=fill)


def draw_source_card(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    title: str,
    subtitle: str,
    accent: tuple[int, int, int],
    frame: int,
    appear: int,
) -> None:
    if frame < appear:
        return
    progress = fade(frame, appear, appear + 14)
    x1, y1, x2, y2 = box
    offset = int((1.0 - progress) * 26)
    alpha_fill = blend(PANEL_2, 0.95 * progress)
    alpha_edge = blend(accent, 0.7 * progress)
    draw.rounded_rectangle((x1 + offset, y1, x2 + offset, y2), radius=18, fill=alpha_fill, outline=alpha_edge, width=2)
    draw.ellipse((x1 + 18 + offset, y1 + 20, x1 + 38 + offset, y1 + 40), fill=accent)
    draw.text((x1 + 54 + offset, y1 + 14), title, font=FONT_BODY, fill=blend(WHITE, progress))
    subtitle_lines = wrapped_text(draw, subtitle, FONT_TINY, x2 - x1 - 84)
    for idx, line in enumerate(subtitle_lines[:2]):
        draw.text((x1 + 54 + offset, y1 + 46 + idx * 18), line, font=FONT_TINY, fill=blend(SLATE, progress))


def draw_scene_before(draw: ImageDraw.ImageDraw, frame: int) -> None:
    title_alpha = fade(frame, 0, 18)
    glow_text(draw, (62, 52), "Inbound leads arrive everywhere.", FONT_H1, blend(WHITE, title_alpha))
    draw.text((64, 104), "Follow-up does not.", font=FONT_H2, fill=blend(GOLD, title_alpha))

    draw_source_card(draw, (64, 172, 430, 256), "Website form", "New inquiry from Acme Services Group", TEAL, frame, 20)
    draw_source_card(draw, (64, 276, 430, 360), "Email inquiry", "Needs routing and reply draft", BLUE, frame, 34)
    draw_source_card(draw, (64, 380, 430, 464), "Chat payload", "Possible duplicate contact follow-up", GOLD, frame, 48)

    panel(draw, (498, 164, 1184, 560), fill=blend(PANEL, 0.95), outline=blend(EDGE, 0.8))
    draw.text((530, 192), "What breaks without automation", font=FONT_H2, fill=blend(WHITE, fade(frame, 42, 58)))

    issue_lines = [
        ("Duplicate checks happen late", RED, 58),
        ("Hot leads cool down in inboxes", RED, 72),
        ("Routing depends on manual judgment", RED, 86),
        ("No consistent audit trail", RED, 100),
    ]
    for index, (text, color, appear) in enumerate(issue_lines):
        alpha = fade(frame, appear, appear + 10)
        if alpha <= 0:
            continue
        y = 246 + index * 54
        draw.ellipse((530, y + 8, 548, y + 26), fill=blend(color, alpha))
        draw.line((535, y + 17, 543, y + 17), fill=blend(BG, alpha), width=2)
        draw.text((564, y), text, font=FONT_BODY, fill=blend(WHITE, alpha))

    if frame >= 110:
        timer_alpha = fade(frame, 110, 126)
        pill(draw, (532, 474, 784, 512), "response delay: 4h 12m", blend(RED, 0.3 * timer_alpha))
        draw.text((534, 526), "High-intent lead still waiting for action.", font=FONT_SMALL, fill=blend(SLATE, timer_alpha))

    pulse = 0.5 + 0.5 * math.sin(frame / 6.0)
    outline = blend(RED, 0.22 + 0.18 * pulse * fade(frame, 120, 145))
    if frame >= 120:
        draw.rounded_rectangle((40, 34, 1220, 674), radius=28, outline=outline, width=3)


def step_state(frame: int, appear: int) -> float:
    return fade(frame, appear, appear + 14)


def draw_pipeline_box(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    title: str,
    detail: str,
    accent: tuple[int, int, int],
    progress: float,
) -> None:
    if progress <= 0:
        return
    x1, y1, x2, y2 = box
    glow = int(8 * progress)
    draw.rounded_rectangle(box, radius=18, fill=blend(PANEL_2, 0.98), outline=blend(accent, 0.95), width=2)
    draw.rounded_rectangle((x1 - glow, y1 - glow, x2 + glow, y2 + glow), radius=22, outline=blend(accent, 0.08 * progress), width=2)
    draw.text((x1 + 18, y1 + 16), title, font=FONT_BODY, fill=blend(WHITE, progress))
    lines = wrapped_text(draw, detail, FONT_SMALL, x2 - x1 - 36)
    for idx, line in enumerate(lines[:2]):
        draw.text((x1 + 18, y1 + 50 + idx * 22), line, font=FONT_SMALL, fill=blend(SLATE, progress))


def draw_pipeline(draw: ImageDraw.ImageDraw, frame: int) -> None:
    title_alpha = fade(frame, 150, 168)
    glow_text(draw, (70, 48), "n8n lead pipeline, built for action.", FONT_H1, blend(WHITE, title_alpha))
    draw.text((72, 100), "Normalize. Qualify. Route. Sync. Alert. Approve. Log.", font=FONT_H2, fill=blend(TEAL, title_alpha))

    steps = [
        ((72, 180, 244, 276), "Inbound", "Website, email, or chat payload", TEAL, 170),
        ((286, 180, 458, 276), "Normalize", "Map all sources into one lead schema", BLUE, 196),
        ((500, 180, 672, 276), "Deduplicate", "Check email, phone, and idempotency key", GOLD, 222),
        ((714, 180, 886, 276), "AI Extract", "Intent, sentiment, urgency, budget", TEAL, 248),
        ((928, 180, 1100, 276), "Score + Rules", "Lead score, route, and risk flags", BLUE, 274),
        ((1142, 180, 1230, 276), "Act", "CRM\nSlack\nAudit", GREEN, 300),
    ]

    for box, title, detail, accent, appear in steps:
        progress = step_state(frame, appear)
        draw_pipeline_box(draw, box, title, detail, accent, progress)

    for idx in range(len(steps) - 1):
        progress = step_state(frame, steps[idx + 1][4])
        if progress <= 0:
            continue
        start_x = steps[idx][0][2]
        start_y = (steps[idx][0][1] + steps[idx][0][3]) // 2
        end_x = steps[idx + 1][0][0]
        end_y = (steps[idx + 1][0][1] + steps[idx + 1][0][3]) // 2
        line_end = start_x + int((end_x - start_x) * progress)
        draw.line((start_x, start_y, line_end, end_y), fill=blend(TEAL, 0.9 * progress), width=3)
        if progress > 0.92:
            draw.ellipse((line_end - 5, end_y - 5, line_end + 5, end_y + 5), fill=blend(TEAL, progress))

    panel(draw, (72, 352, 636, 620), fill=blend(PANEL, 0.95), outline=blend(EDGE, 0.9))
    draw.text((100, 382), "Live result snapshot", font=FONT_H2, fill=blend(WHITE, fade(frame, 316, 332)))
    result_rows = [
        ("lead_score", "88", GOLD, 330),
        ("qualification_status", "qualified", GREEN, 342),
        ("route", "hot_lead_sales", TEAL, 354),
        ("duplicate_submission", "false", BLUE, 366),
    ]
    for idx, (key, value, accent, appear) in enumerate(result_rows):
        alpha = fade(frame, appear, appear + 8)
        if alpha <= 0:
            continue
        y = 430 + idx * 42
        draw.text((100, y), key, font=FONT_MONO, fill=blend(SLATE, alpha))
        draw.text((374, y), value, font=FONT_MONO, fill=blend(accent, alpha))

    panel(draw, (682, 352, 1210, 620), fill=blend(PANEL, 0.95), outline=blend(EDGE, 0.9))
    draw.text((710, 382), "Actions fired", font=FONT_H2, fill=blend(WHITE, fade(frame, 338, 354)))
    actions = [
        ("CRM upserted", GREEN, 356),
        ("Slack notified", BLUE, 368),
        ("Approval pending", GOLD, 380),
        ("Audit log written", TEAL, 392),
    ]
    for idx, (label, accent, appear) in enumerate(actions):
        alpha = fade(frame, appear, appear + 8)
        if alpha <= 0:
            continue
        y = 430 + idx * 42
        draw.ellipse((712, y + 4, 728, y + 20), fill=blend(accent, alpha))
        draw.text((744, y), label, font=FONT_BODY, fill=blend(WHITE, alpha))

    if frame >= 420:
        total_alpha = fade(frame, 420, 440)
        pill(draw, (472, 650, 806, 690), "high-intent lead processed in seconds", blend(TEAL, 0.24 * total_alpha))


def draw_json_line(
    draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    key: str,
    value: str,
    alpha: float,
    max_value_width: int = 220,
) -> None:
    draw.text((x, y), f'"{key}"', font=FONT_MONO_SMALL, fill=blend(BLUE, alpha))
    draw.text((x + 140, y), ": ", font=FONT_MONO_SMALL, fill=blend(SLATE, alpha))
    value_font = fit_font_to_width(draw, value, max_value_width, [15, 14, 13], mono=True)
    safe_value = truncate_to_width(draw, value, value_font, max_value_width)
    draw.text((x + 164, y), safe_value, font=value_font, fill=blend(WHITE, alpha))


def draw_proof(draw: ImageDraw.ImageDraw, frame: int) -> None:
    title_alpha = fade(frame, 450, 466)
    glow_text(draw, (78, 52), "From raw inquiry to routed record.", FONT_H1, blend(WHITE, title_alpha))
    draw.text((80, 104), "This is the part clients actually buy.", font=FONT_H2, fill=blend(GOLD, title_alpha))

    panel(draw, (72, 160, 602, 628), fill=blend(PANEL, 0.96), outline=blend(EDGE, 0.85))
    draw.text((100, 188), "Payload -> normalized lead", font=FONT_H2, fill=blend(WHITE, fade(frame, 464, 480)))

    raw_alpha = fade(frame, 478, 494)
    if raw_alpha > 0:
        draw.text((100, 240), "Raw input", font=FONT_SMALL, fill=blend(SLATE, raw_alpha))
        raw_lines = [
            ('name', '"Avery Chen"'),
            ('company', '"Acme Services Group"'),
            ('service', '"Lead qualification..."'),
            ('budget', '"18000"'),
        ]
        for idx, (key, value) in enumerate(raw_lines):
            draw_json_line(draw, 100, 272 + idx * 28, key, value, raw_alpha, max_value_width=184)

    arrow_alpha = fade(frame, 520, 536)
    if arrow_alpha > 0:
        draw.line((324, 412, 324, 454), fill=blend(TEAL, arrow_alpha), width=3)
        draw.polygon([(324, 472), (314, 452), (334, 452)], fill=blend(TEAL, arrow_alpha))

    norm_alpha = fade(frame, 538, 556)
    if norm_alpha > 0:
        draw.text((100, 470), "Normalized", font=FONT_SMALL, fill=blend(SLATE, norm_alpha))
        norm_lines = [
            ('full_name', '"Avery Chen"'),
            ('intent', '"purchase"'),
            ('estimated_budget', '18000'),
            ('service_interest', '"Lead qualification..."'),
        ]
        for idx, (key, value) in enumerate(norm_lines):
            draw_json_line(draw, 100, 504 + idx * 28, key, value, norm_alpha, max_value_width=184)

    panel(draw, (644, 160, 1208, 628), fill=blend(PANEL, 0.96), outline=blend(EDGE, 0.85))
    draw.text((672, 188), "What the system decides", font=FONT_H2, fill=blend(WHITE, fade(frame, 500, 516)))

    metric_cards = [
        ((672, 240, 924, 334), "Lead score", "88", GOLD, 516),
        ((942, 240, 1180, 334), "Route", "hot_lead_sales", TEAL, 530),
        ((672, 354, 924, 448), "CRM", "upserted", GREEN, 544),
        ((942, 354, 1180, 448), "Approval", "pending", BLUE, 558),
    ]
    for box, label, value, accent, appear in metric_cards:
        alpha = fade(frame, appear, appear + 10)
        if alpha <= 0:
            continue
        panel(draw, box, fill=blend(PANEL_2, 0.98), outline=blend(accent, 0.9))
        draw.text((box[0] + 18, box[1] + 16), label, font=FONT_SMALL, fill=blend(SLATE, alpha))
        value_font = fit_font_to_width(draw, value, box[2] - box[0] - 32, [32, 28, 24, 22, 20, 18], mono=False)
        content_box = (box[0] + 16, box[1] + 42, box[2] - 16, box[3] - 10)
        center_text_in_box(draw, content_box, value, value_font, blend(accent, alpha))

    route_alpha = fade(frame, 584, 604)
    if route_alpha > 0:
        draw.text((672, 484), "Handled branches", font=FONT_SMALL, fill=blend(SLATE, route_alpha))
        branch_specs = [
            ((672, 516, 812, 552), "hot lead", TEAL),
            ((828, 516, 962, 552), "nurture", BLUE),
            ((978, 516, 1128, 552), "review", GOLD),
            ((1144, 516, 1200, 552), "spam", RED),
        ]
        for box, label, accent in branch_specs:
            pill(draw, box, label, blend(accent, 0.22 * route_alpha))

    footer_alpha = fade(frame, 626, 646)
    if footer_alpha > 0:
        draw.text((672, 572), "Qualified in seconds. Routed by rules.", font=FONT_SMALL, fill=blend(WHITE, footer_alpha))
        draw.text((672, 596), "Audit-ready on every run.", font=FONT_SMALL, fill=blend(SLATE, footer_alpha))


def draw_cta(draw: ImageDraw.ImageDraw, frame: int) -> None:
    top_alpha = fade(frame, 720, 744)
    glow_text(draw, (124, 144), "Turn inbound chaos into a qualified lead pipeline.", FONT_H1, blend(WHITE, top_alpha))
    draw.text((126, 206), "n8n workflows for AI lead qualification, routing, CRM sync, alerts, and approval flows.", font=FONT_H2, fill=blend(TEAL, top_alpha))

    alpha = fade(frame, 748, 770)
    if alpha > 0:
        features = [
            "Multi-source intake",
            "LLM extraction + scoring",
            "Duplicate handling",
            "CRM + Slack integration",
            "Human approval checkpoint",
            "Audit-ready delivery",
        ]
        for idx, label in enumerate(features):
            y = 292 + idx * 48
            draw.ellipse((128, y + 6, 144, y + 22), fill=blend(GREEN, alpha))
            draw.text((164, y), label, font=FONT_BODY, fill=blend(WHITE, alpha))

    card_alpha = fade(frame, 782, 804)
    if card_alpha > 0:
        panel(draw, (790, 274, 1160, 520), fill=blend(PANEL, 0.98), outline=blend(TEAL, 0.85))
        draw.text((822, 308), "Project fit", font=FONT_H2, fill=blend(WHITE, card_alpha))
        bullets = [
            "Agencies",
            "Consultancies",
            "Clinics",
            "Service operators",
            "B2B sales teams",
        ]
        for idx, bullet in enumerate(bullets):
            draw.text((824, 356 + idx * 30), f"- {bullet}", font=FONT_BODY, fill=blend(SLATE, card_alpha))

    footer_alpha = fade(frame, 820, 846)
    if footer_alpha > 0:
        draw.line((126, 602, 1156, 602), fill=blend(EDGE, footer_alpha), width=1)
        draw.text((126, 624), "github.com/BondarenkoCom/n8n-ai-lead-ops-demo", font=FONT_SMALL, fill=blend(WHITE, footer_alpha))
        draw.text((126, 654), "upwork.com/freelancers/artemb54", font=FONT_SMALL, fill=blend(WHITE, footer_alpha))

    cta_alpha = fade(frame, 844, 874)
    if cta_alpha > 0:
        cta = "Send your workflow. I'll map the automation path."
        bbox = draw.textbbox((0, 0), cta, font=FONT_H2)
        x = WIDTH - (bbox[2] - bbox[0]) - 118
        glow_text(draw, (x, 624), cta, FONT_H2, blend(GOLD, cta_alpha))


def render_frame(frame: int) -> Image.Image:
    image = Image.new("RGB", (WIDTH, HEIGHT), BG)
    draw_grid(image)
    draw = ImageDraw.Draw(image)

    if frame < 150:
        draw_scene_before(draw, frame)
    elif frame < 450:
        draw_pipeline(draw, frame)
    elif frame < 720:
        draw_proof(draw, frame)
    else:
        draw_cta(draw, frame)

    vignette = Image.new("L", (WIDTH, HEIGHT), 0)
    mask_draw = ImageDraw.Draw(vignette)
    mask_draw.rectangle((50, 50, WIDTH - 50, HEIGHT - 50), fill=180)
    vignette = vignette.filter(ImageFilter.GaussianBlur(60))
    shadow = Image.new("RGB", (WIDTH, HEIGHT), BG)
    image = Image.composite(image, shadow, vignette)

    final_draw = ImageDraw.Draw(image)
    draw_timestamp(final_draw, frame)
    return image


def sine_tone(freq: np.ndarray | float, t: np.ndarray, amp: float) -> np.ndarray:
    if isinstance(freq, np.ndarray):
        phase = 2 * np.pi * np.cumsum(freq) / SAMPLE_RATE
        return np.sin(phase) * amp
    return np.sin(2 * np.pi * freq * t) * amp


def add_tick(buffer: np.ndarray, time_sec: float, freq: float = 1240.0, length: float = 0.03, amp: float = 0.18) -> None:
    start = int(time_sec * SAMPLE_RATE)
    duration = int(length * SAMPLE_RATE)
    if start >= len(buffer):
        return
    end = min(len(buffer), start + duration)
    local_t = np.arange(end - start) / SAMPLE_RATE
    env = np.exp(-local_t * 28)
    buffer[start:end] += np.sin(2 * np.pi * freq * local_t) * env * amp


def generate_audio() -> None:
    n_samples = SAMPLE_RATE * DURATION
    t = np.arange(n_samples) / SAMPLE_RATE
    mix = np.zeros(n_samples, dtype=np.float64)

    drone = sine_tone(48.0, t, 0.08) + sine_tone(62.0, t, 0.04)
    drone *= 0.8 + 0.2 * np.sin(2 * np.pi * 0.18 * t)
    mix += drone

    sweep_mask = t < 3.6
    sweep_freq = 180 + (460 * np.clip(t / 3.6, 0, 1))
    sweep = sine_tone(sweep_freq, t, 0.13) * sweep_mask
    fade_env = np.clip(np.minimum(t / 0.6, (3.6 - t) / 0.7), 0, 1)
    mix += sweep * fade_env

    section_hits = [4.8, 14.5, 23.2]
    for hit in section_hits:
        add_tick(mix, hit, freq=420, length=0.18, amp=0.16)
        add_tick(mix, hit + 0.08, freq=680, length=0.12, amp=0.11)

    tick_frames = [20, 34, 48, 58, 72, 86, 100, 170, 196, 222, 248, 274, 300, 330, 342, 354, 366, 356, 368, 380, 392, 516, 530, 544, 558]
    for frame in tick_frames:
        add_tick(mix, frame / FPS, freq=1180.0, length=0.025, amp=0.12)

    confirm_times = [20.0, 20.16]
    for idx, confirm in enumerate(confirm_times):
        start = int(confirm * SAMPLE_RATE)
        duration = int(0.16 * SAMPLE_RATE)
        end = min(len(mix), start + duration)
        local_t = np.arange(end - start) / SAMPLE_RATE
        env = np.exp(-local_t * 10)
        tone = np.sin(2 * np.pi * (440 * (idx + 1)) * local_t) * env * 0.16
        mix[start:end] += tone

    final_mask = t >= 26.8
    final_env = np.clip(np.minimum((t - 26.8) / 0.5, (30.0 - t) / 2.0), 0, 1)
    mix += sine_tone(660.0, t, 0.14) * final_mask * final_env

    peak = np.max(np.abs(mix))
    if peak > 0:
        mix = mix / peak * 0.97
    wavfile.write(AUDIO_PATH, SAMPLE_RATE, (mix * 32767).astype(np.int16))


def save_preview() -> None:
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    preview_frames = [0, 90, 180, 270, 360, 480, 600, 780, 870]
    for frame in preview_frames:
        image = render_frame(frame)
        image.save(PREVIEW_DIR / f"preview_{frame:04d}.png")
        print(f"saved preview frame {frame}")


def render_all() -> None:
    FRAME_DIR.mkdir(parents=True, exist_ok=True)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for frame in range(TOTAL_FRAMES):
        image = render_frame(frame)
        image.save(FRAME_DIR / f"{frame:05d}.png")
        if (frame + 1) % 60 == 0:
            print(f"rendered {frame + 1}/{TOTAL_FRAMES}")

    generate_audio()

    command = [
        "ffmpeg",
        "-y",
        "-framerate",
        str(FPS),
        "-i",
        str(FRAME_DIR / "%05d.png"),
        "-i",
        str(AUDIO_PATH),
        "-c:v",
        "libx264",
        "-preset",
        "slow",
        "-crf",
        "18",
        "-pix_fmt",
        "yuv420p",
        "-c:a",
        "aac",
        "-b:a",
        "160k",
        "-shortest",
        str(OUTPUT_PATH),
    ]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        sys.stderr.write(result.stderr[-4000:])
        raise SystemExit(result.returncode)
    print(f"video: {OUTPUT_PATH}")


def main() -> None:
    if "--preview" in sys.argv:
        save_preview()
        return
    render_all()


if __name__ == "__main__":
    main()
