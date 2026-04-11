# 许可证与第三方声明 / License and Third-Party Notices

**项目名称 / Project Name:** The Long Drive Mod Installer  
**仓库地址 / Repository:** [https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch)  
**版本 / Version:** 1.0.0

---

## 中文 / Chinese

### 1. 本项目许可证

本项目（The Long Drive Mod Installer）的源代码采用 **GNU Affero General Public License v3.0 (AGPL-3.0)** 进行授权。  
AGPL-3.0 是一种强 copyleft 开源许可证，要求任何人如果通过网络向用户提供服务（例如作为后端服务运行），也必须将完整源代码向所有用户公开。它基于 GPL-3.0，并增加了针对网络服务的附加条款。

**AGPL-3.0 核心要求**：  
- 您可以自由使用、修改、复制和分发本软件。
- 如果您修改了本软件，并在网络上提供服务（包括通过 Web 接口、API 等方式），您必须将完整修改版源代码向所有用户公开。
- 所有分发或网络服务版本必须同样采用 AGPL-3.0 许可证。

**AGPL-3.0 许可证摘要（非正式文本，仅供参考）：**

> GNU AFFERO GENERAL PUBLIC LICENSE
> Version 3, 19 November 2007
> 
> Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
> 
> 本程序是自由软件：您可以依据自由软件基金会发布的 GNU Affero 通用公共许可证第 3 版（或任何更新版本）的条款重新分发和/或修改它。
> 
> 本程序的分发是希望它有用，但没有任何担保；甚至没有适销性或针对特定用途的适用性的暗示担保。有关详细信息，请参见 GNU Affero 通用公共许可证。
> 
> 您应该已收到一份 GNU Affero 通用公共许可证副本。如果没有，请参阅 <https://www.gnu.org/licenses/>。

**完整的 AGPL-3.0 许可证文本请参见：** [https://www.gnu.org/licenses/agpl-3.0.html](https://www.gnu.org/licenses/agpl-3.0.html)

### 2. 引用的第三方数据 / 资源

本工具使用的模组列表数据来源于以下开源仓库。所有模组的版权归其各自的作者所有，本工具仅提供下载安装的接口，不存储或分发模组文件本身。

#### 2.1 官方源 (GitLab)
- **仓库地址**: [https://gitlab.com/KolbenLP/WorkshopTLDMods](https://gitlab.com/KolbenLP/WorkshopTLDMods)
- **许可证**: GNU General Public License v3.0 (GPL-3.0)
- **使用方式**: 本工具通过网络请求获取该仓库提供的 `modlist_3.json` 模组索引文件，用于展示和安装。

**AGPL-3.0 与 GPL-3.0 的兼容性说明**：  
- AGPL-3.0 基于 GPL-3.0，但额外包含了针对网络服务的条款。两者都是强 copyleft 许可证。
- 本工具通过网络请求调用该仓库的数据（不合并其代码），属于独立程序间的通信，不会导致本工具必须采用 GPL-3.0。
- 如果您需要获取该仓库的源代码，请直接访问其仓库地址。

#### 2.2 极狐镜像源 (GitLab China)
- **仓库地址**: [https://gitlab.com/MFSDev-NET/workshop-tld-chinese](https://gitlab.com/MFSDev-NET/workshop-tld-chinese)
- **许可证**: GNU General Public License v3.0 (GPL-3.0)
- **使用方式**: 作为国内加速镜像，数据内容与官方源一致。

#### 2.3 GitHub 镜像源
- **仓库地址**: [https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch)
- **许可证**: GNU Affero General Public License v3.0 (AGPL-3.0)（本仓库提供的前端页面与 JSON 缓存文件）
- **使用方式**: 提供本工具的在线演示页面与缓存数据。

### 3. 打包程序说明

本软件通过 PyInstaller 打包为独立的可执行文件（.exe）。打包过程中包含了本项目的源代码以及引用的第三方库（如 Flask、requests 等），这些库的许可证均兼容 AGPL-3.0 使用条件。

最终用户在使用本软件时，应遵守上述各开源许可证的条款。对于模组本身的使用，请遵循各模组作者指定的许可。

**特别注意**：如果您将本软件修改后部署为网络服务（例如通过 Web 页面提供在线安装功能），则必须根据 AGPL-3.0 的要求，向所有服务用户公开您的完整修改版源代码。

---

## English / English

### 1. This Project License

The source code of this project (The Long Drive Mod Installer) is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.  
AGPL-3.0 is a strong copyleft open-source license that requires anyone who provides services to users over a network (e.g., running as a backend service) to also make the complete source code available to all users. It is based on GPL-3.0 with an additional clause for network services.

**Core requirements of AGPL-3.0**:  
- You are free to use, modify, copy, and distribute this software.
- If you modify the software and provide services over a network (including via web interfaces, APIs, etc.), you must make the complete modified source code available to all users.
- All distributed or network-served versions must be licensed under AGPL-3.0.

**AGPL-3.0 License Summary (informative, not official):**

> GNU AFFERO GENERAL PUBLIC LICENSE
> Version 3, 19 November 2007
> 
> Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
> 
> This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
> 
> This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.
> 
> You should have received a copy of the GNU Affero General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

**Full AGPL-3.0 license text available at:** [https://www.gnu.org/licenses/agpl-3.0.html](https://www.gnu.org/licenses/agpl-3.0.html)

### 2. Third-Party Data / Resources Used

The mod list data used by this tool comes from the following open-source repositories. The copyright of all mods belongs to their respective authors. This tool only provides an interface for downloading and installation; it does not store or distribute the mod files themselves.

#### 2.1 Official Source (GitLab)
- **Repository**: [https://gitlab.com/KolbenLP/WorkshopTLDMods](https://gitlab.com/KolbenLP/WorkshopTLDMods)
- **License**: GNU General Public License v3.0 (GPL-3.0)
- **Usage**: This tool fetches the `modlist_3.json` mod index file from this repository via network requests for display and installation purposes.

**AGPL-3.0 and GPL-3.0 Compatibility Note**:  
- AGPL-3.0 is based on GPL-3.0 with an additional network service clause. Both are strong copyleft licenses.
- This tool calls data from this repository via network requests (without merging its code), which is considered communication between independent programs and does not require this tool to adopt GPL-3.0.
- If you need the source code of this repository, please visit its repository URL directly.

#### 2.2 Jihulab Mirror (GitLab China)
- **Repository**: [https://gitlab.com/MFSDev-NET/workshop-tld-chinese](https://gitlab.com/MFSDev-NET/workshop-tld-chinese)
- **License**: GNU General Public License v3.0 (GPL-3.0)
- **Usage**: Acts as a domestic mirror for faster access; data content is consistent with the official source.

#### 2.3 GitHub Mirror
- **Repository**: [https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch)
- **License**: GNU Affero General Public License v3.0 (AGPL-3.0) (for the frontend pages and cached JSON data provided by this repository)
- **Usage**: Provides an online demo page and cached data for this tool.

### 3. Packaged Application Statement

This software is packaged into a standalone executable file (.exe) using PyInstaller. The packaging process includes the source code of this project as well as referenced third-party libraries (such as Flask, requests, etc.), all of which have licenses compatible with AGPL-3.0 usage conditions.

End users should comply with the terms of the aforementioned open-source licenses when using this software. For the use of the mods themselves, please follow the licenses specified by each mod author.

**Special Note**: If you modify this software and deploy it as a network service (e.g., providing online installation functionality via a web page), you must make your complete modified source code available to all service users as required by AGPL-3.0.

---

## 联系方式 / Contact

- **Issues / 问题反馈**: [GitHub Issues](https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/issues)
- **QQ Group / QQ 交流群**: 661726941

---

**最后更新 / Last Updated:** 2026-04-03
