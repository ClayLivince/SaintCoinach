using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Godbert.ViewModels;
using Godbert.ViewModels.Definition;

namespace Godbert.Views {
    public partial class DefinitionView : UserControl {
        public DefinitionView() {
            InitializeComponent();
        }

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject {
            while (start != null && !(start is T))
                start = VisualTreeHelper.GetParent(start);
            return start as T;
        }

        private void DefinitionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
            if (DataContext is DefinitionViewModel vm && vm.CurrentEditor != null) {
                vm.CurrentEditor.SelectedItem = e.NewValue as PositionedNodeViewModel;
            }
        }

        private void ConverterType_Changed(object sender, SelectionChangedEventArgs e) {
            if (sender is ComboBox combo && combo.Tag is SingleNodeViewModel single) {
                var selected = combo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected) || selected == "(none)") {
                    single.Converter = null;
                } else if (single.Converter?.TypeKey != selected) {
                    single.SetConverterByTypeKey(selected);
                }
            }
        }

        private void MultiRef_Add_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.DataContext is MultiRefConverterEditorViewModel vm) {
                vm.AddTarget();
            }
        }

        private void MultiRef_Remove_Click(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.Tag is MultiRefTarget target) {
                var items = FindAncestor<ItemsControl>(fe);
                if (items?.DataContext is MultiRefConverterEditorViewModel vm)
                    vm.RemoveTarget(target);
            }
        }
    }
}
