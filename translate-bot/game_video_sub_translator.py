import sys
import os
import time
import asyncio
import re
import json
import urllib.request
import urllib.parse
import difflib
from threading import Thread, Lock
import mss
import winocr
import keyboard
from PIL import Image, ImageEnhance
from PyQt6 import QtCore, QtGui, QtWidgets
import google.generativeai as genai
from dotenv import load_dotenv

import warnings
warnings.filterwarnings("ignore")

# ==========================================================
# FIX LỖI TỌA ĐỘ KHI WINDOWS BẬT SCALE (125%, 150%)
# ==========================================================
import ctypes
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    try:
        ctypes.windll.user32.SetProcessDPIAware()
    except Exception:
        pass

# ==========================================================
# CẤU HÌNH BẢO MẬT & API KEY
# ==========================================================
load_dotenv(encoding="utf-8-sig")
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "").strip(' \t\n\r"\'')

if GEMINI_API_KEY and len(GEMINI_API_KEY) > 10:
    genai.configure(api_key=GEMINI_API_KEY)
    
    sys_instruct = (
        "Bạn là một dịch giả game RPG giả tưởng chuyên nghiệp. "
        "Đầu vào là văn bản OCR từ game (thỉnh thoảng có lẫn chữ rác ở viền màn hình như 'LOG', 'AUTO', hoặc số đếm). "
        "NHIỆM VỤ TỐI THƯỢNG:\n"
        "1. CHỦ ĐỘNG BỎ QUA các ký tự rác vô nghĩa.\n"
        "2. Tự động sửa lỗi chính tả tiếng Anh do OCR (ví dụ: 'witn' -> 'with', 'propneaes' -> 'prophecies').\n"
        "3. DỊCH THOÁT Ý THEO NGỮ CẢNH: Không dịch word-by-word. (Ví dụ: 'make sense' / 'in that sense' dịch là 'có lý' / 'theo nghĩa đó').\n"
        "4. Văn phong tự nhiên, mượt mà đậm chất tiểu thuyết kỳ ảo.\n"
        "5. CHỈ TRẢ VỀ BẢN DỊCH TIẾNG VIỆT. Tuyệt đối không giải thích, không bình luận."
    )
    
    gemini_model = genai.GenerativeModel(
        model_name='gemini-1.5-flash',
        system_instruction=sys_instruct
    )
    print("[AI] Da ket noi thanh cong Google Gemini Flash!")
else:
    gemini_model = None
    print("[AI] Chua nhan duoc API Key, dung Google Translate du phong.")

def fast_translate_fallback(text: str) -> str:
    if not text.strip():
        return ""
    try:
        url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=en&tl=vi&q=" + urllib.parse.quote(text)
        req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            if isinstance(data, list):
                return " ".join(data)
            elif isinstance(data, str):
                return data
    except Exception:
        try:
            url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=vi&dt=t&q=" + urllib.parse.quote(text)
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            with urllib.request.urlopen(req, timeout=3.0) as resp:
                data = json.loads(resp.read().decode('utf-8'))
                parts = [part[0] for part in data[0] if part and part[0]]
                return "".join(parts)
        except Exception:
            pass
    return text

def ai_smart_translate(text: str) -> str:
    if not text.strip():
        return ""
    if not gemini_model:
        return "🤖 " + fast_translate_fallback(text)
        
    safety_settings = [
        {"category": "HARM_CATEGORY_HARASSMENT", "threshold": "BLOCK_NONE"},
        {"category": "HARM_CATEGORY_HATE_SPEECH", "threshold": "BLOCK_NONE"},
        {"category": "HARM_CATEGORY_SEXUALLY_EXPLICIT", "threshold": "BLOCK_NONE"},
        {"category": "HARM_CATEGORY_DANGEROUS_CONTENT", "threshold": "BLOCK_NONE"}
    ]
    
    try:
        response = gemini_model.generate_content(
            text, 
            generation_config={"temperature": 0.15}, 
            safety_settings=safety_settings
        )
        if response.text:
            result = response.text.strip().strip('"').strip("'")
            return "✨ " + result
        return "🤖 " + fast_translate_fallback(text)
    except Exception:
        return "🤖 " + fast_translate_fallback(text)


class SubtitleWorker(QtCore.QThread):
    new_subtitle_ready = QtCore.pyqtSignal(str)

    def __init__(self):
        super().__init__()
        self.running = True
        self.paused = False
        self.region = None
        
        self.accumulated_text = ""
        self.last_translated = ""
        
        self.accumulated_text_raw = ""
        self.last_change_time = 0
        self.last_overlap_time = 0  # Chống dính rác: thời điểm gần nhất tìm thấy từ trùng lặp
        
        self.cache = {}
        self.lock = Lock()
        self.req_id = 0

    def update_region(self, r):
        with self.lock:
            self.region = r
            self.accumulated_text = ""
            self.last_translated = ""
            self.accumulated_text_raw = ""
            self.last_overlap_time = 0

    def set_paused(self, state: bool):
        self.paused = state

    def clean_text(self, text: str) -> str:
        # Lọc rác cực mạnh
        text = re.sub(r'\d+:\d+\s*/\s*\d+:\d+', '', text)
        text = re.sub(r'\d+\s*/\s*\d+', '', text)
        text = re.sub(r'\b(LOG|AUTO|SKIP|Mage)\b', '', text, flags=re.IGNORECASE)
        text = re.sub(r'[^a-zA-Z0-9\s.,!?\'"\-]', '', text) 
        text = text.replace('\n', ' ').replace('\r', ' ')
        text = re.sub(r'\s+', ' ', text).strip()
        if text and text[0].islower():
            text = text[0].upper() + text[1:]
        return text

    def are_words_similar(self, w1: str, w2: str) -> bool:
        w1_clean = re.sub(r'[^\w]', '', w1.lower())
        w2_clean = re.sub(r'[^\w]', '', w2.lower())
        if not w1_clean or not w2_clean:
            return w1_clean == w2_clean
        if w1_clean == w2_clean:
            return True
        return difflib.SequenceMatcher(None, w1_clean, w2_clean).ratio() >= 0.85

    def merge_rolling_dialogue(self, accumulated: str, current_screen: str):
        """
        Trả về (văn_bản_đã_ghép, is_new_sentence, has_overlap).
        is_new_sentence = True nghĩa là current_screen là một câu thoại HOÀN TOÀN
        MỚI, không liên quan gì tới 'accumulated' -> nơi gọi hàm này cần kiểm tra
        xem câu cũ đã được gửi đi dịch chưa, tránh bị mất câu (xem chỗ gọi trong run()).
        has_overlap = True nghĩa là tìm thấy từ trùng lặp (câu đang tích lũy bình
        thường, chưa bị "rác" chen vào) -> dùng để reset đồng hồ chống-dính-rác.
        """
        if not accumulated:
            return current_screen, False, True
        if not current_screen:
            return accumulated, False, True

        acc_words = accumulated.split()
        scr_words = current_screen.split()

        best_acc_idx = -1
        best_scr_idx = -1
        max_overlap = 0
        search_start = max(0, len(acc_words) - 16)
        
        for scr_start in range(min(5, len(scr_words))):
            for acc_i in range(search_start, len(acc_words)):
                match_len = 0
                while (acc_i + match_len < len(acc_words) and 
                       scr_start + match_len < len(scr_words) and 
                       self.are_words_similar(acc_words[acc_i + match_len], scr_words[scr_start + match_len])):
                    match_len += 1
                
                if match_len > max_overlap:
                    max_overlap = match_len
                    best_acc_idx = acc_i
                    best_scr_idx = scr_start

        # Nếu có từ trùng lặp
        if max_overlap >= 1:
            merged_list = acc_words[:best_acc_idx] + scr_words[best_scr_idx:]
            return " ".join(merged_list), False, True
        else:
            # SỬA LỖI MẤT DÒNG ĐẦU:
            # Nếu KHÔNG có từ trùng lặp nào, phải kiểm tra xem câu cũ đã chấm dứt chưa.
            # Tránh việc OCR bị mất chữ đầu mà xóa luôn lịch sử câu thoại.
            last_char = accumulated.rstrip()[-1] if accumulated.strip() else ""
            if last_char not in ['.', '!', '?', '"', '”', '…', '>']:
                # Câu lửng lơ chưa hết -> Ép nối tiếp câu mới vào
                # (Lưu ý: nếu đây là chữ RÁC không có dấu câu, has_overlap=False sẽ
                # được nơi gọi (run()) dùng để phát hiện "dính rác quá lâu" và tự
                # xóa đi, xem cơ chế last_overlap_time trong run())
                return accumulated + " " + current_screen, False, False
            else:
                # Nếu đã có dấu ngắt câu đàng hoàng -> Sang câu hoàn toàn mới
                return current_screen, True, False

    def translate_task(self, text, current_id):
        if text in self.cache:
            vi = self.cache[text]
        else:
            vi = ai_smart_translate(text)
            self.cache[text] = vi

        if current_id == self.req_id and self.running:
            self.new_subtitle_ready.emit(vi)

    def run(self):
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)

        with mss.mss() as sct:
            while self.running:
                if self.paused or not self.region:
                    time.sleep(0.04)
                    continue

                try:
                    target_box = {
                        'top': int(self.region['top']),
                        'left': int(self.region['left']),
                        'width': int(self.region['width']),
                        'height': int(self.region['height'])
                    }

                    sct_img = sct.grab(target_box)
                    raw_pil = Image.frombytes("RGB", sct_img.size, sct_img.bgra, "raw", "BGRX")
                    
                    # Nâng cấp OCR: Phóng to hình x2 trước khi nhị phân
                    w, h = raw_pil.size
                    scaled_pil = raw_pil.resize((w * 2, h * 2), Image.Resampling.LANCZOS)
                    
                    gray_pil = scaled_pil.convert('L')
                    enhanced_pil = ImageEnhance.Contrast(gray_pil).enhance(3.0)
                    binary_pil = enhanced_pil.point(lambda p: 255 if p > 160 else 0)

                    try:
                        ocr_result = loop.run_until_complete(winocr.recognize_pil(binary_pil, lang='en-US'))
                    except Exception:
                        ocr_result = loop.run_until_complete(winocr.recognize_pil(binary_pil))

                    raw_text = ""
                    if isinstance(ocr_result, dict) and 'lines' in ocr_result:
                        raw_text = " ".join([l.get('text', '') for l in ocr_result['lines'] if isinstance(l, dict)])
                    elif hasattr(ocr_result, 'text'):
                        raw_text = ocr_result.text
                    else:
                        raw_text = str(ocr_result)

                    clean_screen = self.clean_text(raw_text)

                    now = time.time()
                    if len(clean_screen) >= 3:
                        # SỬA LỖI MẤT CÂU KHI XUỐNG DÒNG:
                        # Trước đây việc ghép câu (merge_rolling_dialogue) chỉ chạy
                        # SAU KHI màn hình đứng yên 0.3s. Nhưng khi câu dài phải xuống
                        # dòng, chữ luôn "đang chạy" (hiệu ứng gõ chữ) nên không bao giờ
                        # đứng yên cho tới khi dòng đầu đã bị đẩy trôi khỏi vùng quét
                        # -> mất chữ vĩnh viễn, không thể ghép lại được nữa.
                        # => Ghép câu phải chạy NGAY MỖI FRAME (bắt kịp chữ trước khi nó
                        # trôi mất), còn việc GỬI ĐI DỊCH thì vẫn đợi ổn định như cũ.
                        merged_text, is_new_sentence, has_overlap = self.merge_rolling_dialogue(self.accumulated_text, clean_screen)

                        # SỬA LỖI DÍNH RÁC VĨNH VIỄN (ví dụ chữ rác từ giao diện OBS
                        # lúc chưa quét đúng vùng game bị OCR đọc nhầm rồi cứ nối mãi
                        # vào câu thoại thật phía sau, không bao giờ tự xóa được):
                        # Nếu suốt 1.5s liên tục KHÔNG tìm được từ nào trùng lặp giữa
                        # câu đang tích lũy và màn hình hiện tại, thì coi như câu đang
                        # tích lũy đã "hết hạn" / là rác, không còn liên quan gì tới màn
                        # hình thật nữa -> phải bỏ nó đi (coi như sang câu mới hoàn
                        # toàn), bất kể nó có kết thúc bằng dấu câu hay không.
                        # Trường hợp này đánh dấu is_garbage=True để KHÔNG flush đi dịch
                        # (khác với is_new_sentence do gặp dấu câu - đó là câu thật).
                        is_garbage = False
                        if has_overlap:
                            self.last_overlap_time = now
                        elif not is_new_sentence:
                            if self.last_overlap_time == 0:
                                self.last_overlap_time = now
                            elif now - self.last_overlap_time >= 1.5:
                                print(f"[Xóa văn bản rác dính quá lâu]: {self.accumulated_text}")
                                is_new_sentence = True
                                is_garbage = True
                                merged_text = clean_screen

                        # SỬA LỖI BỎ QUA CÂU KHI GAME Ở CHẾ ĐỘ AUTO CHUYỂN CÂU QUÁ NHANH:
                        # Nếu phát hiện ranh giới sang một câu thoại HOÀN TOÀN MỚI (không
                        # còn từ nào trùng với câu cũ), nhưng câu cũ vẫn CHƯA từng được gửi
                        # đi dịch (do chưa kịp đứng yên đủ 0.30s trước khi bị ghi đè) thì
                        # phải dịch ngay câu cũ đó trước khi cho phép thay bằng câu mới -
                        # tuyệt đối không được âm thầm xóa mất một câu chưa dịch.
                        # (Nếu câu cũ bị xóa vì là RÁC (is_garbage) thì không flush,
                        # vì đó không phải một câu thoại thật đang chờ dịch.)
                        if is_new_sentence and not is_garbage and self.accumulated_text and self.accumulated_text != self.last_translated:
                            flush_text = self.accumulated_text
                            print(f"[Flush câu bị AUTO chuyển quá nhanh]: {flush_text}")
                            self.last_translated = flush_text
                            self.req_id += 1
                            self.new_subtitle_ready.emit("⏳ Đang dịch...")
                            Thread(
                                target=self.translate_task,
                                args=(flush_text, self.req_id),
                                daemon=True
                            ).start()

                        if is_new_sentence:
                            # Bắt đầu tích lũy lại từ đầu -> đồng hồ chống-dính-rác cũng reset
                            self.last_overlap_time = now

                        if merged_text != self.accumulated_text:
                            self.accumulated_text = merged_text
                            self.last_change_time = now

                        self.accumulated_text_raw = clean_screen

                        # Đợi 0.3s để đảm bảo chữ ngưng chạy rồi mới gửi đi dịch
                        if now - self.last_change_time >= 0.30:
                            full_sentence = self.accumulated_text

                            if full_sentence != self.last_translated:
                                print(f"[Text hoàn chỉnh]: {full_sentence}")
                                self.last_translated = full_sentence
                                self.req_id += 1

                                self.new_subtitle_ready.emit("⏳ Đang dịch...")
                                Thread(
                                    target=self.translate_task,
                                    args=(full_sentence, self.req_id),
                                    daemon=True
                                ).start()
                    else:
                        if self.accumulated_text:
                            self.accumulated_text = ""
                            self.accumulated_text_raw = ""
                            self.last_translated = ""
                            self.new_subtitle_ready.emit("...")
                except Exception:
                    pass

                time.sleep(0.06)

        loop.close()

    def stop(self):
        self.running = False
        self.wait()


class SnippingOverlay(QtWidgets.QWidget):
    area_selected = QtCore.pyqtSignal(dict)

    def __init__(self):
        super().__init__()
        self.setWindowFlags(
            QtCore.Qt.WindowType.FramelessWindowHint | 
            QtCore.Qt.WindowType.WindowStaysOnTopHint |
            QtCore.Qt.WindowType.Tool
        )
        self.setAttribute(QtCore.Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setCursor(QtCore.Qt.CursorShape.CrossCursor)
        self.setGeometry(QtGui.QGuiApplication.primaryScreen().geometry())
        self.start_pos = None
        self.end_pos = None

    def mousePressEvent(self, event):
        if event.button() == QtCore.Qt.MouseButton.LeftButton:
            self.start_pos = event.globalPosition().toPoint()
            self.end_pos = self.start_pos
            self.update()

    def mouseMoveEvent(self, event):
        if self.start_pos:
            self.end_pos = event.globalPosition().toPoint()
            self.update()

    def mouseReleaseEvent(self, event):
        if event.button() == QtCore.Qt.MouseButton.LeftButton and self.start_pos:
            rect = QtCore.QRect(self.start_pos, event.globalPosition().toPoint()).normalized()
            if rect.width() > 30 and rect.height() > 15:
                ratio = self.screen().devicePixelRatio()
                self.area_selected.emit({
                    'top': int(rect.top() * ratio),
                    'left': int(rect.left() * ratio),
                    'width': int(rect.width() * ratio),
                    'height': int(rect.height() * ratio)
                })
            self.close()

    def paintEvent(self, event):
        painter = QtGui.QPainter(self)
        painter.fillRect(self.rect(), QtGui.QColor(0, 0, 0, 90))

        if self.start_pos and self.end_pos:
            rect = QtCore.QRect(self.start_pos, self.end_pos).normalized()
            painter.setCompositionMode(QtGui.QPainter.CompositionMode.CompositionMode_Clear)
            painter.fillRect(rect, QtCore.Qt.GlobalColor.transparent)
            painter.setCompositionMode(QtGui.QPainter.CompositionMode.CompositionMode_SourceOver)
            painter.setPen(QtGui.QPen(QtGui.QColor(0, 229, 255), 2))
            painter.drawRect(rect)


class SubtitleViewer(QtWidgets.QWidget):
    toggle_lock_signal = QtCore.pyqtSignal()

    def __init__(self):
        super().__init__()
        self.translated_text = "Bấm [🎯 Quét Vùng] và quét khung thoại game..."
        self.is_locked = False
        self.is_running = True
        self.drag_position = QtCore.QPoint()
        self.resizing = False
        self.snipper = None

        self.setWindowFlags(
            QtCore.Qt.WindowType.FramelessWindowHint |
            QtCore.Qt.WindowType.WindowStaysOnTopHint |
            QtCore.Qt.WindowType.Tool
        )
        self.setAttribute(QtCore.Qt.WidgetAttribute.WA_TranslucentBackground, True)
        
        self.setGeometry(150, 720, 1150, 160)
        self.setMinimumSize(500, 100)

        self.toggle_lock_signal.connect(self.toggle_lock)
        try:
            keyboard.add_hotkey('f9', self.toggle_lock_signal.emit)
        except Exception:
            pass

        self.worker = SubtitleWorker()
        self.worker.new_subtitle_ready.connect(self.update_sub)
        self.worker.start()

    def start_selection(self):
        self.snipper = SnippingOverlay()
        self.snipper.area_selected.connect(self.on_region_selected)
        self.snipper.show()

    def on_region_selected(self, region):
        self.worker.update_region(region)
        self.translated_text = "Đang nhận diện theo thời gian thực..."
        self.update()

    def toggle_play_pause(self):
        self.is_running = not self.is_running
        self.worker.set_paused(not self.is_running)
        if not self.is_running:
            self.translated_text = "⏸ [Đang tạm dừng dịch]"
        self.update()

    def exit_app(self):
        try:
            self.worker.stop()
        except Exception:
            pass
        
        try:
            parent_pid = os.getppid()
            os.system(f"taskkill /F /PID {parent_pid} >nul 2>&1")
        except Exception:
            pass
            
        os._exit(0)

    def update_sub(self, text):
        self.translated_text = text if text else "..."
        self.update()

    def toggle_lock(self):
        self.is_locked = not self.is_locked
        if self.is_locked:
            self.setWindowFlag(QtCore.Qt.WindowType.WindowTransparentForInput, True)
        else:
            self.setWindowFlag(QtCore.Qt.WindowType.WindowTransparentForInput, False)
        self.show()
        self.update()

    def mousePressEvent(self, event):
        if self.is_locked:
            return
        if event.button() == QtCore.Qt.MouseButton.LeftButton:
            x, y = event.position().x(), event.position().y()
            
            if 10 <= x <= 130 and 8 <= y <= 34:
                self.start_selection()
                return
            if 138 <= x <= 250 and 8 <= y <= 34:
                self.toggle_play_pause()
                return
            if 258 <= x <= 348 and 8 <= y <= 34:
                self.toggle_lock()
                return
            if 356 <= x <= 430 and 8 <= y <= 34:
                self.exit_app()
                return

            if x > self.width() - 25 and y > self.height() - 25:
                self.resizing = True
            else:
                self.drag_position = event.globalPosition().toPoint() - self.frameGeometry().topLeft()

    def mouseMoveEvent(self, event):
        if self.is_locked:
            return
        if event.position().x() > self.width() - 25 and event.position().y() > self.height() - 25:
            self.setCursor(QtCore.Qt.CursorShape.SizeFDiagCursor)
        else:
            self.setCursor(QtCore.Qt.CursorShape.ArrowCursor)

        if event.buttons() == QtCore.Qt.MouseButton.LeftButton:
            if self.resizing:
                self.resize(max(500, int(event.position().x())), max(100, int(event.position().y())))
            elif not self.drag_position.isNull():
                self.move(event.globalPosition().toPoint() - self.drag_position)

    def mouseReleaseEvent(self, event):
        self.resizing = False
        self.drag_position = QtCore.QPoint()

    def paintEvent(self, event):
        painter = QtGui.QPainter(self)
        painter.setRenderHint(QtGui.QPainter.RenderHint.Antialiasing)
        painter.setRenderHint(QtGui.QPainter.RenderHint.TextAntialiasing)
        
        w, h = self.width(), self.height()
        rect = QtCore.QRect(0, 0, w, h)

        bg_alpha = 205 if self.is_locked else 240
        painter.setBrush(QtGui.QColor(8, 12, 18, bg_alpha))
        border_pen = QtGui.QPen(QtGui.QColor(0, 229, 255, 140) if not self.is_locked else QtCore.Qt.GlobalColor.transparent, 1.5)
        painter.setPen(border_pen)
        painter.drawRoundedRect(rect.adjusted(2, 2, -2, -2), 12, 12)

        top_offset = 14
        if not self.is_locked:
            top_offset = 46
            painter.setBrush(QtGui.QColor(0, 180, 216, 220))
            painter.setPen(QtCore.Qt.PenStyle.NoPen)
            painter.drawRoundedRect(10, 8, 120, 26, 5, 5)
            painter.setPen(QtGui.QColor(255, 255, 255))
            painter.setFont(QtGui.QFont("Segoe UI", 9, QtGui.QFont.Weight.Bold))
            painter.drawText(10, 8, 120, 26, QtCore.Qt.AlignmentFlag.AlignCenter, "🎯 Quét Vùng")

            btn_color = QtGui.QColor(40, 167, 69, 220) if not self.is_running else QtGui.QColor(255, 193, 7, 220)
            painter.setBrush(btn_color)
            painter.drawRoundedRect(138, 8, 112, 26, 5, 5)
            painter.setPen(QtGui.QColor(0, 0, 0) if self.is_running else QtGui.QColor(255, 255, 255))
            status_text = "⏸ Tạm dừng" if self.is_running else "▶ Tiếp tục"
            painter.drawText(138, 8, 112, 26, QtCore.Qt.AlignmentFlag.AlignCenter, status_text)

            painter.setBrush(QtGui.QColor(247, 127, 0, 220))
            painter.drawRoundedRect(258, 8, 90, 26, 5, 5)
            painter.setPen(QtGui.QColor(255, 255, 255))
            painter.drawText(258, 8, 90, 26, QtCore.Qt.AlignmentFlag.AlignCenter, "🔒 Khóa [F9]")

            painter.setBrush(QtGui.QColor(220, 53, 69, 220))
            painter.drawRoundedRect(356, 8, 74, 26, 5, 5)
            painter.drawText(356, 8, 74, 26, QtCore.Qt.AlignmentFlag.AlignCenter, "❌ Thoát")

            painter.setPen(QtGui.QColor(160, 175, 190))
            painter.setFont(QtGui.QFont("Segoe UI", 8))
            painter.drawText(445, 25, "✛ Kéo thanh | Kéo góc phải để mở rộng")

            painter.setBrush(QtGui.QColor(0, 229, 255, 200))
            painter.setPen(QtCore.Qt.PenStyle.NoPen)
            handle = QtGui.QPainterPath()
            handle.moveTo(w - 14, h - 2)
            handle.lineTo(w - 2, h - 14)
            handle.lineTo(w - 2, h - 2)
            painter.drawPath(handle)

        # =======================================================
        # GIAO DIỆN TỰ ĐỘNG THU NHỎ FONT VÀ BẮT ĐẦU TỪ SIZE 17
        # =======================================================
        # Mở rộng text_rect để tối đa diện tích vẽ
        text_rect = QtCore.QRect(10, top_offset + 2, w - 20, h - top_offset - 10)
        flags = QtCore.Qt.AlignmentFlag.AlignCenter | QtCore.Qt.TextFlag.TextWordWrap

        font_size = 17  
        font = QtGui.QFont("Segoe UI", font_size, QtGui.QFont.Weight.DemiBold)
        font.setLetterSpacing(QtGui.QFont.SpacingType.AbsoluteSpacing, 0.5)
        metrics = QtGui.QFontMetrics(font)
        
        # SỬA LỖI TRÀN CHỮ: Nếu chữ siêu dài, ép font size nhỏ dần xuống mức 9
        while font_size > 9:
            bound_rect = metrics.boundingRect(text_rect, flags, self.translated_text)
            if bound_rect.height() <= text_rect.height() and bound_rect.width() <= text_rect.width():
                break
            font_size -= 1
            font.setPointSize(font_size)
            metrics = QtGui.QFontMetrics(font)

        painter.setFont(font)
        
        # Bóng viền đen nổi bật
        painter.setPen(QtGui.QColor(0, 0, 0, 255))
        for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (-1, 1), (1, -1)]:
            painter.drawText(text_rect.adjusted(dx, dy, dx, dy), flags, self.translated_text)

        # Thân chữ vàng sáng
        painter.setPen(QtGui.QColor(255, 238, 88))
        painter.drawText(text_rect, flags, self.translated_text)

    def closeEvent(self, event):
        self.exit_app()
        event.accept()


if __name__ == "__main__":
    app = QtWidgets.QApplication(sys.argv)
    window = SubtitleViewer()
    window.show()
    sys.exit(app.exec())