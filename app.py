import os
import json
import shutil
import tempfile
import zipfile
import time
import threading
import logging
import re
import sys
import ctypes
import subprocess
import io # 新增：用于内存文件流
from pathlib import Path
from urllib.parse import urlparse
from flask import Flask, jsonify, render_template, request, send_from_directory, send_file # 新增 send_file

# 控制台设置
if sys.platform == 'win32':
    try:
        ctypes.windll.kernel32.SetConsoleOutputCP(65001)
        ctypes.windll.kernel32.SetConsoleCP(65001)
    except: pass

print("""
╔════════════════════════════════════════════════╗
║                                                ║
║                                                ║
║            TLD 网页模组安装器                  ║
║         The Long Drive Mod Installer           ║
║              QQ群:661726941                    ║
║                                                ║
║                                                ║
╚════════════════════════════════════════════════╝
""")

import requests

# ==================== 路径与日志 ====================
def get_resource_path(relative_path):
    try: base_path = sys._MEIPASS
    except Exception: base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)

# 同步用户修改：去除颜色代码
class ColoredFormatter(logging.Formatter):
    def format(self, record):
        formatter = logging.Formatter('%(asctime)s - %(levelname)s - %(message)s', datefmt='%H:%M:%S')
        return formatter.format(record)

def setup_logging():
    if getattr(sys, 'frozen', False): log_file = os.path.join(os.path.dirname(sys.executable), 'app.log')
    else: log_file = 'app.log'
    logging.basicConfig(level=logging.INFO, handlers=[
        logging.FileHandler(log_file, encoding='utf-8'),
        logging.StreamHandler()
    ], force=True)
    for handler in logging.getLogger().handlers:
        if isinstance(handler, logging.StreamHandler) and not isinstance(handler, logging.FileHandler):
            handler.setFormatter(ColoredFormatter())
    return logging.getLogger(__name__)

logger = setup_logging()

# ==================== 语言与配置 ====================
def load_translations(lang_code="zh"):
    try:
        path = Path(get_resource_path("translations")) / f"{lang_code}.json"
        if path.exists(): return json.loads(path.read_text("utf-8"))
    except: return {}

app = Flask(__name__, template_folder=get_resource_path('templates'), static_folder=get_resource_path('static'))
BASE_DIR = Path(get_resource_path("."))
DOCUMENTS_PATH = Path.home() / "Documents"
GAME_PATH = DOCUMENTS_PATH / "TheLongDrive"
MODS_PATH = GAME_PATH / "Mods"
VERSIONS_PATH = MODS_PATH / "temp" / "Versions"
CONFIG_FILE = GAME_PATH / "installer_config.json"

MODS_PATH.mkdir(parents=True, exist_ok=True)
VERSIONS_PATH.mkdir(parents=True, exist_ok=True)

# 同步用户修改：数据源配置
MODLIST_SOURCES = [
    {"name": "Official source(English)", "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/modlist_3.json"},
    {"name": "极狐镜像源(中文)", "url": "https://jihulab.com/XLDev/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/modlist_3.json"},
    {"name": "Local Source(english)", "url": None, "local_path": BASE_DIR / "en-modlist_3.json"},
    {"name": "本地源(中文)", "url": None, "local_path": BASE_DIR / "modlist_3.json"}
]

MODPACK_SOURCES = [
    {"name": "Official source(English)", "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"},
    {"name": "极狐镜像源(中文)", "url": "https://jihulab.com/XLDev/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"},
    {"name": "Local Source(english)", "url": None, "local_path": BASE_DIR / "Modpacks" / "en-modlist_3.json"},
    {"name": "本地源(中文)", "url": None, "local_path": BASE_DIR / "Modpacks" / "modlist_3.json"}
]

download_tasks = []
task_counter = 0
task_lock = threading.Lock()

# ==================== 工具函数 ====================
def load_config():
    if CONFIG_FILE.exists():
        try: return json.loads(CONFIG_FILE.read_text("utf-8"))
        except: return {}
    return {}

def save_config(cfg):
    CONFIG_FILE.write_text(json.dumps(cfg, ensure_ascii=False), encoding="utf-8")

def fetch_with_retry(url, max_retries=3, timeout=10):
    for attempt in range(max_retries):
        try:
            r = requests.get(url, timeout=timeout)
            if r.status_code == 200: return r
        except: pass
        time.sleep(1)
    return None

def get_normalized_filename(filename):
    if not filename: return ""
    return re.sub(r'[^a-zA-Z0-9]', '', os.path.splitext(filename)[0]).lower()

# ==================== 数据加载 ====================
def load_data_from_source(sources, source_index, data_key="Mods", strict=False):
    if source_index >= len(sources): return [], source_index
    source = sources[source_index]
    data = []
    try:
        if source.get("url"):
            logger.info(f"尝试从 {source['name']} 加载...")
            resp = fetch_with_retry(source["url"])
            if resp:
                data = resp.json().get(data_key, [])
                if data: return data, source_index
        else:
            path = source.get("local_path")
            if path and path.exists():
                with open(path, "r", encoding="utf-8") as f:
                    data = json.load(f).get(data_key, [])
                    if data: return data, source_index
    except Exception as e: logger.error(f"加载失败: {e}")
    
    if not strict and source_index + 1 < len(sources):
        return load_data_from_source(sources, source_index + 1, data_key, False)
    return [], source_index

# ==================== 安装逻辑 ====================
def get_installed_mods():
    installed = {}
    if VERSIONS_PATH.exists():
        for f in VERSIONS_PATH.glob("*.txt"):
            installed[f.stem] = f.read_text(encoding="utf-8").strip()
    return installed

def set_installed_version(mod_name, version):
    (VERSIONS_PATH / f"{mod_name}.txt").write_text(version, encoding="utf-8")

def add_download_task(filename, mod_name):
    global task_counter
    with task_lock:
        task_id = task_counter
        task_counter += 1
        download_tasks.append({"id": task_id, "filename": filename, "name": mod_name, "status": "pending", "progress": 0})
    return task_id

def update_task(task_id, **kwargs):
    with task_lock:
        for t in download_tasks:
            if t["id"] == task_id: t.update(kwargs); break

def download_file_with_progress(url, dest_path, task_id):
    try:
        r = requests.get(url, stream=True, timeout=30)
        r.raise_for_status()
        total = int(r.headers.get('content-length', 0))
        update_task(task_id, total_size=total, status="downloading")
        dl = 0; start = time.time()
        with open(dest_path, "wb") as f:
            for chunk in r.iter_content(8192):
                if chunk:
                    f.write(chunk); dl += len(chunk)
                    update_task(task_id, downloaded=dl, progress=(dl/total*100 if total else 0), speed=dl/(time.time()-start))
        update_task(task_id, status="completed", progress=100)
        return True
    except Exception as e:
        update_task(task_id, status="failed", error=str(e))
        return False

def install_mod(mod, installed_records):
    filename = mod["FileName"]
    link = mod["Link"]
    mod_name = mod.get("Name", filename)
    
    if mod_name in installed_records: return False, "Already installed"
    
    task_id = add_download_task(filename, mod_name)
    with tempfile.NamedTemporaryFile(delete=False) as tmp: tmp_path = tmp.name
    try:
        if not download_file_with_progress(link, tmp_path, task_id): return False, "Download failed"
        extract_temp = Path(tempfile.mkdtemp())
        try:
            with zipfile.ZipFile(tmp_path, "r") as z: z.extractall(extract_temp)
        except: shutil.copy2(tmp_path, extract_temp / filename)
        
        files_record = []
        for item in extract_temp.iterdir():
            target = MODS_PATH / item.name
            if item.is_dir():
                if target.exists(): shutil.rmtree(target)
                shutil.copytree(item, target)
                for root, _, files in os.walk(target): files_record.extend([os.path.join(root, f) for f in files])
            else:
                shutil.copy2(item, target)
                files_record.append(str(target))
        
        (VERSIONS_PATH / f"{mod_name}_manifest.json").write_text(json.dumps(files_record), encoding="utf-8")
        set_installed_version(mod_name, mod.get("Version", "0"))
        return True, mod.get("Version", "0")
    except Exception as e: return False, str(e)
    finally:
        if os.path.exists(tmp_path): os.unlink(tmp_path)
        if 'extract_temp' in locals(): shutil.rmtree(extract_temp, ignore_errors=True)

def uninstall_mod(mod_name):
    if not mod_name: return False, "Invalid name"
    manifest = VERSIONS_PATH / f"{mod_name}_manifest.json"
    if manifest.exists():
        try:
            for f in json.loads(manifest.read_text()):
                p = Path(f)
                if p.exists() and p.is_file():
                    try: p.resolve().relative_to(MODS_PATH.resolve())
                    except: continue
                    p.unlink()
            manifest.unlink()
        except: pass
    (VERSIONS_PATH / f"{mod_name}.txt").unlink(missing_ok=True)
    return True, "OK"

# ==================== 模组包 ====================
def install_modpack_from_list(files, source_index):
    """核心安装逻辑：根据文件名列表安装"""
    all_mods, _ = load_data_from_source(MODLIST_SOURCES, source_index)
    installed = get_installed_mods()
    results = []
    
    for fn in files:
        if not fn.strip(): continue
        mod = next((m for m in all_mods if m.get("FileName") == fn), None)
        if not mod: mod = next((m for m in all_mods if get_normalized_filename(m.get("FileName")) == get_normalized_filename(fn)), None)
        
        if not mod: results.append(f"❌ {fn}: 未找到"); continue
        
        mod_name = mod.get("Name", fn)
        if mod_name in installed:
            results.append(f"⏭️ {mod_name}: 已安装"); continue
        
        ok, msg = install_mod(mod, installed)
        if ok: 
            installed[mod_name] = msg
            results.append(f"✅ {mod_name}: 安装成功")
        else: results.append(f"❌ {mod_name}: {msg}")
    
    return results

# ==================== API ====================
@app.route("/")
def index():
    lang = request.args.get("lang", "zh")
    return render_template("index.html", translations=load_translations(lang))

@app.route("/static/<path:filename>")
def serve_static(filename): return send_from_directory(get_resource_path("static"), filename)

@app.route("/api/sources")
def get_sources(): return jsonify([{"name": s["name"], "index": i} for i, s in enumerate(MODLIST_SOURCES)])

@app.route("/api/mods")
def get_mods():
    source_idx = request.args.get("source", 0, type=int)
    strict = request.args.get("strict", "false") == "true"
    mods, active_idx = load_data_from_source(MODLIST_SOURCES, source_idx, strict=strict)
    installed = get_installed_mods()
    for m in mods:
        mn = m.get("Name", m.get("FileName"))
        m["is_installed"] = mn in installed
        m["installed_version"] = installed.get(mn, "")
    return jsonify({"mods": mods, "active_source": active_idx})

@app.route("/api/modpacks")
def get_modpacks():
    source_idx = request.args.get("source", 0, type=int)
    packs, active_idx = load_data_from_source(MODPACK_SOURCES, source_idx, "Mods")
    return jsonify({"modpacks": packs, "active_source": active_idx})

@app.route("/api/tasks")
def get_tasks():
    with task_lock: return jsonify(download_tasks.copy())

@app.route("/api/install", methods=["POST"])
def api_install():
    data = request.json
    mods, _ = load_data_from_source(MODLIST_SOURCES, data.get("source", 0))
    mod = next((m for m in mods if m.get("FileName") == data.get("filename")), None)
    if not mod: return jsonify({"error": "Not found"}), 404
    
    ok, msg = install_mod(mod, get_installed_mods())
    if ok: return jsonify({"success": True, "new_version": msg})
    else: return jsonify({"error": msg}), 500

@app.route("/api/uninstall", methods=["POST"])
def api_uninstall():
    name = request.json.get("name")
    ok, msg = uninstall_mod(name)
    return jsonify({"success": ok}) if ok else jsonify({"error": msg}), 500

@app.route("/api/update", methods=["POST"])
def api_update():
    name = request.json.get("name")
    source = request.json.get("source", 0)
    mods, _ = load_data_from_source(MODLIST_SOURCES, source)
    mod = next((m for m in mods if m.get("Name") == name), None)
    if not mod: return jsonify({"error": "Not found"}), 404
    
    uninstall_mod(name)
    ok, msg = install_mod(mod, get_installed_mods())
    if ok: return jsonify({"success": True, "new_version": msg})
    return jsonify({"error": msg}), 500

@app.route("/api/install-modpack", methods=["POST"])
def api_install_modpack():
    data = request.json
    txt_url = data.get("Link")
    source = data.get("source", 0)
    
    if not txt_url: return jsonify({"error": "No link"}), 500
    
    # 下载txt内容
    try:
        r = requests.get(txt_url, timeout=30)
        if r.status_code != 200: return jsonify({"error": "Failed to download list"}), 500
        files = [l.strip() for l in r.text.splitlines() if l.strip() and not l.startswith("#")]
    except Exception as e:
        return jsonify({"error": str(e)}), 500

    results = install_modpack_from_list(files, source)
    return jsonify({"success": True, "results": results})

# 新增：导入模组包
@app.route("/api/import-modpack", methods=["POST"])
def api_import_modpack():
    if 'file' not in request.files:
        return jsonify({"error": "No file part"}), 400
    file = request.files['file']
    if file.filename == '':
        return jsonify({"error": "No selected file"}), 400
    
    if file:
        try:
            content = file.read().decode('utf-8')
            files = [l.strip() for l in content.splitlines() if l.strip() and not l.startswith("#")]
            # 默认使用当前浏览源作为查找源
            source = request.form.get('source', 0, type=int)
            results = install_modpack_from_list(files, source)
            return jsonify({"success": True, "results": results})
        except Exception as e:
            return jsonify({"error": str(e)}), 500

# 新增：导出模组包
@app.route("/api/export-modpack")
def api_export_modpack():
    try:
        dll_files = []
        # 仅扫描 Mods 根目录下的 dll，符合大多数模组情况
        # 如果需要递归扫描所有子文件夹，可以使用 os.walk
        for item in MODS_PATH.iterdir():
            if item.is_file() and item.suffix.lower() == '.dll':
                dll_files.append(item.name)
        
        if not dll_files:
            return jsonify({"error": "No DLL files found in Mods folder"}), 404

        # 生成文本内容
        content = "\n".join(dll_files)
        
        # 创建内存文件流
        return send_file(
            io.BytesIO(content.encode('utf-8')),
            mimetype='text/plain',
            as_attachment=True,
            download_name='my_modpack.txt'
        )
    except Exception as e:
        return jsonify({"error": str(e)}), 500

@app.route("/api/config", methods=["GET", "POST"])
def api_config():
    if request.method == "POST":
        save_config(request.json)
        return jsonify({"success": True})
    return jsonify(load_config())

@app.route("/api/launch-exe", methods=["POST"])
def launch_exe():
    path = Path(request.json.get("path", ""))
    if not path.exists(): return jsonify({"error": "File not found"}), 404
    try:
        subprocess.Popen([str(path)], cwd=path.parent)
        return jsonify({"success": True})
    except Exception as e: return jsonify({"error": str(e)}), 500

@app.route("/api/install-patcher", methods=["POST"])
def install_patcher():
    p = BASE_DIR / "TLDPatcher" / "TLDPatcher.exe"
    if not p.exists(): return jsonify({"error": "TLDPatcher not found"}), 404
    try:
        subprocess.Popen([str(p)], cwd=p.parent)
        return jsonify({"success": True})
    except Exception as e: return jsonify({"error": str(e)}), 500

@app.route("/api/browse-exe", methods=["POST"])
def browse_exe():
    if sys.platform != 'win32': return jsonify({"error": "Only Windows supported"}), 400
    ps_script = """
    Add-Type -AssemblyName System.Windows.Forms
    $FileBrowser = New-Object System.Windows.Forms.OpenFileDialog -Property @{
        Filter = 'Executable Files (*.exe)|*.exe'; Title = 'Select Game EXE'; RestoreDirectory = $true
    }
    if ($FileBrowser.ShowDialog() -eq 'OK') { $FileBrowser.FileName }
    """
    try:
        result = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps_script], capture_output=True, text=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
        path = result.stdout.strip()
        if path and os.path.exists(path): return jsonify({"success": True, "path": path})
        return jsonify({"error": "No file selected"}), 400
    except Exception as e: return jsonify({"error": str(e)}), 500

@app.route("/api/license")
def get_license():
    try:
        f = BASE_DIR / "LICENSE_AND_NOTICE.md"
        return f.read_text(encoding="utf-8") if f.exists() else "Not found", 200
    except: return "Error", 500

@app.route("/api/translations")
def api_translations():
    return jsonify(load_translations(request.args.get("lang", "zh")))

if __name__ == "__main__":
    import webbrowser
    threading.Thread(target=lambda: (time.sleep(1), webbrowser.open("http://127.0.0.1:5000")), daemon=True).start()
    app.run(debug=False, threaded=True)
