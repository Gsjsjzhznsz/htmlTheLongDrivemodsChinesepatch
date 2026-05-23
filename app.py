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
import io
import requests
import urllib3
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
from flask import Flask, jsonify, render_template, request, send_from_directory, send_file

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

# ==================== 版本配置 ====================
CURRENT_VERSION = "v5.0"
GITHUB_REPO = "Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch"

if sys.platform == 'win32':
    try:
        ctypes.windll.kernel32.SetConsoleOutputCP(65001)
        ctypes.windll.kernel32.SetConsoleCP(65001)
    except Exception:
        pass

print("""
╔════════════════════════════════════════════════╗
║            TLD 网页模组安装器                  ║
║         The Long Drive Mod Installer           ║
║              QQ群:661726941                    ║
╚════════════════════════════════════════════════╝
""")

# ==================== 路径与日志 ====================
# 找到 app.py 中的这个函数，替换为：
def get_resource_path(relative_path):
    try:
        base_path = sys._MEIPASS
    except Exception:
        # 用脚本所在目录，而非当前工作目录
        base_path = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base_path, relative_path)

def setup_logging():
    if getattr(sys, 'frozen', False):
        log_file = os.path.join(os.path.dirname(sys.executable), 'app.log')
    else:
        log_file = 'app.log'
    logging.basicConfig(level=logging.INFO, handlers=[
        logging.FileHandler(log_file, encoding='utf-8'),
        logging.StreamHandler()
    ], force=True)
    fmt = logging.Formatter('%(asctime)s - %(levelname)s - %(message)s', datefmt='%H:%M:%S')
    for h in logging.getLogger().handlers:
        if isinstance(h, logging.StreamHandler) and not isinstance(h, logging.FileHandler):
            h.setFormatter(fmt)
    return logging.getLogger(__name__)

logger = setup_logging()

# ==================== 路径常量 ====================
BASE_DIR = Path(get_resource_path("")).resolve()
DOCUMENTS_PATH = Path.home() / "Documents"
GAME_PATH = DOCUMENTS_PATH / "TheLongDrive"
MODS_PATH = GAME_PATH / "Mods"
VERSIONS_PATH = MODS_PATH / "temp" / "Versions"
CONFIG_FILE = GAME_PATH / "installer_config.json"
CONFLICTS_FILE = BASE_DIR / "mod_conflicts.json"

MODS_PATH.mkdir(parents=True, exist_ok=True)
VERSIONS_PATH.mkdir(parents=True, exist_ok=True)

# ==================== 数据源 ====================
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

# ==================== 下载任务管理 ====================
download_tasks = []
task_counter = 0
task_lock = threading.Lock()

# 并发下载线程池
MAX_CONCURRENT_DOWNLOADS = 3
download_executor = ThreadPoolExecutor(max_workers=MAX_CONCURRENT_DOWNLOADS, thread_name_prefix="dl")
install_lock = threading.Lock()  # 安装操作互斥锁

def add_download_task(filename, mod_name):
    global task_counter
    with task_lock:
        task_id = task_counter
        task_counter += 1
        download_tasks.append({
            "id": task_id, "filename": filename, "name": mod_name,
            "status": "pending", "progress": 0, "created_at": time.time()
        })
    return task_id

def update_task(task_id, **kwargs):
    with task_lock:
        for t in download_tasks:
            if t["id"] == task_id:
                t.update(kwargs)
                break

def cleanup_old_tasks(max_age=600):
    now = time.time()
    with task_lock:
        download_tasks[:] = [
            t for t in download_tasks
            if t.get("status") in ("pending", "downloading")
            or (now - t.get("completed_at", now)) < max_age
        ]

# ==================== 版本比较（语义化）====================
def parse_version(v):
    """
    语义化版本解析，返回可比较的元组。
    'v1.10.3' -> (1, 10, 3)
    '1.2-beta' -> (1, 2, -1)  预发布版本排后
    '2.0' -> (2, 0, 0)        补零对齐
    """
    if not v:
        return (0,)
    v = str(v).strip().lstrip("vV")
    # 分离预发布标记
    pre_release = 0
    if "-" in v:
        parts = v.split("-", 1)
        v = parts[0]
        pre_release = -1  # 预发布版本号低于正式版
    try:
        num_parts = tuple(int(p) for p in v.split("."))
        # 补零到至少3位
        while len(num_parts) < 3:
            num_parts = num_parts + (0,)
        return num_parts + (pre_release,)
    except (ValueError, AttributeError):
        return (0,)

# ==================== 工具函数 ====================
def load_config():
    if CONFIG_FILE.exists():
        try:
            return json.loads(CONFIG_FILE.read_text("utf-8"))
        except (OSError, json.JSONDecodeError) as e:
            logger.warning(f"加载配置失败: {e}")
    return {}

def save_config(cfg):
    try:
        CONFIG_FILE.write_text(json.dumps(cfg, ensure_ascii=False), encoding="utf-8")
    except OSError as e:
        logger.error(f"保存配置失败: {e}")

def load_translations(lang_code="zh"):
    try:
        path = Path(get_resource_path("translations")) / f"{lang_code}.json"
        if path.exists():
            return json.loads(path.read_text("utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        logger.warning(f"加载翻译失败 ({lang_code}): {e}")
    return {}

def fetch_with_retry(url, max_retries=3, timeout=10):
    for attempt in range(max_retries):
        try:
            r = requests.get(url, timeout=timeout)
            if r.status_code == 200:
                return r
        except requests.RequestException as e:
            logger.debug(f"请求失败 (尝试 {attempt+1}/{max_retries}): {e}")
        time.sleep(1)
    return None

def get_normalized_filename(filename):
    if not filename:
        return ""
    return re.sub(r'[^a-zA-Z0-9]', '', os.path.splitext(filename)[0]).lower()

def load_data_from_source(sources, source_index, data_key="Mods", strict=False):
    if source_index >= len(sources):
        return [], source_index
    source = sources[source_index]
    data = []
    try:
        if source.get("url"):
            logger.info(f"尝试从 {source['name']} 加载...")
            resp = fetch_with_retry(source["url"])
            if resp:
                data = resp.json().get(data_key, [])
                if data:
                    return data, source_index
        else:
            path = source.get("local_path")
            if path and path.exists():
                with open(path, "r", encoding="utf-8") as f:
                    data = json.load(f).get(data_key, [])
                    if data:
                        return data, source_index
    except (OSError, json.JSONDecodeError, requests.RequestException) as e:
        logger.error(f"加载失败: {e}")
    if not strict and source_index + 1 < len(sources):
        return load_data_from_source(sources, source_index + 1, data_key, False)
    return [], source_index

# ==================== 已安装模组 ====================
def get_installed_mods():
    installed = {}
    if VERSIONS_PATH.exists():
        for f in VERSIONS_PATH.glob("*.txt"):
            name = f.stem
            ver = f.read_text(encoding="utf-8").strip()
            if not name.endswith("_manifest"):
                installed[name] = ver
    return installed

def set_installed_version(mod_name, version):
    (VERSIONS_PATH / f"{mod_name}.txt").write_text(version, encoding="utf-8")

# ==================== 冲突检测 ====================
def load_conflicts():
    """加载冲突规则数据库"""
    try:
        if CONFLICTS_FILE.exists():
            data = json.loads(CONFLICTS_FILE.read_text("utf-8"))
            return data.get("conflicts", [])
    except (OSError, json.JSONDecodeError) as e:
        logger.warning(f"加载冲突规则失败: {e}")
    return []

def check_conflicts(mod_names):
    """
    检查给定模组列表中的冲突。
    返回: [{"mods": [...], "reason": "..."}]
    """
    conflicts = load_conflicts()
    found = []
    mod_set = set(n.lower() for n in mod_names)
    for rule in conflicts:
        rule_mods = rule.get("mod_names", [])
        rule_mods_lower = [m.lower() for m in rule_mods]
        if all(m in mod_set for m in rule_mods_lower):
            found.append({
                "mods": rule_mods,
                "reason": rule.get("reason", "这些模组可能不兼容")
            })
    return found

def check_new_mod_conflicts(new_mod_name, installed_names=None):
    """检查安装新模组是否会与已安装模组冲突"""
    if installed_names is None:
        installed_names = list(get_installed_mods().keys())
    all_names = installed_names + [new_mod_name]
    return check_conflicts(all_names)

# ==================== 依赖解析 ====================
def find_mod_by_dependency(dep_value, all_mods):
    """
    根据 Dependency 字段值查找对应的模组。
    Dependency 可能是：文件名、URL、或描述文本
    """
    if not dep_value:
        return None

    # 尝试按文件名匹配
    for m in all_mods:
        fn = m.get("FileName", "")
        if fn and (fn == dep_value or get_normalized_filename(fn) == get_normalized_filename(dep_value)):
            return m

    # 尝试按 URL 匹配
    for m in all_mods:
        if m.get("Link") == dep_value:
            return m

    # 尝试按模组名称匹配
    for m in all_mods:
        name = m.get("Name", "")
        if name and (name.lower() == dep_value.lower()):
            return m

    return None

def resolve_dependencies(mod, all_mods, installed_records, resolved=None, chain=None):
    """
    递归解析模组依赖，返回需要安装的依赖列表。
    resolved: 已解析的模组集合（避免重复）
    chain: 当前依赖链（检测循环依赖）
    """
    if resolved is None:
        resolved = set()
    if chain is None:
        chain = []

    mod_name = mod.get("Name", mod.get("FileName", ""))
    dep_value = mod.get("Dependency", "")

    if not dep_value:
        return []

    # 检查是否为描述性文本（非文件名/URL）
    if dep_value.startswith("http") or dep_value.endswith(".dll"):
        dep_mod = find_mod_by_dependency(dep_value, all_mods)
    else:
        # 可能是描述文本，尝试查找
        dep_mod = find_mod_by_dependency(dep_value, all_mods)

    if not dep_mod:
        return []  # 无法自动解析，跳过

    dep_name = dep_mod.get("Name", dep_mod.get("FileName", ""))

    # 检查循环依赖
    if dep_name in chain:
        logger.warning(f"检测到循环依赖: {' -> '.join(chain)} -> {dep_name}")
        return []

    # 已安装，无需处理
    if dep_name in installed_records:
        return []

    # 已解析过
    if dep_name in resolved:
        return []

    # 递归解析依赖的依赖
    result = []
    chain = chain + [mod_name]
    sub_deps = resolve_dependencies(dep_mod, all_mods, installed_records, resolved, chain)
    result.extend(sub_deps)

    resolved.add(dep_name)
    result.append(dep_mod)

    return result

# ==================== 安装逻辑 ====================
def download_file_with_progress(url, dest_path, task_id):
    try:
        r = requests.get(url, stream=True, timeout=30)
        r.raise_for_status()
        total = int(r.headers.get('content-length', 0))
        update_task(task_id, total_size=total, status="downloading")
        dl = 0
        start = time.time()
        with open(dest_path, "wb") as f:
            for chunk in r.iter_content(8192):
                if chunk:
                    f.write(chunk)
                    dl += len(chunk)
                    elapsed = time.time() - start
                    speed = dl / elapsed if elapsed > 0 else 0
                    update_task(task_id, downloaded=dl,
                                progress=(dl / total * 100 if total else 0),
                                speed=speed)
        update_task(task_id, status="completed", progress=100, completed_at=time.time())
        return True
    except requests.RequestException as e:
        update_task(task_id, status="failed", error=str(e), completed_at=time.time())
        return False

def install_mod(mod, installed_records):
    """
    安装单个模组（下载+解压+记录）。
    线程安全：安装操作受 install_lock 保护。
    """
    filename = mod["FileName"]
    link = mod["Link"]
    mod_name = mod.get("Name", filename)

    with install_lock:
        if mod_name in installed_records:
            return False, "Already installed"

    task_id = add_download_task(filename, mod_name)
    with tempfile.NamedTemporaryFile(delete=False) as tmp:
        tmp_path = tmp.name

    try:
        if not download_file_with_progress(link, tmp_path, task_id):
            return False, "Download failed"

        extract_temp = Path(tempfile.mkdtemp())
        try:
            with zipfile.ZipFile(tmp_path, "r") as z:
                z.extractall(extract_temp)
        except (zipfile.BadZipFile, OSError):
            shutil.copy2(tmp_path, extract_temp / filename)

        with install_lock:
            files_record = []
            for item in extract_temp.iterdir():
                target = MODS_PATH / item.name
                if item.is_dir():
                    if target.exists():
                        shutil.rmtree(target)
                    shutil.copytree(item, target)
                    for root, _, files in os.walk(target):
                        files_record.extend([os.path.join(root, f) for f in files])
                else:
                    shutil.copy2(item, target)
                    files_record.append(str(target))

            (VERSIONS_PATH / f"{mod_name}_manifest.json").write_text(
                json.dumps(files_record), encoding="utf-8"
            )
            set_installed_version(mod_name, mod.get("Version", "0"))
            installed_records[mod_name] = mod.get("Version", "0")

        return True, mod.get("Version", "0")
    except (OSError, shutil.Error) as e:
        return False, str(e)
    finally:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        if 'extract_temp' in locals():
            shutil.rmtree(extract_temp, ignore_errors=True)

def install_mod_with_deps(mod, all_mods, installed_records):
    """
    安装模组并自动安装依赖。
    返回: (success, version, dep_results)
    """
    dep_results = []

    # 解析依赖
    deps = resolve_dependencies(mod, all_mods, installed_records)

    # 先安装依赖
    for dep_mod in deps:
        dep_name = dep_mod.get("Name", dep_mod.get("FileName", ""))
        ok, msg = install_mod(dep_mod, installed_records)
        if ok:
            dep_results.append(f"📦 自动安装依赖: {dep_name} v{msg}")
        else:
            dep_results.append(f"⚠️ 依赖安装失败: {dep_name} - {msg}")
            # 依赖安装失败，但仍尝试安装主模组

    # 安装主模组
    ok, msg = install_mod(mod, installed_records)
    return ok, msg, dep_results

def uninstall_mod(mod_name):
    if not mod_name:
        return False, "Invalid name"
    manifest = VERSIONS_PATH / f"{mod_name}_manifest.json"
    if manifest.exists():
        try:
            files = json.loads(manifest.read_text(encoding="utf-8"))
            for f in files:
                p = Path(f)
                if p.exists() and p.is_file():
                    try:
                        p.resolve().relative_to(MODS_PATH.resolve())
                    except ValueError:
                        continue
                    p.unlink()
            manifest.unlink()
        except (OSError, json.JSONDecodeError) as e:
            logger.warning(f"清理 manifest 出错: {e}")
    ver_file = VERSIONS_PATH / f"{mod_name}.txt"
    if ver_file.exists():
        try:
            ver_file.unlink()
        except OSError as e:
            logger.warning(f"删除版本文件失败: {e}")
    return True, "OK"

# ==================== 批量操作（并发下载）====================
def batch_install_mods(filenames, all_mods, installed, source_index):
    """并发批量安装模组（含依赖自动安装）"""
    results = []
    futures = {}
    installed_lock = threading.Lock()
    installed_copy = dict(installed)

    def do_install(fn):
        mod = next((m for m in all_mods if m.get("FileName") == fn), None)
        if not mod:
            mod = next((m for m in all_mods if get_normalized_filename(m.get("FileName")) == get_normalized_filename(fn)), None)
        if not mod:
            return f"❌ {fn}: 未找到"

        mod_name = mod.get("Name", fn)
        with installed_lock:
            if mod_name in installed_copy:
                return f"⏭️ {mod_name}: 已安装"

        ok, msg, dep_results = install_mod_with_deps(mod, all_mods, installed_copy)
        with installed_lock:
            if ok:
                installed_copy[mod_name] = msg

        parts = []
        if dep_results:
            parts.extend(dep_results)
        if ok:
            parts.append(f"✅ {mod_name}: 安装成功 v{msg}")
        else:
            parts.append(f"❌ {mod_name}: {msg}")
        return "\n".join(parts)

    for fn in filenames:
        if not fn.strip():
            continue
        future = download_executor.submit(do_install, fn)
        futures[future] = fn

    for future in as_completed(futures):
        try:
            result = future.result(timeout=300)
            results.append(result)
        except Exception as e:
            results.append(f"❌ {futures[future]}: {str(e)}")

    return results

# ==================== Flask 应用 ====================
app = Flask(__name__, template_folder=get_resource_path('templates'), static_folder=get_resource_path('static'))

@app.route("/")
def index():
    lang = request.args.get("lang", "zh")
    return render_template("index.html", translations=load_translations(lang), version=CURRENT_VERSION)

@app.route("/static/<path:filename>")
def serve_static(filename):
    return send_from_directory(get_resource_path("static"), filename)

# ==================== API ====================
@app.route("/api/sources")
def get_sources():
    return jsonify([{"name": s["name"], "index": i} for i, s in enumerate(MODLIST_SOURCES)])

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
    cleanup_old_tasks()
    with task_lock:
        return jsonify(download_tasks.copy())

@app.route("/api/clear-tasks", methods=["POST"])
def clear_tasks():
    with task_lock:
        download_tasks[:] = [t for t in download_tasks if t.get("status") in ("pending", "downloading")]
    return jsonify({"success": True})

# ===== 冲突检测 API =====
@app.route("/api/conflicts")
def get_conflicts():
    return jsonify(load_conflicts())

@app.route("/api/check-conflicts")
def api_check_conflicts():
    """检查指定模组列表是否有冲突，或检查已安装模组间的冲突"""
    names = request.args.getlist("names")
    if not names:
        names = list(get_installed_mods().keys())
    found = check_conflicts(names)
    return jsonify({"conflicts": found})

@app.route("/api/check-new-conflicts", methods=["POST"])
def api_check_new_conflicts():
    """检查安装新模组是否会与已安装模组冲突"""
    data = request.json
    new_name = data.get("name", "")
    installed = list(get_installed_mods().keys())
    found = check_new_mod_conflicts(new_name, installed)
    return jsonify({"conflicts": found})

# ===== 依赖解析 API =====
@app.route("/api/resolve-deps", methods=["POST"])
def api_resolve_deps():
    """解析模组的依赖关系"""
    data = request.json
    filename = data.get("filename", "")
    source = data.get("source", 0)
    all_mods, _ = load_data_from_source(MODLIST_SOURCES, source)
    installed = get_installed_mods()

    mod = next((m for m in all_mods if m.get("FileName") == filename), None)
    if not mod:
        return jsonify({"dependencies": []})

    deps = resolve_dependencies(mod, all_mods, installed)
    dep_info = []
    for d in deps:
        dn = d.get("Name", d.get("FileName", ""))
        dep_info.append({
            "name": dn,
            "version": d.get("Version", ""),
            "is_installed": dn in installed,
            "filename": d.get("FileName", "")
        })

    return jsonify({"dependencies": dep_info})

# ===== 安装/卸载/更新 API =====
@app.route("/api/install", methods=["POST"])
def api_install():
    data = request.json
    filename = data.get("filename", "")
    source = data.get("source", 0)
    auto_deps = data.get("auto_deps", True)

    mods, _ = load_data_from_source(MODLIST_SOURCES, source)
    mod = next((m for m in mods if m.get("FileName") == filename), None)
    if not mod:
        return jsonify({"error": "Not found"}), 404

    installed = get_installed_mods()

    if auto_deps:
        ok, msg, dep_results = install_mod_with_deps(mod, mods, installed)
    else:
        ok, msg, dep_results = install_mod(mod, installed), []

    if ok:
        return jsonify({"success": True, "new_version": msg, "name": mod.get("Name"), "dep_results": dep_results})
    else:
        return jsonify({"error": msg}), 500

@app.route("/api/batch-install", methods=["POST"])
def api_batch_install():
    data = request.json
    filenames = [fn for fn in data.get("filenames", []) if fn.strip()]
    source = data.get("source", 0)
    all_mods, _ = load_data_from_source(MODLIST_SOURCES, source)
    installed = get_installed_mods()
    results = batch_install_mods(filenames, all_mods, installed, source)
    return jsonify({"success": True, "results": results})

@app.route("/api/batch-uninstall", methods=["POST"])
def api_batch_uninstall():
    data = request.json
    names = data.get("names", [])
    results = []
    for name in names:
        ok, msg = uninstall_mod(name)
        results.append(f"✅ {name}: 卸载成功" if ok else f"❌ {name}: {msg}")
    return jsonify({"success": True, "results": results})

@app.route("/api/batch-update", methods=["POST"])
def api_batch_update():
    data = request.json
    names = data.get("names", [])
    source = data.get("source", 0)
    all_mods, _ = load_data_from_source(MODLIST_SOURCES, source)
    installed = get_installed_mods()
    results = []

def do_update(name):
    mod = next((m for m in all_mods if m.get("Name") == name), None)
    if not mod:
        return f"❌ {name}: 未找到"
    uninstall_mod(name)
    with install_lock:
        installed.pop(name, None)
    ok, msg, dep_results = install_mod_with_deps(mod, all_mods, installed)
    with install_lock:
        if ok:
            installed[mod.get("Name")] = msg
    parts = []
    if dep_results:
        parts.extend(dep_results)
    parts.append(f"✅ {name}: 更新成功 -> v{msg}" if ok else f"❌ {name}: {msg}")
    return "\n".join(parts)

    return jsonify({"success": True, "results": results})

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
    if not mod:
        return jsonify({"error": "Not found"}), 404
    uninstall_mod(name)
    installed = get_installed_mods()
    ok, msg, dep_results = install_mod_with_deps(mod, mods, installed)
    if ok:
        return jsonify({"success": True, "new_version": msg, "dep_results": dep_results})
    return jsonify({"error": msg}), 500

# ===== 模组包 =====
@app.route("/api/install-modpack", methods=["POST"])
def api_install_modpack():
    data = request.json
    txt_url = data.get("Link")
    source = data.get("source", 0)
    if not txt_url:
        return jsonify({"error": "No link"}), 500
    try:
        r = requests.get(txt_url, timeout=30)
        if r.status_code != 200:
            return jsonify({"error": "Failed to download list"}), 500
        files = [l.strip() for l in r.text.splitlines() if l.strip() and not l.startswith("#")]
    except requests.RequestException as e:
        return jsonify({"error": str(e)}), 500
    all_mods, _ = load_data_from_source(MODLIST_SOURCES, source)
    installed = get_installed_mods()
    results = batch_install_mods(files, all_mods, installed, source)
    return jsonify({"success": True, "results": results})

@app.route("/api/import-modpack", methods=["POST"])
def api_import_modpack():
    if 'file' not in request.files:
        return jsonify({"error": "No file part"}), 400
    file = request.files['file']
    if file.filename == '':
        return jsonify({"error": "No selected file"}), 400
    try:
        content = file.read().decode('utf-8')
        files = [l.strip() for l in content.splitlines() if l.strip() and not l.startswith("#")]
        source = request.form.get('source', 0, type=int)
        all_mods, _ = load_data_from_source(MODLIST_SOURCES, source)
        installed = get_installed_mods()
        results = batch_install_mods(files, all_mods, installed, source)
        return jsonify({"success": True, "results": results})
    except (UnicodeDecodeError, OSError) as e:
        return jsonify({"error": str(e)}), 500

@app.route("/api/export-modpack")
def api_export_modpack():
    try:
        installed = get_installed_mods()
        if not installed:
            return jsonify({"error": "No installed mods found"}), 404
        filenames = []
        for mod_name in installed:
            manifest = VERSIONS_PATH / f"{mod_name}_manifest.json"
            if manifest.exists():
                try:
                    files = json.loads(manifest.read_text(encoding="utf-8"))
                    for f in files:
                        p = Path(f)
                        if p.suffix.lower() == '.dll' and MODS_PATH.resolve() in p.resolve().parents:
                            filenames.append(p.name)
                            break
                except (OSError, json.JSONDecodeError):
                    pass
        if not filenames:
            return jsonify({"error": "No DLL files found in installed mods"}), 404
        content = "\n".join(filenames)
        return send_file(io.BytesIO(content.encode('utf-8')), mimetype='text/plain', as_attachment=True, download_name='my_modpack.txt')
    except OSError as e:
        return jsonify({"error": str(e)}), 500

# ===== 更新检查 =====
@app.route("/api/check-update")
def api_check_update():
    cfg = load_config()
    use_proxy = cfg.get("use_proxy", False)
    try:
        url = f"https://api.github.com/repos/{GITHUB_REPO}/releases/latest"
        headers = {"Accept": "application/vnd.github.v3+json", "User-Agent": "TLD-Installer"}
        try:
            resp = requests.get(url, headers=headers, timeout=10)
        except requests.RequestException:
            resp = requests.get(url, headers=headers, timeout=10, verify=False)

        if resp.status_code == 200:
            data = resp.json()
            latest_tag = data.get("tag_name", "")
            html_url = data.get("html_url", "")
            download_url = ""
            for asset in data.get("assets", []):
                if asset.get("name", "").endswith(".exe"):
                    download_url = asset.get("browser_download_url", "")
                    break
            if use_proxy and download_url:
                download_url = f"https://ghproxy.net/{download_url}"
            if parse_version(latest_tag) > parse_version(CURRENT_VERSION):
                return jsonify({"update_available": True, "current": CURRENT_VERSION, "latest": latest_tag, "url": html_url, "download_url": download_url})
            return jsonify({"update_available": False})
        return jsonify({"update_available": False})
    except requests.RequestException as e:
        logger.warning(f"检查更新失败: {e}")
        return jsonify({"update_available": False})

@app.route("/api/do-update", methods=["POST"])
def api_do_update():
    data = request.json
    download_url = data.get("url")
    backup_name = data.get("backup_name", "ModpackManager_old.exe")
    if not download_url:
        return jsonify({"error": "No download URL"}), 400

    temp_dir = tempfile.gettempdir()
    new_exe_path = os.path.join(temp_dir, "ModpackManager_new.exe")
    download_success = False
    error_msg = ""
    try:
        with requests.get(download_url, stream=True, timeout=60) as r:
            r.raise_for_status()
            with open(new_exe_path, 'wb') as f:
                for chunk in r.iter_content(8192):
                    f.write(chunk)
        download_success = True
    except requests.RequestException as e:
        try:
            with requests.get(download_url, stream=True, timeout=60, verify=False) as r:
                r.raise_for_status()
                with open(new_exe_path, 'wb') as f:
                    for chunk in r.iter_content(8192):
                        f.write(chunk)
            download_success = True
        except requests.RequestException as e2:
            error_msg = str(e2)

    if not download_success:
        return jsonify({"error": f"下载失败: {error_msg}"}), 500

    try:
        current_exe = sys.executable
        current_dir = os.path.dirname(current_exe)
        new_exe_final = os.path.join(current_dir, "ModpackManager.exe")
        old_exe_final = os.path.join(current_dir, backup_name)
        bat_content = f'''@echo off
chcp 65001 >nul
title TLD Mod Installer - Updater
cls
echo ==========================================
echo        TLD Mod Installer Update
echo ==========================================
echo.
echo  [1/3] Download complete.
echo  [2/3] Waiting for program to exit...
:wait_process
tasklist /fi "pid eq {os.getpid()}" 2>NUL | find "{os.getpid()}" >NUL
if "%ERRORLEVEL%"=="0" (
    timeout /t 1 /nobreak >nul
    goto wait_process
)
echo  Program closed. Replacing files...
if exist "{old_exe_final}" del /f /q "{old_exe_final}" 2>nul
:retry_rename
rename "{current_exe}" "{backup_name}" 2>nul
if exist "{current_exe}" (
    timeout /t 1 /nobreak >nul
    goto retry_rename
)
move /y "{new_exe_path}" "{new_exe_final}" 2>nul
if not exist "{new_exe_final}" (
    echo  ERROR: Failed to install!
    pause
    exit /b 1
)
echo  [3/3] Starting new version...
timeout /t 2 /nobreak >nul
cd /d "{current_dir}"
start "" "{new_exe_final}"
timeout /t 5 /nobreak >nul
exit
'''
        bat_path = os.path.join(temp_dir, "update_tld.bat")
        with open(bat_path, "w", encoding="gbk") as f:
            f.write(bat_content)
        subprocess.Popen(['cmd', '/c', bat_path], creationflags=subprocess.CREATE_NEW_CONSOLE)

        def shutdown_self():
            time.sleep(0.5)
            logger.info("正在关闭主程序以完成更新...")
            os._exit(0)
        threading.Thread(target=shutdown_self, daemon=True).start()
        return jsonify({"success": True, "message": "Update started."})
    except (OSError, subprocess.SubprocessError) as e:
        logger.error(f"更新失败: {e}")
        return jsonify({"error": str(e)}), 500

# ===== 配置/启动 =====
@app.route("/api/config", methods=["GET", "POST"])
def api_config():
    if request.method == "POST":
        save_config(request.json)
        return jsonify({"success": True})
    return jsonify(load_config())

@app.route("/api/launch-exe", methods=["POST"])
def launch_exe():
    path = Path(request.json.get("path", ""))
    if not path.exists():
        return jsonify({"error": "File not found"}), 404
    try:
        subprocess.Popen([str(path)], cwd=path.parent)
        return jsonify({"success": True})
    except (OSError, subprocess.SubprocessError) as e:
        return jsonify({"error": str(e)}), 500

@app.route("/api/install-patcher", methods=["POST"])
def install_patcher():
    p = BASE_DIR / "TLDPatcher" / "TLDPatcher.exe"
    if not p.exists():
        return jsonify({"error": "TLDPatcher not found"}), 404
    try:
        subprocess.Popen([str(p)], cwd=p.parent)
        return jsonify({"success": True})
    except (OSError, subprocess.SubprocessError) as e:
        return jsonify({"error": str(e)}), 500

@app.route("/api/browse-exe", methods=["POST"])
def browse_exe():
    if sys.platform != 'win32':
        return jsonify({"error": "Only Windows supported"}), 400
    ps_script = """
    Add-Type -AssemblyName System.Windows.Forms
    $FileBrowser = New-Object System.Windows.Forms.OpenFileDialog -Property @{
        Filter = 'Executable Files (*.exe)|*.exe'; Title = 'Select Game EXE'; RestoreDirectory = $true
    }
    if ($FileBrowser.ShowDialog() -eq 'OK') { $FileBrowser.FileName }
    """
    try:
        result = subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps_script],
            capture_output=True, text=True, timeout=120,
            creationflags=subprocess.CREATE_NO_WINDOW
        )
        path = result.stdout.strip()
        if path and os.path.exists(path):
            return jsonify({"success": True, "path": path})
        return jsonify({"error": "No file selected"}), 400
    except subprocess.TimeoutExpired:
        return jsonify({"error": "Dialog timed out"}), 408
    except (OSError, subprocess.SubprocessError) as e:
        return jsonify({"error": str(e)}), 500

@app.route("/api/license")
def get_license():
    try:
        f = BASE_DIR / "LICENSE_AND_NOTICE.md"
        return f.read_text(encoding="utf-8") if f.exists() else "Not found", 200
    except OSError:
        return "Error", 500

@app.route("/api/translations")
def api_translations():
    return jsonify(load_translations(request.args.get("lang", "zh")))

# ==================== 启动 ====================
if __name__ == "__main__":
    import webbrowser
    threading.Thread(target=lambda: (time.sleep(1), webbrowser.open("http://127.0.0.1:5000")), daemon=True).start()
    app.run(debug=False, threaded=True)