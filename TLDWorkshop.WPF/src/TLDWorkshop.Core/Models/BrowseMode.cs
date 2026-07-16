namespace TLDWorkshop.Core.Models;

/// <summary>
/// 浏览列表的模式枚举 —— 这是修复 Bug #1 的核心。
/// 原版 WinForms 代码靠 <c>mymods.Text == "Back"</c> 字符串判断模式，
/// 汉化时按钮文字被改成"返回"，导致模式判断永远走错分支，触发列表重新加载。
/// 此处用强类型枚举替代字符串比较，从架构上根除该问题。
/// </summary>
public enum BrowseMode
{
    /// <summary>在线 mod 浏览（默认）</summary>
    Online,
    /// <summary>已安装的我的模组</summary>
    MyMods,
    /// <summary>整合包列表</summary>
    Modpack,
    /// <summary>正在查看单个 mod 详情</summary>
    Detail
}
