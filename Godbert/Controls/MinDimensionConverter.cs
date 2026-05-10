using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Godbert.Controls
{
    public class MinDimensionConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 2 || !(values[0] is double nativeWidth) || !(values[1] is double viewWidth))
                return DependencyProperty.UnsetValue;

            // Returns the smaller of the two values
            double availableWidth = Math.Max(0, viewWidth - 15); // Subtract for scrollbar
            return Math.Min(nativeWidth, availableWidth);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
