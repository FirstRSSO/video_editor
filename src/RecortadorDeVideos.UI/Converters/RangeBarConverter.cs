using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RecortadorDeVideos.UI.Converters;

/// <summary>
/// Proyecta el rango [I - O] sobre el ancho real de la tira de tiempo.
/// Devuelve un Thickness pensado para el Margin de un elemento estirado
/// horizontalmente: el borde izquierdo cae sobre [I] y el derecho sobre [O].
/// </summary>
/// <remarks>
/// Entradas esperadas, en este orden:
/// 0) TimeSpan inicio, 1) TimeSpan fin, 2) TimeSpan duración total, 3) double ancho disponible.
/// Cuando el rango no es representable (sin video, duración cero, fin &lt;= inicio)
/// colapsa el elemento contra el borde izquierdo en lugar de ocupar toda la tira.
/// </remarks>
public sealed class RangeBarConverter : IMultiValueConverter
{
    // Un margen derecho desmedido deja el elemento en ancho cero sin romper el layout.
    private static readonly Thickness Collapsed = new(0, 0, 100000, 0);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4)
            return Collapsed;

        if (values[0] is not TimeSpan start ||
            values[1] is not TimeSpan end ||
            values[2] is not TimeSpan total ||
            values[3] is not double width)
            return Collapsed;

        if (total <= TimeSpan.Zero || width <= 0 || end <= start)
            return CollapseAt(width);

        var totalSeconds = total.TotalSeconds;
        var left = Math.Clamp(start.TotalSeconds / totalSeconds, 0.0, 1.0) * width;
        var right = Math.Clamp(end.TotalSeconds / totalSeconds, 0.0, 1.0) * width;

        // Un rango muy corto seguiría siendo visible: mínimo dos píxeles de mecha.
        if (right - left < 2.0)
            right = Math.Min(width, left + 2.0);

        return new Thickness(left, 0, width - right, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException("RangeBarConverter solo proyecta hacia la vista.");

    private static Thickness CollapseAt(double width)
        => width > 0 ? new Thickness(0, 0, width + 1, 0) : Collapsed;
}
