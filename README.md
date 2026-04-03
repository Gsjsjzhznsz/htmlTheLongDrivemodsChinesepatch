```markdown
# 许可证与第三方声明 / License and Third-Party Notices

**项目名称 / Project Name:** The Long Drive Mod Installer  
**仓库地址 / Repository:** [https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch)  
**版本 / Version:** 1.0.0

---

## 中文 / Chinese

### 1. 本项目许可证

本项目（The Long Drive Mod Installer）的源代码采用 **MIT 许可证** 进行授权。  
MIT 许可证是一种宽松的开源许可证，允许任何人使用、复制、修改、合并、出版发行、分发、再许可和/或销售本软件的副本，只要在分发时保留原版权和许可声明。

**MIT 许可证文本：**

```
MIT License

Copyright (c) 2025 Gsjsjzhznsz

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 2. 引用的第三方数据 / 资源

本工具使用的模组列表数据来源于以下开源仓库。所有模组的版权归其各自的作者所有，本工具仅提供下载安装的接口，不存储或分发模组文件本身。

#### 2.1 官方源 (GitLab)
- **仓库地址**: [https://gitlab.com/KolbenLP/WorkshopTLDMods](https://gitlab.com/KolbenLP/WorkshopTLDMods)
- **许可证**: GNU General Public License v3.0 (GPL-3.0)
- **使用方式**: 本工具通过网络请求获取该仓库提供的 `modlist_3.json` 模组索引文件，用于展示和安装。

**GPL-3.0 核心要求**：  
- 本工具作为一个独立程序，通过网络请求调用该仓库的数据，属于“合理使用”范畴。
- 本工具的 MIT 许可证与 GPL-3.0 不冲突，因为本工具未将 GPL-3.0 代码直接合并到自身代码中。
- 若用户需要获取该仓库的源代码，请访问其仓库地址。

#### 2.2 极狐镜像源 (GitLab China)
- **仓库地址**: [https://gitlab.com/MFSDev-NET/workshop-tld-chinese](https://gitlab.com/MFSDev-NET/workshop-tld-chinese)
- **许可证**: GNU General Public License v3.0 (GPL-3.0)
- **使用方式**: 作为国内加速镜像，数据内容与官方源一致。

#### 2.3 GitHub 镜像源
- **仓库地址**: [https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch)
- **许可证**: MIT License（本仓库提供的前端页面与 JSON 缓存文件）
- **使用方式**: 提供本工具的在线演示页面与缓存数据。

### 3. 打包程序说明

本软件通过 PyInstaller 打包为独立的可执行文件（.exe）。打包过程中包含了本项目的源代码以及引用的第三方库（如 Flask、requests 等），这些库的许可证均兼容 MIT 和 GPL 使用条件。

最终用户在使用本软件时，应遵守上述各开源许可证的条款。对于模组本身的使用，请遵循各模组作者指定的许可。

---

## English / English

### 1. This Project License

The source code of this project (The Long Drive Mod Installer) is licensed under the **MIT License**.  
The MIT License is a permissive open-source license that allows anyone to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the software, provided that the original copyright and license notice are retained in the distribution.

**MIT License Text:**

```
MIT License

Copyright (c) 2025 Gsjsjzhznsz

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 2. Third-Party Data / Resources Used

The mod list data used by this tool comes from the following open-source repositories. The copyright of all mods belongs to their respective authors. This tool only provides an interface for downloading and installation; it does not store or distribute the mod files themselves.

#### 2.1 Official Source (GitLab)
- **Repository**: [https://gitlab.com/KolbenLP/WorkshopTLDMods](https://gitlab.com/KolbenLP/WorkshopTLDMods)
- **License**: GNU General Public License v3.0 (GPL-3.0)
- **Usage**: This tool fetches the `modlist_3.json` mod index file from this repository via network requests for display and installation purposes.

**GPL-3.0 Core Requirements**:  
- This tool, as an independent program, calls data from this repository via network requests, which falls under "fair use".
- The MIT license of this tool does not conflict with GPL-3.0, as the GPL-3.0 code is not directly merged into this tool's codebase.
- If users need the source code of this repository, they should visit the repository URL.

#### 2.2 Jihulab Mirror (GitLab China)
- **Repository**: [https://gitlab.com/MFSDev-NET/workshop-tld-chinese](https://gitlab.com/MFSDev-NET/workshop-tld-chinese)
- **License**: GNU General Public License v3.0 (GPL-3.0)
- **Usage**: Acts as a domestic mirror for faster access; data content is consistent with the official source.

#### 2.3 GitHub Mirror
- **Repository**: [https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch)
- **License**: MIT License (for the frontend pages and cached JSON data provided by this repository)
- **Usage**: Provides an online demo page and cached data for this tool.

### 3. Packaged Application Statement

This software is packaged into a standalone executable file (.exe) using PyInstaller. The packaging process includes the source code of this project as well as referenced third-party libraries (such as Flask, requests, etc.), all of which have licenses compatible with MIT and GPL usage conditions.

End users should comply with the terms of the aforementioned open-source licenses when using this software. For the use of the mods themselves, please follow the licenses specified by each mod author.

---

## 联系方式 / Contact

- **Issues / 问题反馈**: [GitHub Issues](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/issues)
- **QQ Group / QQ 交流群**: 661726941

---

**最后更新 / Last Updated:** 2026-03-28
```