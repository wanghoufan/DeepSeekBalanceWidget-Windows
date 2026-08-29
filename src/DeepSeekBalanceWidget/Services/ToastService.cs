using System.Windows;

namespace DeepSeekBalanceWidget.Services;

public static class ToastService
{
    public static void Show(Window owner, string title, string body)
    {
        // 位置由 ToastWindow 自身固定在屏幕右下角，不再依赖主窗位置
        var toast = new ToastWindow(title, body)
        {
            Owner = owner
        };
        toast.Show();
    }
}
