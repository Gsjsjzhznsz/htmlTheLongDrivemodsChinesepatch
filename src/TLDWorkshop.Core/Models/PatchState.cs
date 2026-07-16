namespace TLDWorkshop.Core.Models;

/// <summary>
/// TLDLoader 补丁状态机。对应原项目 CheckPatchStatus 的 6 种判定结果：
/// Patched / NotPatched / NeedsDllUpdate / OldFilesFound / OldPatchFound / GameUpdated。
/// </summary>
public enum PatchState
{
    Unknown,
    NoPath,
    Patched,
    NotPatched,
    NeedsDllUpdate,
    OldFilesFound,
    OldPatchFound,
    GameUpdated,
}
