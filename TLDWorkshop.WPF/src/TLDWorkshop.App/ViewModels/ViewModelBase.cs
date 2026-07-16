using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TLDWorkshop.App.ViewModels;

/// <summary>
/// 所有 ViewModel 的基类。用 CommunityToolkit.Mvvm 的 [ObservableProperty] 源生成器，
/// 不再手写 INotifyPropertyChanged。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    public virtual string Title => string.Empty;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
}
