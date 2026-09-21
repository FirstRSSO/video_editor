using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RecortadorDeVideos.UI.Converters;

/// <summary>
/// Enlaza un preajuste de velocidad con el multiplicador activo: marca el
/// preajuste cuando coinciden y lo aplica cuando el usuario lo elige.
/// </summary>
/// <remarks>
/// El parámetro es el valor del preajuste escrito en cultura invariante
/// ("1.75"), de modo que el XAML no dependa de la configuración regional.
/// Cuando el multiplicador viene del deslizador continuo y no cae sobre
/// ningún preajuste, ninguno queda marcado, que es lo correcto.
/// </remarks>
public sealed class SpeedPresetConverter : IValueConverter
{
    private const double Tolerance = 0.001;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double current && TryParsePreset(parameter, out var preset))
            return Math.Abs(current - preset) < Tolerance;

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Desmarcar un preajuste no significa nada: solo interesa el que se elige.
        if (value is bool isChecked && isChecked && TryParsePreset(parameter, out var preset))
            return preset;

        return Binding.DoNothing;
    }

    private static bool TryParsePreset(object parameter, out double preset)
        => double.TryParse(parameter?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out preset);
}
