import argparse
import json
import math
import os
import re
import sys
import tempfile

from PIL import Image, ImageEnhance, ImageFilter
Image.MAX_IMAGE_PIXELS = None


REC_MAP = {
    'ch': {
        'v4s': 'ch_PP-OCRv4_rec_server.onnx',
        'v4m': 'ch_PP-OCRv4_rec.onnx',
        'v3s': 'ch_PP-OCRv3_rec_server.onnx',
        'v3m': 'ch_PP-OCRv3_rec.onnx',
    },
    'en': {
        'v4s': 'en_PP-OCRv4_rec_server.onnx',
        'v4m': 'en_PP-OCRv4_rec.onnx',
        'v3s': 'en_PP-OCRv3_rec_server.onnx',
        'v3m': 'en_PP-OCRv3_rec.onnx',
    },
    'ko': {
        'v4s': 'korean_PP-OCRv4_rec_server.onnx',
        'v4m': 'korean_PP-OCRv4_rec.onnx',
        'v3s': 'korean_PP-OCRv3_rec_server.onnx',
        'v3m': 'korean_PP-OCRv3_rec.onnx',
    },
}

DET_MAP = {
    'v4s': 'ch_PP-OCRv4_det_server.onnx',
    'v4m': 'ch_PP-OCRv4_det.onnx',
    'v3s': 'ch_PP-OCRv3_det_server.onnx',
    'v3m': 'ch_PP-OCRv3_det.onnx',
}

LANG_MAP = {
    'ch': '中文',
    'en': '英文',
    'ko': '韩文',
    'ko_roman': '韩语罗马音',
}

MODEL_KEYS = {
    'v4s': ('v4', 'server'),
    'v4m': ('v4', 'mobile'),
    'v3s': ('v3', 'server'),
    'v3m': ('v3', 'mobile'),
}


def create_ocr(model='v4s', lang='ch'):
    from rapidocr_onnxruntime import RapidOCR

    rec_lang = 'en' if lang == 'ko_roman' else lang
    rec_models = REC_MAP.get(rec_lang, REC_MAP['ch'])
    params = {
        'det_model': DET_MAP[model],
        'rec_model': rec_models[model],
        'cls_model': 'ch_ppocr_mobile_v2.0_cls.onnx',
    }
    return RapidOCR(**params)


def enhance_image(img, contrast=1.5, sharpness=2.0, brightness=1.1):
    img = ImageEnhance.Contrast(img).enhance(contrast)
    img = ImageEnhance.Sharpness(img).enhance(sharpness)
    img = ImageEnhance.Brightness(img).enhance(brightness)
    return img


def clean_text(text, lang):
    text = text.strip()
    if lang == 'ch':
        text = re.sub(r'[^\u4e00-\u9fa5]', '', text)
        if not text or len(text) < 2:
            return None
    elif lang == 'ko_roman':
        text = re.sub(r'[^a-zA-Z\-]', '', text)
        if not text or len(text) < 2:
            return None
    elif lang == 'ko':
        text = re.sub(r'[^\uac00-\ud7a3\u4e00-\u9fa5]', '', text)
        if not text or len(text) < 2:
            return None
    else:
        if not text or len(text) < 2:
            return None
    return text


def split_and_recognize(ocr, img, enhance=True, tile_size=1500,
                        overlap=250, scale=2, lang='ch',
                        region=None, progress=True,
                        contrast=1.5, sharpness=2.0, brightness=1.1):
    if region:
        x1, y1, x2, y2 = region
        img = img.crop((x1, y1, x2, y2))
        offset = (x1, y1)
    else:
        offset = (0, 0)

    w, h = img.size
    all_results = []
    seen = set()
    tiles = []
    y = 0
    while y < h:
        x = 0
        while x < w:
            tw = min(tile_size, w - x)
            th = min(tile_size, h - y)
            tiles.append((x, y, tw, th))
            x += tile_size - overlap
        y += tile_size - overlap

    total = len(tiles)
    tmp_dir = tempfile.gettempdir()

    for idx, (ox, oy, tw, th) in enumerate(tiles):
        if progress:
            pct = (idx + 1) / total * 100
            bar = '█' * int(pct // 2) + '░' * (50 - int(pct // 2))
            print(f"\r  分块识别: [{bar}] {idx+1}/{total} ({pct:.1f}%)", end='', flush=True)

        tile = img.crop((ox, oy, ox + tw, oy + th))
        if enhance:
            tile = enhance_image(tile, contrast, sharpness, brightness)
        tile = tile.resize((int(tw * scale), int(th * scale)), Image.Resampling.LANCZOS)

        tmp_path = os.path.join(tmp_dir, f"ocr_tile_{idx}.png")
        tile.save(tmp_path, format='PNG')

        try:
            result, _ = ocr(tmp_path)
            if result:
                for detection in result:
                    bbox, text, confidence = detection
                    abs_bbox = [[ox + p[0] / scale, oy + p[1] / scale] for p in bbox]
                    cx = sum(p[0] for p in abs_bbox) / 4
                    cy = sum(p[1] for p in abs_bbox) / 4
                    key = (text, int(cx), int(cy))
                    if key in seen:
                        continue
                    seen.add(key)
                    all_results.append((abs_bbox, text, confidence))
        except Exception as e:
            print(f"\n  块 {idx} 出错: {e}")

        try:
            os.remove(tmp_path)
        except OSError:
            pass

    if progress and total > 0:
        print()

    return all_results, offset


def recognize_whole(ocr, img, lang='ch', region=None):
    if region:
        x1, y1, x2, y2 = region
        img = img.crop((x1, y1, x2, y2))
        offset = (x1, y1)
    else:
        offset = (0, 0)

    tmp_dir = tempfile.gettempdir()
    tmp_path = os.path.join(tmp_dir, "ocr_whole.png")
    img.save(tmp_path, format='PNG')

    try:
        result, _ = ocr(tmp_path)
        raw = result or []
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass

    return raw, offset


def build_places(raw_results, offset, lang, min_conf=0.0):
    ox_off, oy_off = offset
    places = []

    for detection in raw_results:
        bbox, text, confidence = detection
        text_clean = clean_text(text, lang)
        if text_clean is None:
            continue
        if confidence < min_conf:
            continue

        abs_bbox = [[p[0] + ox_off, p[1] + oy_off] for p in bbox]
        center_x = sum(p[0] for p in abs_bbox) / 4
        center_y = sum(p[1] for p in abs_bbox) / 4
        x_coords = [p[0] for p in abs_bbox]
        y_coords = [p[1] for p in abs_bbox]

        places.append({
            "name": text_clean,
            "position": {"x": round(center_x, 2), "y": round(center_y, 2)},
            "bbox": {
                "top_left": [round(abs_bbox[0][0], 2), round(abs_bbox[0][1], 2)],
                "top_right": [round(abs_bbox[1][0], 2), round(abs_bbox[1][1], 2)],
                "bottom_right": [round(abs_bbox[2][0], 2), round(abs_bbox[2][1], 2)],
                "bottom_left": [round(abs_bbox[3][0], 2), round(abs_bbox[3][1], 2)]
            },
            "size": {
                "width": round(max(x_coords) - min(x_coords), 2),
                "height": round(max(y_coords) - min(y_coords), 2)
            },
            "confidence": round(float(confidence), 4)
        })

    return places


def merge_results(existing, new_results):
    existing_keys = set()
    for item in existing:
        key = (item['name'], int(item['position']['x']), int(item['position']['y']))
        existing_keys.add(key)
    added = 0
    for item in new_results:
        key = (item['name'], int(item['position']['x']), int(item['position']['y']))
        if key not in existing_keys:
            existing.append(item)
            existing_keys.add(key)
            added += 1
    return added


def save_json(places, output_path, sort_by='name_len'):
    if sort_by == 'name_len':
        sorted_results = sorted(places, key=lambda x: len(x['name']))
    elif sort_by == 'confidence':
        sorted_results = sorted(places, key=lambda x: x['confidence'], reverse=True)
    elif sort_by == 'position':
        sorted_results = sorted(places, key=lambda x: (x['position']['y'], x['position']['x']))
    else:
        sorted_results = places

    lines = []
    for item in sorted_results:
        lines.append('  ' + json.dumps(item, ensure_ascii=False, separators=(', ', ': ')))
    content = '[\n' + ',\n'.join(lines) + '\n]'

    output_dir = os.path.dirname(output_path)
    if output_dir and not os.path.exists(output_dir):
        os.makedirs(output_dir)

    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(content)


def save_csv(places, output_path):
    import csv
    output_dir = os.path.dirname(output_path)
    if output_dir and not os.path.exists(output_dir):
        os.makedirs(output_dir)
    with open(output_path, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.writer(f)
        writer.writerow(['地名', 'X', 'Y', '宽度', '高度', '置信度'])
        for item in places:
            writer.writerow([
                item['name'],
                item['position']['x'],
                item['position']['y'],
                item['size']['width'],
                item['size']['height'],
                item['confidence']
            ])


def print_results(places, top_n=20):
    print(f"\n{'='*60}")
    print(f" 识别结果（前 {min(top_n, len(places))} 条）")
    print(f"{'='*60}")
    print(f"{'序号':<6}{'地名':<20}{'X':<10}{'Y':<10}{'置信度':<10}")
    print("-" * 60)

    for i, place in enumerate(places[:top_n], 1):
        name = place['name'][:18] if len(place['name']) > 18 else place['name']
        print(f"{i:<6}{name:<20}{place['position']['x']:<10.1f}{place['position']['y']:<10.1f}{place['confidence']:<10.4f}")

    if len(places) > top_n:
        print(f"\n... 还有 {len(places) - top_n} 条结果未显示")


def print_stats(places):
    print(f"\n{'='*60}")
    print(f" 统计信息")
    print(f"{'='*60}")
    print(f"  总识别数量: {len(places)}")
    if places:
        avg_conf = sum(p['confidence'] for p in places) / len(places)
        print(f"  平均置信度: {avg_conf:.4f}")
        high_conf = sum(1 for p in places if p['confidence'] > 0.9)
        print(f"  高置信度(>0.9): {high_conf} 个")
        mid_conf = sum(1 for p in places if 0.5 <= p['confidence'] <= 0.9)
        print(f"  中置信度(0.5~0.9): {mid_conf} 个")
        low_conf = sum(1 for p in places if p['confidence'] < 0.5)
        print(f"  低置信度(<0.5): {low_conf} 个")


def main():
    parser = argparse.ArgumentParser(
        description='地图地名 OCR 识别工具 (命令行版)',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
示例:
  python ocr_map.py map.png
  python ocr_map.py map.png -o result.json
  python ocr_map.py map.png -m v4s -l ch --mode tile_enhance
  python ocr_map.py map.png --region 100,200,800,600
  python ocr_map.py map.png --merge existing.json -o merged.json
  python ocr_map.py map.png --csv result.csv
  python ocr_map.py map.png --sort confidence --min-conf 0.6
        """
    )

    parser.add_argument('image', nargs='?', default=r'e:\CSharpProject\WC4MapEditor\map.png',
                        help='地图图片路径')
    parser.add_argument('-o', '--output', default=None,
                        help='输出JSON路径 (默认: <图片名>_places.json)')
    parser.add_argument('-m', '--model', default='v4s',
                        choices=['v4s', 'v4m', 'v3s', 'v3m'],
                        help='OCR模型: v4s=v4Server v4m=v4Mobile v3s=v3Server v3m=v3Mobile (默认: v4s)')
    parser.add_argument('-l', '--lang', default='ch',
                        choices=['ch', 'en', 'ko', 'ko_roman'],
                        help='语言: ch=中文 en=英文 ko=韩文 ko_roman=韩语罗马音 (默认: ch)')
    parser.add_argument('--mode', default='tile_enhance',
                        choices=['tile_enhance', 'tile', 'whole'],
                        help='识别模式: tile_enhance=分块增强 tile=分块识别 whole=整图识别 (默认: tile_enhance)')
    parser.add_argument('--tile-size', type=int, default=1500,
                        help='分块大小 (默认: 1500)')
    parser.add_argument('--overlap', type=int, default=250,
                        help='分块重叠像素 (默认: 250)')
    parser.add_argument('--scale', type=int, default=2,
                        help='放大倍数 (默认: 2)')
    parser.add_argument('--no-enhance', action='store_true',
                        help='禁用图像增强 (对比度/锐化/亮度)')
    parser.add_argument('--contrast', type=float, default=1.5,
                        help='对比度增强系数 (默认: 1.5)')
    parser.add_argument('--sharpness', type=float, default=2.0,
                        help='锐度增强系数 (默认: 2.0)')
    parser.add_argument('--brightness', type=float, default=1.1,
                        help='亮度增强系数 (默认: 1.1)')
    parser.add_argument('--min-conf', type=float, default=0.0,
                        help='最低置信度阈值 (默认: 0.0)')
    parser.add_argument('--region', type=str, default=None,
                        help='识别区域 x1,y1,x2,y2 (像素坐标)')
    parser.add_argument('--merge', type=str, default=None,
                        help='与已有JSON结果叠加合并')
    parser.add_argument('--sort', default='name_len',
                        choices=['name_len', 'confidence', 'position'],
                        help='排序方式: name_len=名称长度 confidence=置信度 position=位置 (默认: name_len)')
    parser.add_argument('--csv', type=str, default=None,
                        help='同时导出CSV文件')
    parser.add_argument('--top', type=int, default=20,
                        help='控制台显示前N条结果 (默认: 20)')
    parser.add_argument('--quiet', action='store_true',
                        help='静默模式，仅输出最终结果')

    args = parser.parse_args()

    if not os.path.exists(args.image):
        print(f"错误: 图片文件不存在: {args.image}")
        sys.exit(1)

    if args.output is None:
        base = os.path.splitext(os.path.basename(args.image))[0]
        args.output = os.path.join(os.path.dirname(args.image), f"{base}_places.json")

    region = None
    if args.region:
        parts = args.region.split(',')
        if len(parts) != 4:
            print("错误: --region 格式为 x1,y1,x2,y2")
            sys.exit(1)
        region = tuple(int(p) for p in parts)

    if not args.quiet:
        ver, arch = MODEL_KEYS[args.model]
        print(f"{'='*60}")
        print(f" 地图地名 OCR 识别工具 (命令行版)")
        print(f"{'='*60}")
        print(f"  图片: {args.image}")
        print(f"  模型: PP-OCRv{ver} {'Server' if arch=='server' else 'Mobile'}")
        print(f"  语言: {LANG_MAP[args.lang]}")
        print(f"  模式: {args.mode}")
        if args.mode != 'whole':
            print(f"  分块: {args.tile_size}px, 重叠: {args.overlap}px, 放大: {args.scale}x")
        if args.mode == 'tile_enhance' and not args.no_enhance:
            print(f"  增强: 对比度={args.contrast}, 锐度={args.sharpness}, 亮度={args.brightness}")
        if region:
            print(f"  区域: ({region[0]},{region[1]})-({region[2]},{region[3]})")
        if args.merge:
            print(f"  叠加: {args.merge}")
        print(f"  输出: {args.output}")
        print()

    if not args.quiet:
        print("正在初始化 RapidOCR...")
    ocr = create_ocr(args.model, args.lang)

    if not args.quiet:
        print(f"正在加载图片: {args.image}")
    img = Image.open(args.image)
    if not args.quiet:
        print(f"  图片尺寸: {img.size[0]} x {img.size[1]}")

    existing_results = []
    if args.merge:
        if not args.quiet:
            print(f"正在加载已有结果: {args.merge}")
        with open(args.merge, 'r', encoding='utf-8') as f:
            existing_results = json.load(f)
        if not args.quiet:
            print(f"  已有结果: {len(existing_results)} 条")

    if args.mode == 'whole':
        if not args.quiet:
            print("整图识别中...")
        raw_results, offset = recognize_whole(ocr, img, lang=args.lang, region=region)
    else:
        do_enhance = (args.mode == 'tile_enhance') and not args.no_enhance
        if not args.quiet:
            mode_label = "分块增强" if do_enhance else "分块识别"
            print(f"{mode_label}中... 请耐心等待")
        raw_results, offset = split_and_recognize(
            ocr, img,
            enhance=do_enhance,
            tile_size=args.tile_size,
            overlap=args.overlap,
            scale=args.scale,
            lang=args.lang,
            region=region,
            progress=not args.quiet,
            contrast=args.contrast,
            sharpness=args.sharpness,
            brightness=args.brightness
        )

    places = build_places(raw_results, offset, args.lang, min_conf=args.min_conf)

    if not args.quiet:
        print(f"识别完成！共 {len(places)} 个结果")

    if existing_results:
        added = merge_results(existing_results, places)
        places = existing_results
        if not args.quiet:
            print(f"叠加完成：新增 {added} 个，总计 {len(places)} 个")

    places.sort(key=lambda x: x['confidence'], reverse=True)

    save_json(places, args.output, sort_by=args.sort)
    if not args.quiet:
        print(f"JSON 已保存: {args.output}")

    if args.csv:
        save_csv(places, args.csv)
        if not args.quiet:
            print(f"CSV 已保存: {args.csv}")

    if not args.quiet:
        print_results(places, top_n=args.top)
        print_stats(places)
    else:
        print(f"{len(places)}")


if __name__ == "__main__":
    main()