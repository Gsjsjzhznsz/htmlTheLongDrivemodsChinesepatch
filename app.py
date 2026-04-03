import os
import json
import shutil
import tempfile
import zipfile
import time
import threading
import logging
import subprocess
import queue
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib.parse import urlparse
import sys
import ctypes
import platform

import requests
from flask import Flask, jsonify, render_template, request, send_from_directory

# 路径处理函数
def get_resource_path(relative_path):
    """获取资源文件的绝对路径（支持PyInstaller打包）"""
    try:
        base_path = sys._MEIPASS
    except Exception:
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)

def is_admin():
    """检测是否以管理员权限运行"""
    try:
        if platform.system() == 'Windows':
            return ctypes.windll.shell32.IsUserAnAdmin() != 0
        else:
            return os.geteuid() == 0
    except Exception:
        return False

def get_documents_path():
    """获取用户文档路径，处理管理员权限导致的路径重定向"""
    if platform.system() == 'Windows':
        base_documents = Path.home() / "Documents"
        if is_admin():
            local_appdata = Path(os.environ.get('LOCALAPPDATA', ''))
            if local_appdata:
                redirected = local_appdata / "VirtualStore" / "Users" / os.environ.get('USERNAME', '') / "Documents"
                if redirected.exists():
                    return redirected
        return base_documents
    else:
        return Path.home() / "Documents"

def get_game_path():
    """获取游戏文件夹路径"""
    return get_documents_path() / "TheLongDrive"

def get_favorites_path():
    """获取收藏文件路径"""
    return get_game_path() / "favorites.xml"

def load_favorites():
    """从XML文件加载收藏列表"""
    fav_path = get_favorites_path()
    favorites = []
    if fav_path.exists():
        try:
            tree = ET.parse(fav_path)
            root = tree.getroot()
            for item in root.findall('mod'):
                mod_data = {
                    'Name': item.find('Name').text if item.find('Name') is not None else '',
                    'FileName': item.find('FileName').text if item.find('FileName') is not None else '',
                    'Author': item.find('Author').text if item.find('Author') is not None else '',
                    'Version': item.find('Version').text if item.find('Version') is not None else '',
                    'Description': item.find('Description').text if item.find('Description') is not None else '',
                    'Category': item.find('Category').text if item.find('Category') is not None else '',
                    'PictureLink': item.find('PictureLink').text if item.find('PictureLink') is not None else '',
                    'Link': item.find('Link').text if item.find('Link') is not None else '',
                    'Date': item.find('Date').text if item.find('Date') is not None else '',
                    'Dependency': item.find('Dependency').text if item.find('Dependency') is not None else '',
                    'sourceIndex': int(item.find('sourceIndex').text) if item.find('sourceIndex') is not None and item.find('sourceIndex').text else 0
                }
                favorites.append(mod_data)
            logger.info(f"从 {fav_path} 加载了 {len(favorites)} 个收藏")
        except Exception as e:
            logger.error(f"加载收藏文件失败: {e}")
    return favorites

def save_favorites(favorites):
    """保存收藏列表到XML文件"""
    fav_path = get_favorites_path()
    fav_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        root = ET.Element('favorites')
        for mod in favorites:
            item = ET.SubElement(root, 'mod')
            for key, value in mod.items():
                child = ET.SubElement(item, key)
                child.text = str(value) if value is not None else ''
        tree = ET.ElementTree(root)
        tree.write(fav_path, encoding='utf-8', xml_declaration=True)
        logger.info(f"收藏已保存到 {fav_path}，共 {len(favorites)} 个")
        return True
    except Exception as e:
        logger.error(f"保存收藏文件失败: {e}")
        return False

# 日志配置
def setup_logging():
    """配置日志，同时输出到控制台和文件"""
    if getattr(sys, 'frozen', False):
        log_dir = os.path.dirname(sys.executable)
        log_file = os.path.join(log_dir, 'app.log')
    else:
        log_file = 'app.log'
    
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        handlers=[
            logging.FileHandler(log_file, encoding='utf-8'),
            logging.StreamHandler()
        ]
    )
    return logging.getLogger(__name__)

logger = setup_logging()

# 语言系统
def load_translations(lang_code="zh"):
    """加载翻译文件"""
    try:
        translations_path = Path(get_resource_path("translations")) / f"{lang_code}.json"
        if translations_path.exists():
            with open(translations_path, "r", encoding="utf-8") as f:
                return json.load(f)
        else:
            logger.warning(f"翻译文件不存在: {translations_path}")
            # 返回空字典，使用默认文本
            return {}
    except Exception as e:
        logger.error(f"加载翻译文件失败: {e}")
        return {}

def get_user_language():
    """获取用户语言偏好"""
    # 这里可以从请求中获取语言设置
    # 默认使用中文
    return "zh"

# Flask应用（只定义一次）
app = Flask(__name__,
           template_folder=get_resource_path('templates'),
           static_folder=get_resource_path('static'))

# 基础路径（用于本地文件）
BASE_DIR = Path(get_resource_path("."))

logger.info(f"BASE_DIR: {BASE_DIR}")
logger.info(f"BASE_DIR exists: {BASE_DIR.exists()}")
if (BASE_DIR / "modlist_3.json").exists():
    logger.info("本地 modlist_3.json 存在")
else:
    logger.warning("本地 modlist_3.json 不存在")

# 游戏路径配置（使用新的路径处理函数）
DOCUMENTS_PATH = get_documents_path()
GAME_PATH = get_game_path()
MODS_PATH = GAME_PATH / "Mods"
VERSIONS_PATH = MODS_PATH / "temp" / "Versions"

MODS_PATH.mkdir(parents=True, exist_ok=True)
VERSIONS_PATH.mkdir(parents=True, exist_ok=True)

logger.info(f"用户文档路径: {DOCUMENTS_PATH}")
logger.info(f"游戏路径: {GAME_PATH}")
logger.info(f"管理员模式: {is_admin()}")

# 模组列表源配置（使用 BASE_DIR）
MODLIST_SOURCES = [
    {
        "name": "官方源 (GitLab)",
        "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/modlist_3.json"
    },
    {
        "name": "极狐镜像源 (GitLab中国)",
        "url": "https://gitlab.com/MFSDev-NET/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/modlist_3.json"
    },
    {
        "name": "GitHub镜像源 (中国)",
        "url": "https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/raw/refs/heads/main/modlist_3.json"
    },
    {
        "name": "官方本地源（英文）",
        "url": None,
        "local_path": BASE_DIR / "en-modlist_3.json"
    },
    {
        "name": "官方本地源（中文）",
        "url": None,
        "local_path": BASE_DIR / "modlist_3.json"
    }
]

MODPACK_SOURCES = [
    {
        "name": "官方源 (GitLab)",
        "url": "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"
    },
    {
        "name": "极狐镜像源 (GitLab中国)",
        "url": "https://gitlab.com/MFSDev-NET/workshop-tld-chinese/-/raw/WorkshopDatabase8.6/Modpacks/modlist_3.json"
    },
    {
        "name": "GitHub镜像源 (中国)",
        "url": "https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/raw/refs/heads/main/Modpacks/modlist_3.json"
    },
    {
        "name": "官方本地源",
        "url": None,
        "local_path": BASE_DIR / "Modpacks" / "en-modlist_3.json"
    },
    {
        "name": "本地源",
        "url": None,
        "local_path": BASE_DIR / "Modpacks" / "modlist_3.json"
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
def load_modlist(source_index=0, strict=False):
    """加载模组列表，strict=True时只尝试指定源，失败则返回空列表"""
    if strict:
        # 严格模式：只尝试指定源
        if source_index >= len(MODLIST_SOURCES):
            logger.error(f"严格模式：源索引 {source_index} 超出范围")
            return [], source_index
        source = MODLIST_SOURCES[source_index]
        try:
            if source.get("url"):
                logger.info(f"严格模式：正在尝试从 {source['name']} 加载...")
                response = fetch_with_retry(source["url"])
                if response:
                    data = response.json()
                    mods = data.get("Mods", [])
                    if mods:
                        logger.info(f"严格模式：从 {source['name']} 加载 {len(mods)} 个模组")
                        return mods, source_index
                    else:
                        logger.warning(f"严格模式：{source['name']} 返回的模组列表为空")
                else:
                    logger.warning(f"严格模式：{source['name']} 请求失败")
            else:
                local_path = source.get("local_path")
                if local_path and local_path.exists():
                    with open(local_path, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    mods = data.get("Mods", [])
                    if mods:
                        logger.info(f"严格模式：从 {source['name']} 加载 {len(mods)} 个模组")
                        return mods, source_index
                    else:
                        logger.warning(f"严格模式：{source['name']} 文件存在但模组列表为空")
                else:
                    logger.warning(f"严格模式：{source['name']} 本地文件不存在")
        except Exception as e:
            logger.error(f"严格模式：{source['name']} 加载异常: {e}")
        logger.error(f"严格模式：源 {source['name']} 加载失败")
        return [], source_index
    else:
        # 非严格模式：自动回退到后续源
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
        return [], source_index

def load_modpacks(source_index=0, strict=False):
    """加载模组包列表，strict=True时只尝试指定源，失败则返回空列表"""
    if strict:
        # 严格模式：只尝试指定源
        if source_index >= len(MODPACK_SOURCES):
            logger.error(f"严格模式：模组包源索引 {source_index} 超出范围")
            return [], source_index
        source = MODPACK_SOURCES[source_index]
        try:
            if source.get("url"):
                logger.info(f"严格模式：正在尝试从 {source['name']} 加载模组包...")
                response = fetch_with_retry(source["url"])
                if response:
                    data = response.json()
                    modpacks = data.get("Mods", [])
                    if modpacks:
                        logger.info(f"严格模式：从 {source['name']} 加载 {len(modpacks)} 个模组包")
                        return modpacks, source_index
                    else:
                        logger.warning(f"严格模式：{source['name']} 返回的模组包列表为空")
                else:
                    logger.warning(f"严格模式：{source['name']} 请求失败")
            else:
                local_path = source.get("local_path")
                if local_path and local_path.exists():
                    with open(local_path, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    modpacks = data.get("Mods", [])
                    if modpacks:
                        logger.info(f"严格模式：从 {source['name']} 加载 {len(modpacks)} 个模组包")
                        return modpacks, source_index
                    else:
                        logger.warning(f"严格模式：{source['name']} 文件存在但模组包列表为空")
                else:
                    logger.warning(f"严格模式：{source['name']} 本地文件不存在")
        except Exception as e:
            logger.error(f"严格模式：{source['name']} 加载异常: {e}")
        logger.error(f"严格模式：模组包源 {source['name']} 加载失败")
        return [], source_index
    else:
        # 非严格模式：自动回退到后续源
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
        return [], source_index

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

def find_mod_by_filename(filename, source_index=0, strict=False):
    mods, _ = load_modlist(source_index, strict)
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

            # 移动文件，确保目标目录存在
            for item in extract_temp.iterdir():
                target = MODS_PATH / item.name
                if item.is_dir():
                    if target.exists():
                        shutil.rmtree(target)
                    # 确保父目录存在
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copytree(item, target)
                else:
                    # 确保目标文件的父目录存在
                    target.parent.mkdir(parents=True, exist_ok=True)
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

def install_with_deps(mod, installed_records, source_index=0, strict=False):
    filename = mod["FileName"]
    mod_name = mod.get("Name", filename)

    if mod_name in installed_records:
        return [filename], None

    deps = extract_dependency_mods(mod.get("Dependency", ""))
    for dep_filename in deps:
        dep_mod = find_mod_by_filename(dep_filename, source_index, strict)
        if not dep_mod:
            return None, f"依赖模组 {dep_filename} 不存在于列表中"
        dep_installed, dep_err = install_with_deps(dep_mod, installed_records, source_index, strict)
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
    # 获取语言参数，默认为中文
    lang = request.args.get("lang", "zh")
    if lang not in ["zh", "en"]:
        lang = "zh"
    
    # 加载翻译
    translations = load_translations(lang)
    
    return render_template("index.html", lang=lang, translations=translations)

@app.route("/static/<path:filename>")
def serve_static(filename):
    return send_from_directory(get_resource_path("static"), filename)

@app.route("/api/sources")
def get_sources():
    logger.info("API /api/sources 被调用")
    result = [{"name": s["name"], "index": i} for i, s in enumerate(MODLIST_SOURCES)]
    logger.info(f"返回源列表: {result}")
    return jsonify(result)

@app.route("/api/mods", methods=["GET"])
def get_mods():
    source_index = request.args.get("source", 0, type=int)
    strict = request.args.get("strict", "false").lower() == "true"
    logger.info(f"API /api/mods 被调用, source={source_index}, strict={strict}")
    mods, active_index = load_modlist(source_index, strict)
    logger.info(f"加载了 {len(mods)} 个模组, 实际源索引: {active_index}")
    installed = get_installed_mods()
    
    # 统计缺少文件名的模组
    missing_filename_count = 0
    missing_name_count = 0
    
    for i, mod in enumerate(mods):
        # 确保Name字段存在
        if "Name" not in mod or not mod["Name"]:
            missing_name_count += 1
            # 尝试从FileName或Link生成Name
            if "FileName" in mod and mod["FileName"]:
                mod["Name"] = mod["FileName"].replace('.dll', '').replace('.DLL', '')
                logger.warning(f"模组索引 {i} 缺少Name字段，从FileName生成: {mod['Name']}")
            elif "Link" in mod and mod["Link"]:
                import os
                filename = os.path.basename(mod["Link"])
                mod["Name"] = filename.replace('.dll', '').replace('.DLL', '')
                logger.warning(f"模组索引 {i} 缺少Name字段，从Link生成: {mod['Name']}")
            else:
                mod["Name"] = f"未知模组_{i}"
                logger.error(f"模组索引 {i} 缺少所有必要字段，无法生成有效名称")
        
        # 确保FileName字段存在，如果没有则尝试从Link提取或使用Name
        original_filename = mod.get("FileName", "")
        if not original_filename or original_filename.strip() == "":
            missing_filename_count += 1
            if "Link" in mod and mod["Link"]:
                # 从Link中提取文件名
                import os
                mod["FileName"] = os.path.basename(mod["Link"])
                logger.info(f"模组 '{mod.get('Name', '未知')}' 缺少文件名，从Link提取: {mod['FileName']}")
            elif "Name" in mod:
                mod["FileName"] = f"{mod['Name']}.dll"
                logger.info(f"模组 '{mod['Name']}' 缺少文件名，使用Name生成: {mod['FileName']}")
            else:
                mod["FileName"] = "unknown.dll"
                logger.warning(f"模组缺少必要字段，无法生成文件名: {mod}")
        elif original_filename != mod["FileName"]:
            logger.debug(f"模组 '{mod.get('Name', '未知')}' 文件名已修正: {original_filename} -> {mod['FileName']}")
        
        mod_name = mod.get("Name", mod["FileName"])
        mod["is_installed"] = mod_name in installed
        mod["installed_version"] = installed.get(mod_name, "")
    
    if missing_filename_count > 0 or missing_name_count > 0:
        logger.info(f"API返回 {len(mods)} 个模组，其中 {missing_filename_count} 个缺少文件名，{missing_name_count} 个缺少名称，已自动修复")
    
    return jsonify({"mods": mods, "active_source": active_index, "strict": strict})

@app.route("/api/modpacks", methods=["GET"])
def get_modpacks_route():
    source_index = request.args.get("source", 0, type=int)
    strict = request.args.get("strict", "false").lower() == "true"
    modpacks, active_index = load_modpacks(source_index, strict)
    return jsonify({"modpacks": modpacks, "active_source": active_index, "strict": strict})

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
    source_index = data.get("source", 0)
    strict = data.get("strict", False)

    mod = find_mod_by_filename(filename, source_index, strict)
    if not mod:
        return jsonify({"error": "模组不存在"}), 404

    installed = get_installed_mods()
    mod_name = mod.get("Name", filename)
    if mod_name in installed:
        return jsonify({"error": "模组已安装"}), 400

    installed_copy = installed.copy()
    result, err = install_with_deps(mod, installed_copy, source_index, strict)
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
    source_index = data.get("source", 0)
    strict = data.get("strict", False)

    mods, _ = load_modlist(source_index, strict)
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
    result, err = install_with_deps(target_mod, installed_copy, source_index, strict)
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

@app.route("/api/get-installed-filenames", methods=["GET"])
def get_installed_filenames():
    """获取已安装模组的文件名列表用于导出"""
    installed = get_installed_mods()
    filenames = []
    for name in installed.keys():
        manifest_path = VERSIONS_PATH / f"{name}_manifest.json"
        if manifest_path.exists():
            try:
                files = json.loads(manifest_path.read_text(encoding="utf-8"))
                for f in files:
                    if f.endswith('.dll'):
                        filenames.append(os.path.basename(f))
            except:
                pass
    return jsonify({"filenames": filenames})

@app.route("/api/install-from-filenames", methods=["POST"])
def install_from_filenames():
    """根据文件名列表安装模组"""
    data = request.get_json()
    if not data or not isinstance(data.get("filenames"), list):
        return jsonify({"error": "缺少文件名列表"}), 400
    
    filenames = data.get("filenames", [])
    source_index = data.get("source", 4)
    
    installed = get_installed_mods()
    results = []
    
    for fn in filenames:
        fn = fn.strip()
        if not fn or fn.startswith("#"):
            continue
        mod = find_mod_by_filename(fn, source_index)
        if not mod:
            results.append(f"❌ {fn} 不存在于模组列表中")
            continue
        mod_name = mod.get("Name", fn)
        if mod_name in installed:
            results.append(f"⏭️ {mod_name} 已安装，跳过")
            continue
        _, err = install_with_deps(mod, installed, source_index)
        if err:
            results.append(f"❌ {mod_name}: {err}")
        else:
            installed[mod_name] = mod.get("Version", "0")
            results.append(f"✅ {mod_name} 安装成功")
    
    return jsonify({"success": True, "results": results})

@app.route("/api/license")
def get_license():
    """获取许可证文件内容"""
    try:
        license_path = BASE_DIR / "LICENSE_AND_NOTICE.md"
        logger.info(f"尝试加载许可证文件，路径: {license_path}")
        logger.info(f"文件存在: {license_path.exists()}")
        
        if license_path.exists():
            content = license_path.read_text(encoding="utf-8")
            logger.info(f"成功读取许可证文件，长度: {len(content)} 字符")
            return content, 200, {"Content-Type": "text/plain; charset=utf-8"}
        else:
            logger.error(f"许可证文件不存在: {license_path}")
            # 尝试其他可能的位置
            alt_paths = [
                Path("LICENSE_AND_NOTICE.md"),
                Path(get_resource_path("LICENSE_AND_NOTICE.md")),
                Path.cwd() / "LICENSE_AND_NOTICE.md",
            ]
            for alt_path in alt_paths:
                logger.info(f"尝试备用路径: {alt_path}, 存在: {alt_path.exists()}")
                if alt_path.exists():
                    content = alt_path.read_text(encoding="utf-8")
                    logger.info(f"从备用路径读取成功: {alt_path}")
                    return content, 200, {"Content-Type": "text/plain; charset=utf-8"}
            
            return "许可证文件不存在", 404
    except Exception as e:
        logger.error(f"读取许可证文件失败: {e}")
        return f"无法读取许可证文件: {str(e)}", 500

@app.route("/api/launch-exe", methods=["POST"])
def launch_exe():
    """启动游戏EXE"""
    data = request.get_json()
    exe_path = data.get("path")
    
    if not exe_path:
        return jsonify({"error": "缺少EXE路径"}), 400
    
    exe_path_obj = Path(exe_path)
    if not exe_path_obj.exists():
        return jsonify({"error": f"EXE文件不存在: {exe_path}"}), 400
    
    try:
        import subprocess
        # 启动游戏EXE
        if exe_path_obj.suffix.lower() == '.exe':
            subprocess.Popen([str(exe_path_obj)], cwd=exe_path_obj.parent)
        else:
            # 对于其他可执行文件
            subprocess.Popen([str(exe_path_obj)], shell=True, cwd=exe_path_obj.parent)
        
        logger.info(f"已启动游戏EXE: {exe_path}")
        return jsonify({"success": True, "message": "游戏已启动"})
    except Exception as e:
        logger.error(f"启动游戏EXE失败: {e}")
        return jsonify({"error": f"启动失败: {str(e)}"}), 500

@app.route("/api/browse-exe", methods=["POST"])
def browse_exe():
    """打开文件对话框选择EXE文件（使用线程避免阻塞）"""
    import threading
    import queue
    
    def tkinter_file_dialog():
        """使用tkinter打开文件对话框"""
        try:
            import tkinter as tk
            from tkinter import filedialog
            
            root = tk.Tk()
            root.withdraw()
            root.attributes('-topmost', True)
            
            file_path = filedialog.askopenfilename(
                title="选择游戏EXE文件",
                filetypes=[("可执行文件", "*.exe"), ("所有文件", "*.*")]
            )
            
            root.destroy()
            return file_path
        except Exception as e:
            logger.error(f"tkinter文件对话框失败: {e}")
            return None
    
    def powershell_file_dialog():
        """使用PowerShell打开文件对话框"""
        try:
            if platform.system() != "Windows":
                return None
            
            # 使用PowerShell的OpenFileDialog
            ps_script = """
            Add-Type -AssemblyName System.Windows.Forms
            $dialog = New-Object System.Windows.Forms.OpenFileDialog
            $dialog.Filter = '可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*'
            $dialog.Title = '选择游戏EXE文件'
            $dialog.ShowDialog() | Out-Null
            if ($dialog.FileName) {
                $dialog.FileName
            }
            """
            # 使用更短的超时，避免长时间等待
            result = subprocess.run(["powershell", "-Command", ps_script], 
                                  capture_output=True, text=True, timeout=15)
            if result.returncode == 0 and result.stdout.strip():
                return result.stdout.strip()
        except subprocess.TimeoutExpired:
            logger.warning("PowerShell文件选择器超时")
        except Exception as e:
            logger.error(f"PowerShell文件对话框失败: {e}")
        
        return None
    
    try:
        # 先尝试tkinter
        try:
            import tkinter as tk
            from tkinter import filedialog
            use_tkinter = True
        except ImportError:
            use_tkinter = False
        
        file_path = None
        
        if use_tkinter:
            # 在线程中运行tkinter对话框
            result_queue = queue.Queue()
            thread = threading.Thread(target=lambda q: q.put(tkinter_file_dialog()), args=(result_queue,))
            thread.daemon = True
            thread.start()
            thread.join(timeout=20)  # 20秒超时
            
            if thread.is_alive():
                logger.warning("tkinter文件选择器超时")
            else:
                file_path = result_queue.get() if not result_queue.empty() else None
        
        # 如果tkinter失败或不可用，尝试PowerShell
        if not file_path:
            file_path = powershell_file_dialog()
        
        if file_path:
            logger.info(f"用户选择的文件路径: {file_path}")
            return jsonify({"success": True, "path": file_path})
        else:
            logger.warning("文件选择器未返回有效路径")
            return jsonify({"error": "未选择文件或选择超时，请手动输入路径"}), 400
            
    except Exception as e:
        logger.error(f"打开文件对话框失败: {e}")
        return jsonify({"error": f"文件选择失败: {str(e)}，请手动输入路径"}), 500

@app.route("/api/install-patcher", methods=["POST"])
def install_patcher():
    """安装模组加载器（TLDPatcher）"""
    try:
        # 调试：输出当前BASE_DIR路径
        logger.info(f"当前BASE_DIR路径: {BASE_DIR}")
        logger.info(f"当前工作目录: {os.getcwd()}")
        
        patcher_path = BASE_DIR / "TLDPatcher" / "TLDPatcher.exe"
        logger.info(f"检查路径1: {patcher_path}")
        
        if not patcher_path.exists():
            # 如果TLDPatcher目录不存在，检查exe同级目录
            patcher_path = BASE_DIR / "TLDPatcher.exe"
            logger.info(f"检查路径2: {patcher_path}")
            if not patcher_path.exists():
                # 尝试相对路径
                patcher_path = Path("TLDPatcher") / "TLDPatcher.exe"
                logger.info(f"检查路径3: {patcher_path}")
                if not patcher_path.exists():
                    patcher_path = Path("TLDPatcher.exe")
                    logger.info(f"检查路径4: {patcher_path}")
                    if not patcher_path.exists():
                        return jsonify({"error": f"找不到TLDPatcher.exe文件。已检查路径：\n1. {BASE_DIR / 'TLDPatcher' / 'TLDPatcher.exe'}\n2. {BASE_DIR / 'TLDPatcher.exe'}\n3. TLDPatcher/TLDPatcher.exe\n4. TLDPatcher.exe"}), 404
        
        logger.info(f"找到TLDPatcher，路径: {patcher_path}")
        import subprocess
        # 启动TLDPatcher
        subprocess.Popen([str(patcher_path)], cwd=patcher_path.parent)
        
        logger.info(f"已启动模组加载器: {patcher_path}")
        return jsonify({"success": True, "message": "模组加载器已启动"})
    except Exception as e:
        logger.error(f"启动模组加载器失败: {e}")
        return jsonify({"error": f"启动失败: {str(e)}"}), 500

@app.route("/api/translations")
def get_translations():
    """获取翻译文本"""
    lang = request.args.get("lang", "zh")
    if lang not in ["zh", "en"]:
        lang = "zh"
    
    translations = load_translations(lang)
    return jsonify({"lang": lang, "translations": translations})

@app.route("/api/favorites", methods=["GET"])
def get_favorites():
    """获取收藏列表"""
    favorites = load_favorites()
    return jsonify({"favorites": favorites})

@app.route("/api/favorites", methods=["POST"])
def save_favorites_route():
    """保存收藏列表"""
    data = request.get_json()
    if not data or not isinstance(data.get("favorites"), list):
        return jsonify({"error": "缺少收藏数据"}), 400
    
    favorites = data.get("favorites", [])
    ok = save_favorites(favorites)
    if ok:
        return jsonify({"success": True, "count": len(favorites)})
    else:
        return jsonify({"error": "保存失败"}), 500

@app.route("/api/paths")
def get_paths():
    """获取路径信息"""
    return jsonify({
        "documents_path": str(DOCUMENTS_PATH),
        "game_path": str(GAME_PATH),
        "mods_path": str(MODS_PATH),
        "is_admin": is_admin()
    })

@app.errorhandler(Exception)
def handle_exception(e):
    logger.exception("未捕获的异常")
    return jsonify({"error": "服务器内部错误"}), 500

def open_browser():
    """启动后自动打开浏览器"""
    import time
    import threading
    import webbrowser
    
    def _open():
        time.sleep(2)  # 等待服务器启动
        try:
            webbrowser.open("http://127.0.0.1:5000")
            logger.info("已自动打开浏览器")
        except Exception as e:
            logger.error(f"自动打开浏览器失败: {e}")
    
    thread = threading.Thread(target=_open)
    thread.daemon = True
    thread.start()
    logger.info("已启动自动打开浏览器线程")

if __name__ == "__main__":
    # 自动打开浏览器
    open_browser()
    app.run(debug=False, threaded=True)