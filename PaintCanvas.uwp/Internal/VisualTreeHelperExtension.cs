using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Painting.Internal
{
    internal static class VisualTreeHelperExtension
    {
        public static IEnumerable<DependencyObject> GetVisualChildren(this DependencyObject reference)
        {
            var i = VisualTreeHelper.GetChildrenCount(reference);
            for (var k = 0; k < i; ++k)
            {
                yield return VisualTreeHelper.GetChild(reference, k);
            }
        }

        public static IEnumerable<T> GetVisualChildren<T>(this DependencyObject reference)
        {
            return GetVisualChildren(reference).OfType<T>();
        }
    }
}
