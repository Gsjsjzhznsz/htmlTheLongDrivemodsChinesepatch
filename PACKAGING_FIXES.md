# 打包修复与优化指南

## 已修复的问题

### 1. **build.spec配置问题**
**修复**：更新了`datas`部分，包含所有必需文件：
```python
datas=[
    ('modlist_3.json', '.'),
    ('Modpacks', 'Modpacks'),
    ('static', 'static'),
    ('templates', 'templates'),
    ('TLDPatcher', 'TLDPatcher'),      # 新增
    ('LICENSE_AND_NOTICE.md', '.'),    # 新增
    ('translations', 'translations'),  # 新增
],
```
**影响**：确保打包时包含TLDPatcher.exe、许可证文件和翻译文件。

### 2. **TLDPatcher.exe路径问题**
**修复**：在`install_patcher()`函数中添加了详细的路径调试日志，检查多个可能位置：
1. `BASE_DIR/TLDPatcher/TLDPatcher.exe`
2. `BASE_DIR/TLDPatcher.exe`
3. 相对路径`TLDPatcher/TLDPatcher.exe`
4. 相对路径`TLDPatcher.exe`

### 3. **许可证文件加载问题**
**修复**：在`get_license()`函数中添加了调试日志和备用路径检查：
1. 主路径：`BASE_DIR / "LICENSE_AND_NOTICE.md"`
2. 备用路径：`Path("LICENSE_AND_NOTICE.md")`
3. 备用路径：`Path(get_resource_path("LICENSE_AND_NOTICE.md"))`
4. 备用路径：`Path.cwd() / "LICENSE_AND_NOTICE.md"`

### 4. **文件选择器超时问题**
**修复**：改进了`/api/browse-exe`端点：
- 使用线程运行tkinter文件对话框，避免阻塞主线程
- 20秒超时设置
- 备用的PowerShell文件对话框方案
- 更好的错误处理和日志记录

### 5. **语言系统基础框架**
**新增**：创建了基本的语言系统框架：
- 翻译文件：`translations/zh.json`和`translations/en.json`
- API端点：`/api/translations?lang=zh|en`
- 根路由支持语言参数：`/?lang=zh`
- 模板变量：`lang`和`translations`

## 待完善的特性

### 1. **完整的前端语言切换**
当前已创建翻译文件和API，但前端需要进一步实现：
1. 修改`templates/index.html`使用模板变量而不是硬编码文本
2. 添加语言切换时的动态文本更新
3. 保存用户语言偏好到localStorage

**示例实现**：
```javascript
// 加载翻译
async function loadTranslations(lang) {
    const res = await fetch(`/api/translations?lang=${lang}`);
    const data = await res.json();
    window.translations = data.translations;
    applyTranslations();
}

// 应用翻译到页面
function applyTranslations() {
    const t = window.translations;
    document.querySelector('[data-i18n="app_title"]').textContent = t.app_title;
    document.querySelector('[data-i18n="browse_mods"]').textContent = t.browse_mods;
    // ... 更多元素
}
```

### 2. **字体颜色全面应用**
已修复`applyTheme()`函数，但可能需要调整CSS选择器以确保所有文本元素都被更新。

### 3. **安装失败处理**
已添加文件名安全检查，但可能需要更全面的错误处理。

## 打包命令

```bash
# 清理旧的构建
pyinstaller build.spec --clean

# 重新构建
pyinstaller build.spec

# 构建目录：dist/ModpackManager/
```

## 测试建议

### 1. **测试TLDPatcher**
```bash
# 运行打包后的程序
cd dist/ModpackManager
ModpackManager.exe

# 点击"安装模组加载器"按钮
# 检查日志文件app.log中的路径信息
```

### 2. **测试许可证加载**
```bash
# 检查关于页面
# 查看控制台网络请求：/api/license
# 检查app.log中的调试信息
```

### 3. **测试文件选择器**
```bash
# 在设置中点击"浏览"按钮
# 测试tkinter和PowerShell两种方式
# 检查超时处理
```

### 4. **测试打包完整性**
```bash
# 检查dist/ModpackManager/目录是否包含：
# - TLDPatcher/TLDPatcher.exe
# - LICENSE_AND_NOTICE.md
# - translations/zh.json
# - translations/en.json
```

## 故障排除

### 1. **文件找不到**
**症状**：TLDPatcher或许可证文件加载失败
**解决**：
- 检查`app.log`中的路径调试信息
- 验证文件是否被打包到正确位置
- 检查`get_resource_path()`函数

### 2. **文件选择器卡死**
**症状**：点击浏览按钮后程序无响应
**解决**：
- 检查线程超时设置（当前20秒）
- 尝试禁用tkinter，使用PowerShell方案
- 增加日志记录

### 3. **语言不生效**
**症状**：语言设置保存但界面不更新
**解决**：
- 检查`/api/translations`端点响应
- 实现前端翻译应用逻辑
- 验证翻译文件格式

## 性能优化

1. **减少打包体积**：
   - 保持`excludes`列表排除不必要的大型库
   - 使用`upx=True`压缩可执行文件
   - `console=False`隐藏控制台窗口

2. **提高启动速度**：
   - 避免在导入时进行重型操作
   - 使用懒加载模式
   - 优化资源文件加载

## 安全注意事项

1. **文件路径验证**：
   - 所有用户提供的路径都经过验证
   - 检查文件是否存在和权限
   - 防止路径遍历攻击

2. **子进程安全**：
   - 使用绝对路径执行外部程序
   - 限制子进程权限
   - 超时和资源限制

## 后续开发建议

1. **完善语言系统**：实现完整的国际化支持
2. **主题系统**：允许保存和加载多个主题预设
3. **离线模式**：缓存模组列表，支持完全离线使用
4. **模组管理**：添加模组启用/禁用、配置文件编辑功能
5. **自动更新**：程序自动检查更新并升级