import tkinter as tk
from tkinter import ttk, filedialog, messagebox, scrolledtext
from PIL import Image, ImageTk, ImageEnhance, ImageFilter
Image.MAX_IMAGE_PIXELS = None
import json
import os
import re
import threading
import subprocess
import tempfile
import sys


class MapOCRApp:
    def __init__(self, root):
        self.root = root
        self.root.title("地图地名 OCR 识别工具")
        self.root.geometry("1400x900")
        self.root.minsize(1200, 800)

        self.current_image = None
        self.current_image_path = None
        self.recognition_results = []
        self.photo_image = None
        self.scale_factor = 1.0
        self.image_offset_x = 0
        self.image_offset_y = 0
        self.drag_start_x = 0
        self.drag_start_y = 0
        self.is_dragging = False
        self.ocr_engine = None
        self.ocr = None
        self.rect_start_x = 0
        self.rect_start_y = 0
        self.is_rect_selecting = False
        self._cached_scale = None

        self._build_ui()
        self._init_ocr()

    def _init_ocr(self):
        try:
            from rapidocr_onnxruntime import RapidOCR
            self._create_ocr_engine()
            self.ocr_engine = 'rapidocr'
            self.status_var.set("RapidOCR 就绪，可以开始识别")
            self.recognize_btn.config(state=tk.NORMAL)
        except ImportError:
            self.status_var.set("正在安装 RapidOCR...")
            self.root.after(100, self._install_rapidocr)

    def _create_ocr_engine(self):
        from rapidocr_onnxruntime import RapidOCR
        model_text = self.model_var.get()
        lang_text = self.lang_var.get() if hasattr(self, 'lang_var') else '中文'

        REC_MAP = {
            '中文': {
                'v4s': 'ch_PP-OCRv4_rec_server.onnx',
                'v4m': 'ch_PP-OCRv4_rec.onnx',
                'v3s': 'ch_PP-OCRv3_rec_server.onnx',
                'v3m': 'ch_PP-OCRv3_rec.onnx',
            },
            '韩语罗马音': {
                'v4s': 'en_PP-OCRv4_rec_server.onnx',
                'v4m': 'en_PP-OCRv4_rec.onnx',
                'v3s': 'en_PP-OCRv3_rec_server.onnx',
                'v3m': 'en_PP-OCRv3_rec.onnx',
            },
            '英文': {
                'v4s': 'en_PP-OCRv4_rec_server.onnx',
                'v4m': 'en_PP-OCRv4_rec.onnx',
                'v3s': 'en_PP-OCRv3_rec_server.onnx',
                'v3m': 'en_PP-OCRv3_rec.onnx',
            },
            '韩文': {
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

        if 'v4' in model_text and 'Server' in model_text:
            key = 'v4s'
        elif 'v4' in model_text and 'Mobile' in model_text:
            key = 'v4m'
        elif 'v3' in model_text and 'Server' in model_text:
            key = 'v3s'
        else:
            key = 'v3m'

        rec_models = REC_MAP.get(lang_text, REC_MAP['中文'])
        params = {
            'det_model': DET_MAP[key],
            'rec_model': rec_models[key],
            'cls_model': 'ch_ppocr_mobile_v2.0_cls.onnx',
        }
        lang_label = {'中文': '中', '韩语罗马音': '韩罗马', '英文': '英', '韩文': '韩'}.get(lang_text, '中')
        self.status_var.set(f"模型: {model_text.split(' - ')[0]} | 语言: {lang_label}")
        self.ocr = RapidOCR(**params)

    def _install_rapidocr(self):
        try:
            subprocess.check_call([sys.executable, "-m", "pip", "install", "rapidocr-onnxruntime", "-q"])
            self._create_ocr_engine()
            self.ocr_engine = 'rapidocr'
            self.status_var.set("RapidOCR 安装完成，可以开始识别")
            self.recognize_btn.config(state=tk.NORMAL)
        except Exception as e:
            self.status_var.set(f"RapidOCR 安装失败: {e}")
            messagebox.showerror("错误", f"无法安装 RapidOCR: {e}\n请手动运行: pip install rapidocr-onnxruntime")

    def _on_model_change(self, event=None):
        if self.ocr_engine != 'rapidocr':
            return
        self.status_var.set("正在切换模型...")
        def do_switch():
            try:
                self._create_ocr_engine()
                self.root.after(0, lambda: self.status_var.set("模型切换完成，可以开始识别"))
            except Exception as e:
                self.root.after(0, lambda: messagebox.showerror("错误", f"切换模型失败: {e}"))
                self.root.after(0, lambda: self.status_var.set("模型切换失败"))
        threading.Thread(target=do_switch, daemon=True).start()

    def _build_ui(self):
        main_paned = ttk.PanedWindow(self.root, orient=tk.HORIZONTAL)
        main_paned.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)
        left_frame = ttk.Frame(main_paned)
        main_paned.add(left_frame, weight=3)
        self._build_image_panel(left_frame)
        right_frame = ttk.Frame(main_paned, width=400)
        main_paned.add(right_frame, weight=1)
        right_frame.pack_propagate(False)
        self._build_result_panel(right_frame)
        status_frame = ttk.Frame(self.root)
        status_frame.pack(fill=tk.X, side=tk.BOTTOM, padx=5, pady=2)
        self.status_var = tk.StringVar(value="初始化中...")
        ttk.Label(status_frame, textvariable=self.status_var).pack(side=tk.LEFT)
        self.progress_var = tk.DoubleVar(value=0)
        self.progress_bar = ttk.Progressbar(status_frame, variable=self.progress_var, maximum=100, length=200, mode='determinate')
        self.progress_bar.pack(side=tk.LEFT, padx=10)
        self.stats_var = tk.StringVar(value="识别数量: 0")
        ttk.Label(status_frame, textvariable=self.stats_var).pack(side=tk.RIGHT)

    def _build_image_panel(self, parent):
        toolbar = ttk.Frame(parent)
        toolbar.pack(fill=tk.X, pady=2)
        ttk.Button(toolbar, text="加载图片", command=self._load_image).pack(side=tk.LEFT, padx=2)
        self.recognize_btn = ttk.Button(toolbar, text="开始识别", command=self._start_recognition, state=tk.DISABLED)
        self.recognize_btn.pack(side=tk.LEFT, padx=2)
        ttk.Button(toolbar, text="重置视图", command=self._fit_to_window).pack(side=tk.LEFT, padx=2)

        ttk.Label(toolbar, text="  模型:").pack(side=tk.LEFT, padx=(10, 2))
        self.model_var = tk.StringVar(value='PP-OCRv4 Server - 目前精度最高')
        model_combo = ttk.Combobox(toolbar, textvariable=self.model_var, width=24, state='readonly')
        model_combo['values'] = [
            'PP-OCRv4 Server - 目前精度最高',
            'PP-OCRv4 Mobile - 精度高 速度快',
            'PP-OCRv3 Server - 高精度',
            'PP-OCRv3 Mobile - 速度最快',
        ]
        model_combo.current(0)
        model_combo.pack(side=tk.LEFT, padx=2)
        model_combo.bind('<<ComboboxSelected>>', self._on_model_change)

        ttk.Label(toolbar, text="  语言:").pack(side=tk.LEFT, padx=(10, 2))
        self.lang_var = tk.StringVar(value='中文')
        lang_combo = ttk.Combobox(toolbar, textvariable=self.lang_var, width=16, state='readonly')
        lang_combo['values'] = [
            '中文',
            '韩语罗马音',
            '韩文',
            '英文',
        ]
        lang_combo.current(0)
        lang_combo.pack(side=tk.LEFT, padx=2)
        lang_combo.bind('<<ComboboxSelected>>', self._on_model_change)

        ttk.Label(toolbar, text="  模式:").pack(side=tk.LEFT, padx=(10, 2))
        self.mode_var = tk.StringVar(value='分块增强 (地图最佳)')
        mode_combo = ttk.Combobox(toolbar, textvariable=self.mode_var, width=20, state='readonly')
        mode_combo['values'] = [
            '分块增强 (地图最佳)',
            '分块识别 (大字密度)',
            '整图识别 (最快)',
        ]
        mode_combo.current(0)
        mode_combo.pack(side=tk.LEFT, padx=2)

        self.image_info_var = tk.StringVar(value="未加载图片")
        ttk.Label(toolbar, textvariable=self.image_info_var).pack(side=tk.RIGHT, padx=5)

        canvas_frame = ttk.Frame(parent)
        canvas_frame.pack(fill=tk.BOTH, expand=True)
        self.canvas = tk.Canvas(canvas_frame, bg='gray', highlightthickness=0)
        self.canvas.pack(fill=tk.BOTH, expand=True)
        self.canvas.bind('<Button-1>', self._on_mouse_down)
        self.canvas.bind('<B1-Motion>', self._on_mouse_drag)
        self.canvas.bind('<ButtonRelease-1>', self._on_mouse_up)
        self.canvas.bind('<MouseWheel>', self._on_mouse_wheel)
        self.canvas.bind('<Button-3>', self._on_right_down)
        self.canvas.bind('<B3-Motion>', self._on_right_drag)
        self.canvas.bind('<ButtonRelease-3>', self._on_right_up)

    def _build_result_panel(self, parent):
        filter_frame = ttk.LabelFrame(parent, text="筛选条件", padding=5)
        filter_frame.pack(fill=tk.X, pady=5)

        ttk.Label(filter_frame, text="置信度阈值:").pack(anchor=tk.W)
        self.confidence_threshold = tk.DoubleVar(value=0.5)
        self.confidence_scale = ttk.Scale(filter_frame, from_=0, to=1, orient=tk.HORIZONTAL,
                                          variable=self.confidence_threshold, command=self._on_threshold_change)
        self.confidence_scale.pack(fill=tk.X)
        self.confidence_label = ttk.Label(filter_frame, text="0.50")
        self.confidence_label.pack(anchor=tk.E)

        ttk.Label(filter_frame, text="搜索:").pack(anchor=tk.W, pady=(5, 0))
        self.search_var = tk.StringVar()
        self.search_var.trace('w', lambda *args: self._filter_results())
        ttk.Entry(filter_frame, textvariable=self.search_var).pack(fill=tk.X)

        result_frame = ttk.LabelFrame(parent, text="识别结果", padding=5)
        result_frame.pack(fill=tk.BOTH, expand=True, pady=5)

        columns = ('name', 'x', 'y', 'confidence')
        self.tree = ttk.Treeview(result_frame, columns=columns, show='headings', height=15)
        self.tree.heading('name', text='地名')
        self.tree.heading('x', text='X')
        self.tree.heading('y', text='Y')
        self.tree.heading('confidence', text='置信度')
        self.tree.column('name', width=120)
        self.tree.column('x', width=60)
        self.tree.column('y', width=60)
        self.tree.column('confidence', width=70)

        scrollbar = ttk.Scrollbar(result_frame, orient=tk.VERTICAL, command=self.tree.yview)
        self.tree.configure(yscrollcommand=scrollbar.set)
        self.tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        self.tree.bind('<<TreeviewSelect>>', self._on_select_item)
        self.tree.bind('<Double-1>', self._on_double_click)

        detail_frame = ttk.LabelFrame(parent, text="详细信息", padding=5)
        detail_frame.pack(fill=tk.X, pady=5)
        self.detail_text = scrolledtext.ScrolledText(detail_frame, height=8, wrap=tk.WORD)
        self.detail_text.pack(fill=tk.BOTH, expand=True)

        btn_frame = ttk.Frame(parent)
        btn_frame.pack(fill=tk.X, pady=5)
        ttk.Button(btn_frame, text="导出 JSON", command=self._export_json).pack(fill=tk.X, pady=2)
        ttk.Button(btn_frame, text="导出 CSV", command=self._export_csv).pack(fill=tk.X, pady=2)
        ttk.Button(btn_frame, text="复制选中", command=self._copy_selected).pack(fill=tk.X, pady=2)

    def _load_image(self):
        file_path = filedialog.askopenfilename(
            title="选择地图图片",
            filetypes=[("图片文件", "*.png *.jpg *.jpeg *.bmp *.webp"), ("所有文件", "*.*")]
        )
        if not file_path:
            return
        try:
            self.current_image = Image.open(file_path)
            self.current_image_path = file_path
            self.image_info_var.set(f"{os.path.basename(file_path)} | {self.current_image.size[0]}x{self.current_image.size[1]}")
            self._fit_to_window()
            self.status_var.set(f"已加载: {os.path.basename(file_path)}")
        except Exception as e:
            messagebox.showerror("错误", f"无法加载图片: {e}")

    def _display_image(self, full_redraw=True):
        if self.current_image is None:
            return
        if full_redraw or self._cached_scale != self.scale_factor:
            new_width = int(self.current_image.width * self.scale_factor)
            new_height = int(self.current_image.height * self.scale_factor)
            resized = self.current_image.resize((new_width, new_height), Image.Resampling.LANCZOS)
            self.photo_image = ImageTk.PhotoImage(resized)
            self._cached_scale = self.scale_factor
            self.canvas.delete("all")
            self.canvas.create_image(self.image_offset_x, self.image_offset_y,
                                     image=self.photo_image, anchor=tk.NW, tags="image")
            self._draw_boxes()
        else:
            self.canvas.coords("image", self.image_offset_x, self.image_offset_y)
            self.canvas.delete("box")
            self.canvas.delete("label")
            self._draw_boxes()

    def _draw_boxes(self):
        if not self.recognition_results:
            return
        threshold = self.confidence_threshold.get()
        search = self.search_var.get().lower()
        for i, item in enumerate(self.recognition_results):
            if item['confidence'] < threshold:
                continue
            if search and search not in item['name'].lower():
                continue
            bbox = item['bbox']
            points = []
            for corner in ['top_left', 'top_right', 'bottom_right', 'bottom_left']:
                x = bbox[corner][0] * self.scale_factor + self.image_offset_x
                y = bbox[corner][1] * self.scale_factor + self.image_offset_y
                points.extend([x, y])
            self.canvas.create_polygon(points, outline='red', fill='', width=1, tags=("box", f"box_{i}"))
            label_x = points[0]
            label_y = points[1] - 5
            self.canvas.create_text(label_x, label_y, text=item['name'],
                                    fill='red', anchor=tk.SW, font=('微软雅黑', 8),
                                    tags=("label", f"label_{i}"))

    def _enhance_image(self, img):
        enhancer = ImageEnhance.Contrast(img)
        img = enhancer.enhance(1.5)
        enhancer = ImageEnhance.Sharpness(img)
        img = enhancer.enhance(2.0)
        enhancer = ImageEnhance.Brightness(img)
        img = enhancer.enhance(1.1)
        return img

    def _split_and_recognize(self, img, enhance=True, tile_size=1500, overlap=250, progress_cb=None):
        w, h = img.size
        all_results = []
        seen = set()
        tiles = []
        y = 0
        while y < h:
            x = 0
            while x < w:
                tile_w = min(tile_size, w - x)
                tile_h = min(tile_size, h - y)
                tiles.append((x, y, tile_w, tile_h))
                x += tile_size - overlap
            y += tile_size - overlap
        total = len(tiles)
        for idx, (ox, oy, tw, th) in enumerate(tiles):
            if progress_cb:
                progress_cb(idx, total)
            self.root.after(0, lambda i=idx, t=total: self.status_var.set(
                f"分块识别中... {i+1}/{t}"))
            tile = img.crop((ox, oy, ox + tw, oy + th))
            if enhance:
                tile = self._enhance_image(tile)
            tile = tile.resize((int(tw * 2), int(th * 2)), Image.Resampling.LANCZOS)
            tmp_dir = tempfile.gettempdir()
            tmp_path = os.path.join(tmp_dir, f"ocr_tile_{idx}.png")
            tile.save(tmp_path, format='PNG')
            try:
                result, _ = self.ocr(tmp_path)
                if result:
                    for detection in result:
                        bbox, text, confidence = detection
                        abs_bbox = [[ox + p[0]/2, oy + p[1]/2] for p in bbox]
                        cx = sum([p[0] for p in abs_bbox]) / 4
                        cy = sum([p[1] for p in abs_bbox]) / 4
                        key = (text, int(cx), int(cy))
                        if key in seen:
                            continue
                        seen.add(key)
                        all_results.append((abs_bbox, text, confidence))
            except Exception as e:
                print(f"块 {idx} 出错: {e}")
            try:
                os.remove(tmp_path)
            except:
                pass
        return all_results

    def _start_recognition(self, region_img=None, region_offset=None):
        if self.current_image is None:
            messagebox.showwarning("提示", "请先加载图片")
            return
        if self.ocr_engine != 'rapidocr':
            messagebox.showwarning("提示", "OCR 引擎尚未就绪，请稍候...")
            return
        mode_text = self.mode_var.get()

        def progress_cb(current, total):
            pct = (current + 1) / total * 100
            self.root.after(0, lambda p=pct: self.progress_var.set(p))

        def recognize():
            try:
                self.root.after(0, lambda: self.progress_var.set(0))
                target_img = region_img if region_img else self.current_image
                ox_off = region_offset[0] if region_offset else 0
                oy_off = region_offset[1] if region_offset else 0

                if '分块增强' in mode_text:
                    self.root.after(0, lambda: self.status_var.set(
                        "模式: 分块+增强+2x放大 (地图最佳)... 请耐心等待"))
                    raw_results = self._split_and_recognize(
                        target_img, enhance=True, tile_size=1500, overlap=250, progress_cb=progress_cb
                    )
                elif '分块识别' in mode_text:
                    self.root.after(0, lambda: self.status_var.set(
                        "模式: 分块识别 (大字体密度)... 请耐心等待"))
                    raw_results = self._split_and_recognize(
                        target_img, enhance=False, tile_size=2500, overlap=200, progress_cb=progress_cb
                    )
                else:
                    self.root.after(0, lambda: self.status_var.set(
                        "模式: 整图识别 (最快)... 请耐心等待"))
                    tmp_dir = tempfile.gettempdir()
                    tmp_path = os.path.join(tmp_dir, "ocr_whole.png")
                    target_img.save(tmp_path, format='PNG')
                    result, _ = self.ocr(tmp_path)
                    raw_results = result or []
                    self.root.after(0, lambda: self.progress_var.set(100))
                    try:
                        os.remove(tmp_path)
                    except:
                        pass

                places = []
                if raw_results:
                    for detection in raw_results:
                        bbox, text, confidence = detection
                        text_clean = text.strip()
                        lang_text = self.lang_var.get()
                        if lang_text == '中文':
                            text_clean = re.sub(r'[^\u4e00-\u9fa5]', '', text_clean)
                            if not text_clean or len(text_clean) < 2:
                                continue
                        elif lang_text == '韩语罗马音':
                            text_clean = re.sub(r'[^a-zA-Z\-]', '', text_clean)
                            if not text_clean or len(text_clean) < 2:
                                continue
                        elif lang_text == '韩文':
                            text_clean = re.sub(r'[^\uac00-\ud7a3\u4e00-\u9fa5]', '', text_clean)
                            if not text_clean or len(text_clean) < 2:
                                continue
                        else:
                            if not text_clean or len(text_clean) < 2:
                                continue
                        abs_bbox = [[p[0] + ox_off, p[1] + oy_off] for p in bbox]
                        center_x = sum([p[0] for p in abs_bbox]) / 4
                        center_y = sum([p[1] for p in abs_bbox]) / 4
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

                places.sort(key=lambda x: x['confidence'], reverse=True)
                is_region = region_img is not None
                self.root.after(0, lambda: self._update_results(places, merge=is_region))
                self.root.after(0, lambda: self.progress_var.set(0))
                if is_region:
                    self.root.after(0, lambda: self.status_var.set(
                        f"区域识别完成！新增 {len(places)} 个，已叠加到现有结果"))
                else:
                    self.root.after(0, lambda: self.status_var.set(
                        f"识别完成！共 {len(places)} 个结果（分块模式已自动去重）"))

            except Exception as e:
                import traceback
                traceback.print_exc()
                self.root.after(0, lambda: messagebox.showerror("错误", f"识别失败: {e}"))
                self.root.after(0, lambda: self.status_var.set("识别失败"))
                self.root.after(0, lambda: self.progress_var.set(0))

        threading.Thread(target=recognize, daemon=True).start()

    def _update_results(self, results, merge=False):
        if merge:
            existing_keys = set()
            for item in self.recognition_results:
                key = (item['name'], int(item['position']['x']), int(item['position']['y']))
                existing_keys.add(key)
            added = 0
            for item in results:
                key = (item['name'], int(item['position']['x']), int(item['position']['y']))
                if key not in existing_keys:
                    self.recognition_results.append(item)
                    existing_keys.add(key)
                    added += 1
            self.recognition_results.sort(key=lambda x: x['confidence'], reverse=True)
            self.status_var.set(f"叠加完成：新增 {added} 个，总计 {len(self.recognition_results)} 个")
        else:
            self.recognition_results = results
        self.stats_var.set(f"识别数量: {len(self.recognition_results)}")
        self._filter_results()
        self._display_image()

    def _filter_results(self):
        self.confidence_label.config(text=f"{self.confidence_threshold.get():.2f}")
        for item in self.tree.get_children():
            self.tree.delete(item)
        threshold = self.confidence_threshold.get()
        search = self.search_var.get().lower()
        filtered = []
        for i, item in enumerate(self.recognition_results):
            if item['confidence'] < threshold:
                continue
            if search and search not in item['name'].lower():
                continue
            filtered.append((i, item))
        for idx, item in filtered:
            self.tree.insert('', tk.END, iid=str(idx),
                             values=(item['name'],
                                     item['position']['x'],
                                     item['position']['y'],
                                     f"{item['confidence']:.2f}"))

    def _on_select_item(self, event):
        selection = self.tree.selection()
        if not selection:
            return
        idx = int(selection[0])
        item = self.recognition_results[idx]
        self.detail_text.delete(1.0, tk.END)
        self.detail_text.insert(tk.END, f"地名: {item['name']}\n")
        self.detail_text.insert(tk.END, f"位置: ({item['position']['x']}, {item['position']['y']})\n")
        self.detail_text.insert(tk.END, f"置信度: {item['confidence']:.4f}\n")
        self.detail_text.insert(tk.END, f"尺寸: {item['size']['width']} x {item['size']['height']}\n")
        self.detail_text.insert(tk.END, f"边界框:\n")
        for corner, coords in item['bbox'].items():
            self.detail_text.insert(tk.END, f"  {corner}: ({coords[0]}, {coords[1]})\n")
        self._highlight_item(idx)

    def _highlight_item(self, idx):
        self.canvas.delete("highlight")
        item = self.recognition_results[idx]
        bbox = item['bbox']
        points = []
        for corner in ['top_left', 'top_right', 'bottom_right', 'bottom_left']:
            x = bbox[corner][0] * self.scale_factor + self.image_offset_x
            y = bbox[corner][1] * self.scale_factor + self.image_offset_y
            points.extend([x, y])
        self.canvas.create_polygon(points, outline='yellow', fill='', width=3, tags="highlight")

    def _on_double_click(self, event):
        selection = self.tree.selection()
        if not selection:
            return
        idx = int(selection[0])
        item = self.recognition_results[idx]
        self.root.clipboard_clear()
        self.root.clipboard_append(item['name'])
        self.status_var.set(f"已复制: {item['name']}")

    def _export_json(self):
        if not self.recognition_results:
            messagebox.showwarning("提示", "没有识别结果可导出")
            return
        file_path = filedialog.asksaveasfilename(
            defaultextension=".json",
            filetypes=[("JSON 文件", "*.json"), ("所有文件", "*.*")]
        )
        if not file_path:
            return
        try:
            lines = []
            sorted_results = sorted(self.recognition_results, key=lambda x: len(x['name']))
            for item in sorted_results:
                lines.append('  ' + json.dumps(item, ensure_ascii=False, separators=(', ', ': ')))
            content = '[\n' + ',\n'.join(lines) + '\n]'
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(content)
            self.status_var.set(f"已导出: {file_path}")
        except Exception as e:
            messagebox.showerror("错误", f"导出失败: {e}")

    def _export_csv(self):
        if not self.recognition_results:
            messagebox.showwarning("提示", "没有识别结果可导出")
            return
        file_path = filedialog.asksaveasfilename(
            defaultextension=".csv",
            filetypes=[("CSV 文件", "*.csv"), ("所有文件", "*.*")]
        )
        if not file_path:
            return
        try:
            import csv
            with open(file_path, 'w', newline='', encoding='utf-8-sig') as f:
                writer = csv.writer(f)
                writer.writerow(['地名', 'X', 'Y', '宽度', '高度', '置信度'])
                for item in self.recognition_results:
                    writer.writerow([
                        item['name'],
                        item['position']['x'],
                        item['position']['y'],
                        item['size']['width'],
                        item['size']['height'],
                        item['confidence']
                    ])
            self.status_var.set(f"已导出: {file_path}")
        except Exception as e:
            messagebox.showerror("错误", f"导出失败: {e}")

    def _copy_selected(self):
        selection = self.tree.selection()
        if not selection:
            messagebox.showwarning("提示", "请先选择一项")
            return
        idx = int(selection[0])
        item = self.recognition_results[idx]
        text = f"{item['name']}\t{item['position']['x']}\t{item['position']['y']}\t{item['confidence']:.4f}"
        self.root.clipboard_clear()
        self.root.clipboard_append(text)
        self.status_var.set("已复制到剪贴板")

    def _on_threshold_change(self, value):
        self._filter_results()
        self._display_image()

    def _fit_to_window(self):
        if self.current_image is None:
            return
        canvas_width = self.canvas.winfo_width()
        canvas_height = self.canvas.winfo_height()
        if canvas_width <= 1 or canvas_height <= 1:
            self.root.after(100, self._fit_to_window)
            return
        img_width, img_height = self.current_image.size
        scale_x = canvas_width / img_width
        scale_y = canvas_height / img_height
        self.scale_factor = min(scale_x, scale_y) * 0.95
        self.image_offset_x = (canvas_width - img_width * self.scale_factor) / 2
        self.image_offset_y = (canvas_height - img_height * self.scale_factor) / 2
        self._display_image()

    def _on_mouse_down(self, event):
        self.drag_start_x = event.x
        self.drag_start_y = event.y
        self.is_dragging = True

    def _on_mouse_drag(self, event):
        if not self.is_dragging:
            return
        dx = event.x - self.drag_start_x
        dy = event.y - self.drag_start_y
        self.image_offset_x += dx
        self.image_offset_y += dy
        self.drag_start_x = event.x
        self.drag_start_y = event.y
        self.canvas.coords("image", self.image_offset_x, self.image_offset_y)
        self.canvas.move("box", dx, dy)
        self.canvas.move("label", dx, dy)

    def _on_mouse_up(self, event):
        if self.is_dragging:
            self.is_dragging = False

    def _on_right_down(self, event):
        self.rect_start_x = event.x
        self.rect_start_y = event.y
        self.is_rect_selecting = True

    def _on_right_drag(self, event):
        if not self.is_rect_selecting:
            return
        self.canvas.delete("sel_rect")
        self.canvas.create_rectangle(
            self.rect_start_x, self.rect_start_y, event.x, event.y,
            outline='cyan', width=2, dash=(4, 4), tags="sel_rect"
        )

    def _on_right_up(self, event):
        if not self.is_rect_selecting:
            return
        self.is_rect_selecting = False
        self.canvas.delete("sel_rect")

        if self.current_image is None:
            return

        x1 = min(self.rect_start_x, event.x)
        y1 = min(self.rect_start_y, event.y)
        x2 = max(self.rect_start_x, event.x)
        y2 = max(self.rect_start_y, event.y)

        if x2 - x1 < 10 or y2 - y1 < 10:
            return

        img_x1 = int((x1 - self.image_offset_x) / self.scale_factor)
        img_y1 = int((y1 - self.image_offset_y) / self.scale_factor)
        img_x2 = int((x2 - self.image_offset_x) / self.scale_factor)
        img_y2 = int((y2 - self.image_offset_y) / self.scale_factor)

        img_w, img_h = self.current_image.size
        img_x1 = max(0, min(img_x1, img_w - 1))
        img_y1 = max(0, min(img_y1, img_h - 1))
        img_x2 = max(0, min(img_x2, img_w - 1))
        img_y2 = max(0, min(img_y2, img_h - 1))

        if img_x2 - img_x1 < 10 or img_y2 - img_y1 < 10:
            return

        menu = tk.Menu(self.root, tearoff=0)
        menu.add_command(
            label=f"识别此区域 ({img_x2-img_x1}x{img_y2-img_y1})",
            command=lambda: self._recognize_region(img_x1, img_y1, img_x2, img_y2)
        )
        menu.add_command(
            label="取消",
            command=lambda: None
        )
        menu.tk_popup(event.x_root, event.y_root)

    def _recognize_region(self, x1, y1, x2, y2):
        if self.ocr_engine != 'rapidocr':
            messagebox.showwarning("提示", "OCR 引擎尚未就绪")
            return
        region_img = self.current_image.crop((x1, y1, x2, y2))
        self.status_var.set(f"区域识别: ({x1},{y1})-({x2},{y2}) 大小 {x2-x1}x{y2-y1}")
        self._start_recognition(region_img=region_img, region_offset=(x1, y1))

    def _on_mouse_wheel(self, event):
        if hasattr(event, 'delta') and event.delta > 0:
            factor = 1.1
        else:
            factor = 0.9
        mouse_x = event.x
        mouse_y = event.y
        self.image_offset_x = mouse_x - (mouse_x - self.image_offset_x) * factor
        self.image_offset_y = mouse_y - (mouse_y - self.image_offset_y) * factor
        self.scale_factor *= factor
        self._display_image()


def main():
    root = tk.Tk()
    app = MapOCRApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()