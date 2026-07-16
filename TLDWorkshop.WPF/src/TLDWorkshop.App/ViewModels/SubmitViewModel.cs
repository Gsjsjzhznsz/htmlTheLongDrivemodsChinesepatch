using System.IO;
using System.IO.Compression;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.App.ViewModels;

/// <summary>
/// 模组提交 ViewModel。对应原项目 submodb_Click / submodd_Click。
///
/// 严格复刻原 exe 的 FTP 提交流程（从 IL 反编译确认）：
/// 1. 下载 ftp://.../Submissions.zip 到本地
/// 2. 解压到临时目录
/// 3. 把用户的 mod 文件 + ModInfo.txt 打包成 Submission_xxx.zip 放进解压目录
/// 4. 重新打包整个目录为 Submissions.zip
/// 5. 上传回 ftp://.../Submissions.zip
///
/// .NET 10: WebClient 已过时，改用 FtpWebRequest（FTP 专用，支持进度）。
/// 凭据默认值：kolben1000 / Kolben1000（从原 exe 提取）
/// </summary>
public partial class SubmitViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly DialogService _dialogs;
    private readonly FilePickerService _pickers;

    [ObservableProperty] private string _modName = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private double _progress;

    public SubmitViewModel(AppSettings settings, DialogService dialogs, FilePickerService pickers)
    {
        _settings = settings;
        _dialogs = dialogs;
        _pickers = pickers;
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var picked = _pickers.PickOpenFile("Mod 文件|*.zip;*.dll", "选择要提交的 mod 文件");
        if (!string.IsNullOrEmpty(picked)) FilePath = picked;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (string.IsNullOrWhiteSpace(ModName) || string.IsNullOrWhiteSpace(Author))
        {
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.Hint"), I18nService.Instance.T("Msg.InfoIncomplete"));
            return;
        }
        if (!File.Exists(FilePath))
        {
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.PathNotFound"), I18nService.Instance.T("Msg.FileNotFound"));
            return;
        }
        if (string.IsNullOrEmpty(_settings.CustomFtpUsername) ||
            string.IsNullOrEmpty(_settings.CustomFtpPassword) ||
            string.IsNullOrEmpty(_settings.ModSubmissionEndpoint))
        {
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.Hint"),
                I18nService.Instance.T("Msg.NoSubmitService"));
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusMessage = I18nService.Instance.T("Msg.ConnectingFTP");

        try
        {
            var ftpUrl = _settings.ModSubmissionEndpoint;
            var username = _settings.CustomFtpUsername!;
            var password = _settings.CustomFtpPassword!;

            // 临时工作目录
            var workDir = Path.Combine(Path.GetTempPath(), "TLDSubmit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);

            try
            {
                // 1. 准备用户的 mod 文件 + ModInfo.txt
                StatusMessage = I18nService.Instance.T("Msg.PreparingFiles");
                Progress = 10;

                var modFileDest = Path.Combine(workDir, Path.GetFileName(FilePath));
                File.Copy(FilePath, modFileDest, overwrite: true);

                var modInfoPath = Path.Combine(workDir, "ModInfo.txt");
                await File.WriteAllTextAsync(modInfoPath,
                    $"Name: {ModName}\nYour Username (Discord): {Author}\nVersion of Mod: {Version}\nShort description: {Description}\n");

                // 2. 把用户文件打包成 Submission_{timestamp}.zip
                StatusMessage = I18nService.Instance.T("Msg.Packing");
                Progress = 30;

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var submissionZipName = $"Submission_{timestamp}.zip";
                var submissionZipPath = Path.Combine(Path.GetTempPath(), submissionZipName);
                ZipFile.CreateFromDirectory(workDir, submissionZipPath);

                // 3. 下载现有的 Submissions.zip（包含所有历史提交）
                StatusMessage = I18nService.Instance.T("Msg.DownloadingExisting");
                Progress = 40;

                var existingSubmissionsZip = Path.Combine(workDir, "Submissions.zip");
                var submissionsDir = Path.Combine(workDir, "Submissions");
                Directory.CreateDirectory(submissionsDir);

                try
                {
                    await DownloadFtpFileAsync(ftpUrl, existingSubmissionsZip, username, password);
                    // 解压到 Submissions 目录
                    ZipFile.ExtractToDirectory(existingSubmissionsZip, submissionsDir);
                }
                catch
                {
                    // 服务器上还没有 Submissions.zip，第一次提交，忽略错误
                }

                // 4. 把新的 Submission_xxx.zip 放进 Submissions 目录
                File.Copy(submissionZipPath, Path.Combine(submissionsDir, submissionZipName), overwrite: true);

                // 5. 重新打包整个 Submissions 目录为 Submissions.zip
                StatusMessage = I18nService.Instance.T("Msg.Repacking");
                Progress = 70;

                var finalZipPath = Path.Combine(workDir, "Submissions_final.zip");
                if (File.Exists(finalZipPath)) File.Delete(finalZipPath);
                ZipFile.CreateFromDirectory(submissionsDir, finalZipPath);

                // 6. 上传回 FTP 服务器
                StatusMessage = I18nService.Instance.T("Msg.Uploading");
                Progress = 85;

                await UploadFtpFileAsync(ftpUrl, finalZipPath, username, password);

                Progress = 100;
                StatusMessage = I18nService.Instance.T("Msg.SubmitDone");
                AppLog.Add($"模组提交成功：{ModName} by {Author}");
                await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.SubmitDone"),
                    I18nService.Instance.T("Msg.SubmitSuccess"));
            }
            finally
            {
                try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
                catch { /* 清理失败不致命 */ }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = I18nService.Instance.T("Msg.SubmitFailed") + ": " + ex.Message;
            AppLog.Add($"模组提交失败：{ex.Message}");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.SubmitFailed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 用 FtpWebRequest 下载文件。
    /// .NET 10 标记 WebRequest.Create 过时，但 FtpWebRequest 仍是 FTP 的唯一官方方案
    /// （HttpClient 不支持 FTP 协议），所以这里用 pragma 抑制警告。
    /// </summary>
#pragma warning disable SYSLIB0014
    private static async Task DownloadFtpFileAsync(string ftpUrl, string localPath, string username, string password)
    {
        var request = (FtpWebRequest)WebRequest.Create(ftpUrl);
        request.Method = WebRequestMethods.Ftp.DownloadFile;
        request.Credentials = new NetworkCredential(username, password);
        request.UseBinary = true;
        request.UsePassive = true;

        using var response = (FtpWebResponse)await request.GetResponseAsync();
        await using var responseStream = response.GetResponseStream();
        await using var fileStream = File.Create(localPath);
        await responseStream.CopyToAsync(fileStream);
    }

    /// <summary>
    /// 用 FtpWebRequest 上传文件。
    /// </summary>
    private static async Task UploadFtpFileAsync(string ftpUrl, string localPath, string username, string password)
    {
        var request = (FtpWebRequest)WebRequest.Create(ftpUrl);
        request.Method = WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new NetworkCredential(username, password);
        request.UseBinary = true;
        request.UsePassive = true;

        await using var fileStream = File.OpenRead(localPath);
        await using var requestStream = await request.GetRequestStreamAsync();
        await fileStream.CopyToAsync(requestStream);

        using var response = (FtpWebResponse)await request.GetResponseAsync();
    }
#pragma warning restore SYSLIB0014
}
