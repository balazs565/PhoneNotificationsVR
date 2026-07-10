using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PhoneNotificationsVR.App.Converters;

/// <summary>Maps a bool to one of two brushes (used for the status dots).</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Green;
    public Brush FalseBrush { get; set; } = Brushes.Red;
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? TrueBrush : FalseBrush;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Maps a bool to one of two strings.</summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "Yes";
    public string FalseText { get; set; } = "No";
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? TrueText : FalseText;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
