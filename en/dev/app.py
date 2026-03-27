import os
import json
import shutil
import tempfile
import zipfile
import time
import threading
import logging
from pathlib import Path
from urllib.parse import urlparse

import requests
from flask import Flask, jsonify, render_template, request, send_from_directory

# 配置日志
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

app = Flask(__name__)

# ==================== 配置 ====================
DOCUMENTS_PATH = Path.home() / "Documents"
GAME_PATH = DOCUMENTS_PATH / "TheLongDrive"
MODS_PATH = GAME_PATH / "Mods"
VERSIONS_PATH = MODS_PATH / "temp" / "Versions"

MODS_PATH.mkdir(parents=True, exist_ok=True)
VERSIONS_PATH.mkdir(parents=True, exist_ok=True)

# 模组列表源配置（极狐源已改为 GitLab 官方链接，并设为第一优先级）
# 模组列表源配置（官方源设为第一优先级）
# Mod list sources configuration (Official source as first priority)
MODLIST_SOURCES = [
    {
        "name": "Official Source (GitLab)",
        "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/modlist_3.json"
    },
    {
        "name": "Jihulab Mirror (GitLab China)",
        "url": "https://gitlab.com/MFSDev-NET/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/modlist_3.json"
    },
    {
        "name": "GitHub Mirror China",
        "url": "https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/raw/refs/heads/main/modlist_3.json"
    },
    {
        "name": "Local Source",
        "url": None,
        "local_path": Path(__file__).parent / "modlist_3.json"
    }
]

# Modpack sources configuration (Official source as first priority)
MODPACK_SOURCES = [
    {
        "name": "Official Source (GitLab)",
        "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"
    },
    {
        "name": "Jihulab Mirror (GitLab China)",
        "url": "https://gitlab.com/MFSDev-NET/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"
    },
    {
        "name": "GitHub Mirror China",
        "url": "https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/raw/refs/heads/main/Modpacks/modlist_3.json"
    },
    {
        "name": "Local Source",
        "url": None,
        "local_path": Path(__file__).parent / "Modpacks" / "modlist_3.json"
    }
]

# 下载任务队列（保留所有任务，包括已完成）
download_tasks = []
task_counter = 0
task_lock = threading.Lock()

# ==================== 文件收集工具 ====================
def collect_all_files(directory):
    file_list = []
    for root, dirs, files in os.walk(directory):
        for file in files:
            file_list.append(os.path.join(root, file))
    return file_list

# ==================== 网络请求 ====================
def fetch_with_retry(url, max_retries=5, timeout=10):
    for attempt in range(max_retries):
        try:
            response = requests.get(url, timeout=timeout)
            if response.status_code == 200:
                return response
            else:
                logger.warning(f"请求返回状态码 {response.status_code} (尝试 {attempt + 1}/{max_retries})")
        except requests.exceptions.Timeout:
            logger.warning(f"请求超时 (尝试 {attempt + 1}/{max_retries})")
        except requests.exceptions.ConnectionError:
            logger.warning(f"连接失败 (尝试 {attempt + 1}/{max_retries})")
        except Exception as e:
            logger.warning(f"请求异常: {e} (尝试 {attempt + 1}/{max_retries})")
        
        if attempt < max_retries - 1:
            time.sleep(1)
    return None

# ==================== 模组列表加载 ====================
def load_modlist(source_index=0):
    for i in range(source_index, len(MODLIST_SOURCES)):
        source = MODLIST_SOURCES[i]
        try:
            if source.get("url"):
                logger.info(f"正在尝试从 {source['name']} 加载...")
                response = fetch_with_retry(source["url"])
                if response:
                    data = response.json()
                    mods = data.get("Mods", [])
                    if mods:
                        logger.info(f"从 {source['name']} 加载 {len(mods)} 个模组")
                        return mods, i
                    else:
                        logger.warning(f"{source['name']} 返回的模组列表为空")
                else:
                    logger.warning(f"{source['name']} 请求失败，尝试下一个源")
            else:
                local_path = source.get("local_path")
                if local_path and local_path.exists():
                    with open(local_path, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    mods = data.get("Mods", [])
                    if mods:
                        logger.info(f"从 {source['name']} 加载 {len(mods)} 个模组")
                        return mods, i
                    else:
                        logger.warning(f"{source['name']} 文件存在但模组列表为空")
                else:
                    logger.warning(f"{source['name']} 本地文件不存在")
        except Exception as e:
            logger.error(f"{source['name']} 加载异常: {e}")
            continue
    logger.error("所有源均加载失败，返回空列表")
    return [], 0

def load_modpacks(source_index=0):
    for i in range(source_index, len(MODPACK_SOURCES)):
        source = MODPACK_SOURCES[i]
        try:
            if source.get("url"):
                logger.info(f"正在尝试从 {source['name']} 加载模组包...")
                response = fetch_with_retry(source["url"])
                if response:
                    data = response.json()
                    modpacks = data.get("Mods", [])
                    if modpacks:
                        logger.info(f"从 {source['name']} 加载 {len(modpacks)} 个模组包")
                        return modpacks, i
                    else:
                        logger.warning(f"{source['name']} 返回的模组包列表为空")
                else:
                    logger.warning(f"{source['name']} 请求失败，尝试下一个源")
            else:
                local_path = source.get("local_path")
                if local_path and local_path.exists():
                    with open(local_path, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    modpacks = data.get("Mods", [])
                    if modpacks:
                        logger.info(f"从 {source['name']} 加载 {len(modpacks)} 个模组包")
                        return modpacks, i
                    else:
                        logger.warning(f"{source['name']} 文件存在但模组包列表为空")
                else:
                    logger.warning(f"{source['name']} 本地文件不存在")
        except Exception as e:
            logger.error(f"{source['name']} 加载异常: {e}")
            continue
    logger.error("所有模组包源均加载失败，返回空列表")
    return [], 0

# ==================== 安装记录管理 ====================
def get_installed_mods():
    installed = {}
    if VERSIONS_PATH.exists():
        for file in VERSIONS_PATH.glob("*.txt"):
            if file.name.endswith("_manifest.json"):
                continue
            name = file.stem
            version = file.read_text(encoding="utf-8").strip()
            installed[name] = version
    return installed

def set_installed_version(mod_name, version):
    version_file = VERSIONS_PATH / f"{mod_name}.txt"
    version_file.write_text(version, encoding="utf-8")

def remove_installed_version(mod_name):
    version_file = VERSIONS_PATH / f"{mod_name}.txt"
    if version_file.exists():
        version_file.unlink()

# ==================== 依赖处理 ====================
def extract_dependency_mods(dependency_str):
    if not dependency_str or dependency_str.strip() == "":
        return []
    if "http" in dependency_str:
        parsed = urlparse(dependency_str)
        filename = os.path.basename(parsed.path)
        if filename:
            return [filename]
    if dependency_str.endswith(".dll"):
        return [dependency_str]
    return []

def find_mod_by_filename(filename):
    mods, _ = load_modlist()
    for mod in mods:
        if mod.get("FileName") == filename:
            return mod
    return None

# ==================== 下载管理 ====================
def add_download_task(filename, mod_name):
    global task_counter
    with task_lock:
        task_id = task_counter
        task_counter += 1
        download_tasks.append({
            "id": task_id,
            "filename": filename,
            "name": mod_name,
            "status": "pending",
            "progress": 0,
            "speed": 0,
            "total_size": 0,
            "downloaded": 0,
            "error": None,
            "start_time": None
        })
    return task_id

def update_task(task_id, **kwargs):
    with task_lock:
        for task in download_tasks:
            if task["id"] == task_id:
                task.update(kwargs)
                break

def download_file_with_progress(url, dest_path, task_id):
    try:
        response = requests.get(url, stream=True, timeout=30)
        response.raise_for_status()
        total_size = int(response.headers.get('content-length', 0))
        update_task(task_id, total_size=total_size, status="downloading", start_time=time.time())

        downloaded = 0
        start_time = time.time()
        with open(dest_path, "wb") as f:
            for chunk in response.iter_content(chunk_size=8192):
                if chunk:
                    f.write(chunk)
                    downloaded += len(chunk)
                    update_task(task_id, downloaded=downloaded)
                    progress = (downloaded / total_size * 100) if total_size > 0 else 0
                    elapsed = time.time() - start_time
                    speed = downloaded / elapsed if elapsed > 0 else 0
                    update_task(task_id, progress=progress, speed=speed)

        update_task(task_id, status="completed", progress=100)
        return True
    except Exception as e:
        update_task(task_id, status="failed", error=str(e))
        return False

def download_file(url, dest_path):
    try:
        response = requests.get(url, stream=True, timeout=30)
        response.raise_for_status()
        with open(dest_path, "wb") as f:
            for chunk in response.iter_content(chunk_size=8192):
                f.write(chunk)
        return True
    except Exception as e:
        logger.error(f"下载失败 {url}: {e}")
        return False

# ==================== 模组安装 ====================
def install_mod(mod, installed_records):
    filename = mod["FileName"]
    link = mod["Link"]
    mod_name = mod.get("Name", filename)

    if mod_name in installed_records:
        return False, f"模组 {mod_name} 已安装"

    task_id = add_download_task(filename, mod_name)

    with tempfile.NamedTemporaryFile(delete=False) as tmp:
        tmp_path = tmp.name

    try:
        if not download_file_with_progress(link, tmp_path, task_id):
            update_task(task_id, status="failed")
            return False, f"下载失败: {link}"

        installed_files = []
        extract_temp = Path(tempfile.mkdtemp())

        if filename.lower().endswith(('.zip', '.dll')):
            try:
                with zipfile.ZipFile(tmp_path, "r") as zip_ref:
                    zip_ref.extractall(extract_temp)
                is_zip = True
            except (zipfile.BadZipFile, NotImplementedError):
                is_zip = False
                shutil.copy2(tmp_path, extract_temp / filename)

            # 记录所有将要安装的文件
            file_records = []
            for item in extract_temp.iterdir():
                if item.is_dir():
                    for root, dirs, files in os.walk(item):
                        for file in files:
                            src_file = os.path.join(root, file)
                            rel_path = os.path.relpath(src_file, extract_temp)
                            target_file = MODS_PATH / rel_path
                            file_records.append(str(target_file))
                else:
                    target_file = MODS_PATH / item.name
                    file_records.append(str(target_file))

            # 移动文件
            for item in extract_temp.iterdir():
                target = MODS_PATH / item.name
                if item.is_dir():
                    if target.exists():
                        shutil.rmtree(target)
                    shutil.copytree(item, target)
                else:
                    shutil.copy2(item, target)
            installed_files = file_records
        else:
            return False, f"不支持的文件类型: {filename}"

        shutil.rmtree(extract_temp, ignore_errors=True)

        # 保存 manifest 文件
        manifest_path = VERSIONS_PATH / f"{mod_name}_manifest.json"
        manifest_path.write_text(json.dumps(installed_files, ensure_ascii=False), encoding="utf-8")

        set_installed_version(mod_name, mod.get("Version", "0"))
        update_task(task_id, status="completed", progress=100)

        return True, installed_files
    except Exception as e:
        update_task(task_id, status="failed", error=str(e))
        return False, f"安装异常: {str(e)}"
    finally:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        if 'extract_temp' in locals() and os.path.exists(extract_temp):
            shutil.rmtree(extract_temp, ignore_errors=True)

def install_with_deps(mod, installed_records):
    filename = mod["FileName"]
    mod_name = mod.get("Name", filename)

    if mod_name in installed_records:
        return [filename], None

    deps = extract_dependency_mods(mod.get("Dependency", ""))
    for dep_filename in deps:
        dep_mod = find_mod_by_filename(dep_filename)
        if not dep_mod:
            return None, f"依赖模组 {dep_filename} 不存在于列表中"
        dep_installed, dep_err = install_with_deps(dep_mod, installed_records)
        if dep_err:
            return None, f"依赖安装失败: {dep_err}"

    ok, result = install_mod(mod, installed_records)
    if ok:
        installed_records[mod_name] = mod.get("Version", "0")
        return [filename], None
    else:
        return None, result

# ==================== 模组包安装 ====================
def install_modpack(modpack):
    """安装模组包 - 为每个模组创建独立下载任务"""
    txt_url = modpack.get("Link")
    if not txt_url:
        return False, "模组包没有指定模组列表文件"

    with tempfile.NamedTemporaryFile(delete=False, suffix=".txt") as tmp:
        tmp_path = tmp.name

    try:
        if not download_file(txt_url, tmp_path):
            return False, "下载模组列表失败"

        with open(tmp_path, "r", encoding="utf-8") as f:
            mod_filenames = [line.strip() for line in f if line.strip() and not line.startswith("#")]

        installed = get_installed_mods()
        installed_copy = installed.copy()
        results = []
        
        # 为每个模组创建独立任务
        for fn in mod_filenames:
            mod = find_mod_by_filename(fn)
            if not mod:
                results.append(f"❌ 模组 {fn} 不存在于列表中")
                continue
            mod_name = mod.get("Name", fn)
            if mod_name in installed_copy:
                results.append(f"⏭️ 模组 {mod_name} 已安装，跳过")
                continue
            
            # 单独安装每个模组（会创建独立下载任务）
            _, err = install_with_deps(mod, installed_copy)
            if err:
                results.append(f"❌ 安装 {mod_name} 失败: {err}")
            else:
                installed_copy[mod_name] = mod.get("Version", "0")
                results.append(f"✅ 安装 {mod_name} 成功")

        return True, results
    except Exception as e:
        return False, f"模组包安装异常: {e}"
    finally:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)

# ==================== 模组卸载（仅删除 .dll 文件） ====================
def delete_empty_folders(path):
    """递归删除空文件夹"""
    if not path.exists():
        return
    for item in path.iterdir():
        if item.is_dir():
            delete_empty_folders(item)
    try:
        if path.is_dir() and not any(path.iterdir()):
            path.rmdir()
            logger.info(f"删除空文件夹: {path}")
    except Exception:
        pass

def uninstall_mod(mod_name):
    """卸载模组：只删除 .dll 文件，保留其他文件"""
    manifest_path = VERSIONS_PATH / f"{mod_name}_manifest.json"
    dll_deleted = False

    if manifest_path.exists():
        try:
            files_to_delete = json.loads(manifest_path.read_text(encoding="utf-8"))
            for file_path in files_to_delete:
                path = Path(file_path)
                if path.exists() and path.is_file() and path.suffix.lower() == '.dll':
                    path.unlink()
                    dll_deleted = True
                    logger.debug(f"删除 DLL 文件: {path}")
            # 清理空文件夹（仅针对 DLL 所在路径）
            for file_path in files_to_delete:
                path = Path(file_path)
                if path.suffix.lower() == '.dll':
                    parent = path.parent
                    delete_empty_folders(parent)
            manifest_path.unlink()
        except Exception as e:
            return False, f"卸载时清理文件失败: {e}"

    version_file = VERSIONS_PATH / f"{mod_name}.txt"
    if version_file.exists():
        version_file.unlink()

    if not dll_deleted:
        # 如果没有找到 DLL 文件，可能模组未安装或已经手动删除，视为成功
        return True, "未找到对应的 DLL 文件（可能已被删除）"
    return True, "卸载成功（仅删除 DLL 文件）"

# ==================== API 路由 ====================
@app.route("/")
def index():
    return render_template("index.html")

@app.route("/static/<path:filename>")
def serve_static(filename):
    return send_from_directory("static", filename)

@app.route("/api/sources")
def get_sources():
    return jsonify([{"name": s["name"], "index": i} for i, s in enumerate(MODLIST_SOURCES)])

@app.route("/api/mods", methods=["GET"])
def get_mods():
    source_index = request.args.get("source", 0, type=int)
    mods, active_index = load_modlist(source_index)
    installed = get_installed_mods()
    for mod in mods:
        mod_name = mod.get("Name", mod["FileName"])
        mod["is_installed"] = mod_name in installed
        mod["installed_version"] = installed.get(mod_name, "")
    return jsonify({"mods": mods, "active_source": active_index})

@app.route("/api/modpacks", methods=["GET"])
def get_modpacks_route():
    source_index = request.args.get("source", 0, type=int)
    modpacks, active_index = load_modpacks(source_index)
    return jsonify({"modpacks": modpacks, "active_source": active_index})

@app.route("/api/tasks")
def get_tasks():
    with task_lock:
        # 返回所有任务（包括已完成和失败）
        return jsonify(download_tasks.copy())

@app.route("/api/install", methods=["POST"])
def install():
    data = request.get_json()
    filename = data.get("filename")
    if not filename:
        return jsonify({"error": "缺少文件名"}), 400

    mod = find_mod_by_filename(filename)
    if not mod:
        return jsonify({"error": "模组不存在"}), 404

    installed = get_installed_mods()
    mod_name = mod.get("Name", filename)
    if mod_name in installed:
        return jsonify({"error": "模组已安装"}), 400

    installed_copy = installed.copy()
    result, err = install_with_deps(mod, installed_copy)
    if err:
        return jsonify({"error": err}), 500
    return jsonify({"success": True, "installed": result})

@app.route("/api/uninstall", methods=["POST"])
def uninstall():
    data = request.get_json()
    mod_name = data.get("name")
    if not mod_name:
        return jsonify({"error": "缺少模组名"}), 400

    ok, msg = uninstall_mod(mod_name)
    if not ok:
        return jsonify({"error": msg}), 400
    return jsonify({"success": True, "message": msg})

@app.route("/api/update", methods=["POST"])
def update():
    data = request.get_json()
    mod_name = data.get("name")
    if not mod_name:
        return jsonify({"error": "缺少模组名"}), 400

    mods, _ = load_modlist()
    target_mod = None
    for mod in mods:
        if mod.get("Name") == mod_name:
            target_mod = mod
            break
    if not target_mod:
        return jsonify({"error": "模组不存在"}), 404

    ok, msg = uninstall_mod(mod_name)
    if not ok:
        return jsonify({"error": f"卸载失败: {msg}"}), 500

    installed = get_installed_mods()
    installed_copy = installed.copy()
    result, err = install_with_deps(target_mod, installed_copy)
    if err:
        return jsonify({"error": f"更新失败: {err}"}), 500
    return jsonify({"success": True, "message": "更新成功"})

@app.route("/api/install-modpack", methods=["POST"])
def install_modpack_route():
    data = request.get_json()
    if not data:
        return jsonify({"error": "缺少模组包数据"}), 400

    ok, result = install_modpack(data)
    if not ok:
        return jsonify({"error": result}), 500
    return jsonify({"success": True, "results": result})

@app.errorhandler(Exception)
def handle_exception(e):
    logger.exception("未捕获的异常")
    return jsonify({"error": "服务器内部错误"}), 500

if __name__ == "__main__":
    app.run(debug=True, threaded=True)