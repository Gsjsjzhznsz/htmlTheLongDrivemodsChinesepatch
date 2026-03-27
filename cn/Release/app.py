import os
import json
import shutil
import tempfile
import zipfile
import time
import threading
from pathlib import Path
from urllib.parse import urlparse

import requests
from flask import Flask, jsonify, render_template, request, send_from_directory

app = Flask(__name__)

# ==================== 配置 ====================
DOCUMENTS_PATH = Path.home() / "Documents"
GAME_PATH = DOCUMENTS_PATH / "TheLongDrive"
MODS_PATH = GAME_PATH / "Mods"
VERSIONS_PATH = MODS_PATH / "temp" / "Versions"

# 模组列表源配置（按优先级排序）
MODLIST_SOURCES = [
    {
        "name": "极狐源 (Jihulab)",
        "url": "https://jihulab.com/MFSDev-NET/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/modlist_3.json"
    },
    {
        "name": "GitHub 源",
        "url": "https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/raw/refs/heads/main/modlist_3.json"
    },
    {
        "name": "本地源 (Local)",
        "url": None,
        "local_path": Path(__file__).parent / "modlist_3.json"
    },
    {
        "name": "官方源 (GitLab)",
        "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/modlist_3.json"
    }
]

MODPACK_SOURCES = [
    {
        "name": "极狐源 (Jihulab)",
        "url": "https://jihulab.com/MFSDev-NET/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"
    },
    {
        "name": "GitHub 源",
        "url": "https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/raw/refs/heads/main/Modpacks/modlist_3.json"
    },
    {
        "name": "本地源 (Local)",
        "url": None,
        "local_path": Path(__file__).parent / "Modpacks" / "modlist_3.json"
    },
    {
        "name": "官方源 (GitLab)",
        "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"
    }
]

# 下载任务队列
download_tasks = []
task_counter = 0
task_lock = threading.Lock()

# 确保目录存在
MODS_PATH.mkdir(parents=True, exist_ok=True)
VERSIONS_PATH.mkdir(parents=True, exist_ok=True)
Path(__file__).parent / "Modpacks" / "modlist_3.json"


# ==================== 文件收集工具 ====================
def collect_all_files(directory):
    """递归收集目录下所有文件路径"""
    file_list = []
    for root, dirs, files in os.walk(directory):
        for file in files:
            file_list.append(os.path.join(root, file))
    return file_list


# ==================== 模组列表加载 ====================

def fetch_with_retry(url, max_retries=5, timeout=10):
    """带重试的请求，失败返回 None"""
    for attempt in range(max_retries):
        try:
            response = requests.get(url, timeout=timeout)
            if response.status_code == 200:
                return response
            else:
                print(f"请求返回状态码 {response.status_code} (尝试 {attempt + 1}/{max_retries})")
        except requests.exceptions.Timeout:
            print(f"请求超时 (尝试 {attempt + 1}/{max_retries})")
        except requests.exceptions.ConnectionError:
            print(f"连接失败 (尝试 {attempt + 1}/{max_retries})")
        except Exception as e:
            print(f"请求异常: {e} (尝试 {attempt + 1}/{max_retries})")
        
        if attempt < max_retries - 1:
            time.sleep(1)  # 等待1秒后重试
    return None

def load_modlist(source_index=0):
    """从指定源加载模组列表，失败则回退到下一个源"""
    for i in range(source_index, len(MODLIST_SOURCES)):
        source = MODLIST_SOURCES[i]
        try:
            if source.get("url"):
                print(f"正在尝试从 {source['name']} 加载...")
                response = fetch_with_retry(source["url"], max_retries=5, timeout=10)
                if response:
                    data = response.json()
                    mods = data.get("Mods", [])
                    if mods:
                        print(f"从 {source['name']} 加载 {len(mods)} 个模组")
                        return mods, i
                    else:
                        print(f"{source['name']} 返回的模组列表为空")
                else:
                    print(f"{source['name']} 请求失败，尝试下一个源")
            else:
                # 本地文件
                local_path = source.get("local_path")
                if local_path and local_path.exists():
                    with open(local_path, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    mods = data.get("Mods", [])
                    if mods:
                        print(f"从 {source['name']} 加载 {len(mods)} 个模组")
                        return mods, i
                    else:
                        print(f"{source['name']} 文件存在但模组列表为空")
                else:
                    print(f"{source['name']} 本地文件不存在")
        except Exception as e:
            print(f"{source['name']} 加载异常: {e}")
            continue
    print("所有源均加载失败，返回空列表")
    return [], 0

def load_modpacks(source_index=0):
    """从指定源加载模组包列表"""
    for i in range(source_index, len(MODPACK_SOURCES)):
        source = MODPACK_SOURCES[i]
        try:
            if source.get("url"):
                print(f"正在尝试从 {source['name']} 加载模组包...")
                response = fetch_with_retry(source["url"], max_retries=5, timeout=10)
                if response:
                    data = response.json()
                    modpacks = data.get("Mods", [])
                    if modpacks:
                        print(f"从 {source['name']} 加载 {len(modpacks)} 个模组包")
                        return modpacks, i
                    else:
                        print(f"{source['name']} 返回的模组包列表为空")
                else:
                    print(f"{source['name']} 请求失败，尝试下一个源")
            else:
                # 本地文件
                local_path = source.get("local_path")
                if local_path and local_path.exists():
                    with open(local_path, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    modpacks = data.get("Mods", [])
                    if modpacks:
                        print(f"从 {source['name']} 加载 {len(modpacks)} 个模组包")
                        return modpacks, i
                    else:
                        print(f"{source['name']} 文件存在但模组包列表为空")
                else:
                    print(f"{source['name']} 本地文件不存在")
        except Exception as e:
            print(f"{source['name']} 加载异常: {e}")
            continue
    print("所有模组包源均加载失败，返回空列表")
    return [], 0


# ==================== 安装记录管理 ====================
def get_installed_mods():
    """读取已安装模组信息（基于版本文件）"""
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
    """保存模组版本信息"""
    version_file = VERSIONS_PATH / f"{mod_name}.txt"
    version_file.write_text(version, encoding="utf-8")


def remove_installed_version(mod_name):
    """删除模组版本文件"""
    version_file = VERSIONS_PATH / f"{mod_name}.txt"
    if version_file.exists():
        version_file.unlink()


# ==================== 依赖处理 ====================
def extract_dependency_mods(dependency_str):
    """从Dependency字段提取需要安装的模组文件名列表"""
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
    """通过文件名查找模组条目"""
    mods, _ = load_modlist()
    for mod in mods:
        if mod.get("FileName") == filename:
            return mod
    return None


# ==================== 下载管理 ====================
def add_download_task(filename, mod_name):
    """添加下载任务到队列"""
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
            "error": None
        })
    return task_id


def update_task(task_id, **kwargs):
    """更新下载任务状态"""
    with task_lock:
        for task in download_tasks:
            if task["id"] == task_id:
                task.update(kwargs)
                break


def download_file_with_progress(url, dest_path, task_id):
    """带进度回调和速度显示的下载"""
    try:
        response = requests.get(url, stream=True, timeout=30)
        response.raise_for_status()
        total_size = int(response.headers.get('content-length', 0))
        update_task(task_id, total_size=total_size, status="downloading")

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
    """简单下载（不带进度）"""
    try:
        response = requests.get(url, stream=True, timeout=30)
        response.raise_for_status()
        with open(dest_path, "wb") as f:
            for chunk in response.iter_content(chunk_size=8192):
                f.write(chunk)
        return True
    except Exception as e:
        print(f"下载失败 {url}: {e}")
        return False


# ==================== 模组安装 ====================
def install_mod(mod, installed_records):
    """
    安装单个模组，返回 (成功与否, 消息或文件列表)
    installed_records 是已安装记录的字典（会被修改）
    """
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

            # 记录所有将要安装的文件（先记录路径，再移动）
            file_records = []
            for item in extract_temp.iterdir():
                target = MODS_PATH / item.name
                if item.is_dir():
                    # 遍历子目录，记录所有文件
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

        # 保存 manifest 文件（记录所有安装的文件路径）
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


def install_with_deps(mod, installed_records):
    """递归安装模组及其依赖"""
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
    """安装模组包"""
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
        for fn in mod_filenames:
            mod = find_mod_by_filename(fn)
            if not mod:
                results.append(f"❌ 模组 {fn} 不存在于列表中")
                continue
            mod_name = mod.get("Name", fn)
            if mod_name in installed_copy:
                results.append(f"⏭️ 模组 {mod_name} 已安装，跳过")
                continue
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


# ==================== 模组卸载 ====================
def uninstall_mod(mod_name):
    """卸载模组，只删除 manifest 中记录的文件"""
    manifest_path = VERSIONS_PATH / f"{mod_name}_manifest.json"
    if manifest_path.exists():
        try:
            files_to_delete = json.loads(manifest_path.read_text(encoding="utf-8"))
            for file_path in files_to_delete:
                path = Path(file_path)
                if path.exists() and path.is_file():
                    path.unlink()
            # 清理空文件夹
            for file_path in files_to_delete:
                parent = Path(file_path).parent
                try:
                    if parent.exists() and not any(parent.iterdir()):
                        parent.rmdir()
                except Exception:
                    pass
            manifest_path.unlink()
        except Exception as e:
            return False, f"卸载时清理文件失败: {e}"

    version_file = VERSIONS_PATH / f"{mod_name}.txt"
    if version_file.exists():
        version_file.unlink()

    return True, "卸载成功"


# ==================== API 路由 ====================
@app.route("/")
def index():
    return render_template("index.html")


@app.route("/static/<path:filename>")
def serve_static(filename):
    return send_from_directory("static", filename)


@app.route("/api/sources")
def get_sources():
    """返回可用的模组列表源"""
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


if __name__ == "__main__":
    app.run(debug=True, threaded=True)