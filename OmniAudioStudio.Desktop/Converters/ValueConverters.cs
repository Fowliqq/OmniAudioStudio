using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OmniAudioStudio.Desktop.Models;
using Wpf.Ui.Controls;

namespace OmniAudioStudio.Desktop.Converters;

/// <summary>
/// Returns clean, high-contrast, modern text foreground brushes for task statuses.
/// Complies with Progressive UI/UX standard (Emerald, Sky, Rose, Slate, Zinc).
/// </summary>
public class TaskStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PendingBrush = new(Color.FromRgb(148, 163, 184));    // #94A3B8 Slate-400
    private static readonly SolidColorBrush ConvertingBrush = new(Color.FromRgb(56, 189, 248));  // #38BDF8 Sky-400
    private static readonly SolidColorBrush CompletedBrush = new(Color.FromRgb(52, 211, 153));   // #34D399 Emerald-400
    private static readonly SolidColorBrush FailedBrush = new(Color.FromRgb(251, 113, 133));     // #FB7185 Rose-400
    private static readonly SolidColorBrush CancelledBrush = new(Color.FromRgb(161, 161, 170));  // #A1A1AA Zinc-400

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.TaskStatus status)
        {
            return status switch
            {
                Models.TaskStatus.Pending => PendingBrush,
                Models.TaskStatus.Converting => ConvertingBrush,
                Models.TaskStatus.Completed => CompletedBrush,
                Models.TaskStatus.Failed => FailedBrush,
                Models.TaskStatus.Cancelled => CancelledBrush,
                _ => PendingBrush
            };
        }
        return PendingBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Returns soft, translucent pill backgrounds (10-14% opacity).
/// </summary>
public class TaskStatusToBackgroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PendingBg = new(Color.FromArgb(24, 148, 163, 184));    // #1894A3B8
    private static readonly SolidColorBrush ConvertingBg = new(Color.FromArgb(34, 56, 189, 248));  // #2238BDF8
    private static readonly SolidColorBrush CompletedBg = new(Color.FromArgb(34, 52, 211, 153));   // #2234D399
    private static readonly SolidColorBrush FailedBg = new(Color.FromArgb(34, 251, 113, 133));     // #22FB7185
    private static readonly SolidColorBrush CancelledBg = new(Color.FromArgb(24, 161, 161, 170));  // #18A1A1AA

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.TaskStatus status)
        {
            return status switch
            {
                Models.TaskStatus.Pending => PendingBg,
                Models.TaskStatus.Converting => ConvertingBg,
                Models.TaskStatus.Completed => CompletedBg,
                Models.TaskStatus.Failed => FailedBg,
                Models.TaskStatus.Cancelled => CancelledBg,
                _ => PendingBg
            };
        }
        return PendingBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Returns subtle 1px border brushes corresponding to each status pill.
/// </summary>
public class TaskStatusToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PendingBorder = new(Color.FromArgb(50, 148, 163, 184));
    private static readonly SolidColorBrush ConvertingBorder = new(Color.FromArgb(70, 56, 189, 248));
    private static readonly SolidColorBrush CompletedBorder = new(Color.FromArgb(70, 52, 211, 153));
    private static readonly SolidColorBrush FailedBorder = new(Color.FromArgb(70, 251, 113, 133));
    private static readonly SolidColorBrush CancelledBorder = new(Color.FromArgb(50, 161, 161, 170));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.TaskStatus status)
        {
            return status switch
            {
                Models.TaskStatus.Pending => PendingBorder,
                Models.TaskStatus.Converting => ConvertingBorder,
                Models.TaskStatus.Completed => CompletedBorder,
                Models.TaskStatus.Failed => FailedBorder,
                Models.TaskStatus.Cancelled => CancelledBorder,
                _ => PendingBorder
            };
        }
        return PendingBorder;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Returns intuitive symbols for task status badges.
/// </summary>
public class TaskStatusToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.TaskStatus status)
        {
            return status switch
            {
                Models.TaskStatus.Pending => SymbolRegular.Clock24,
                Models.TaskStatus.Converting => SymbolRegular.ArrowSync24,
                Models.TaskStatus.Completed => SymbolRegular.CheckmarkCircle24,
                Models.TaskStatus.Failed => SymbolRegular.DismissCircle24,
                Models.TaskStatus.Cancelled => SymbolRegular.Prohibited24,
                _ => SymbolRegular.Clock24
            };
        }
        return SymbolRegular.Clock24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && !b) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class IntToEmptyQueueVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is int count && count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class GreaterZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is int count && count > 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
