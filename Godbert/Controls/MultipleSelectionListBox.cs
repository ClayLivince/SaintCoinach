using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Godbert.Controls {
    // Source - https://stackoverflow.com/a
    // Posted by redcurry
    // Retrieved 2026-01-26, License - CC BY-SA 4.0
    // P.S.: I have to say, MVVM mode is just shit!

    public class MultipleSelectionListBox : ListBox {
        public static readonly DependencyProperty BindableSelectedItemsProperty =
            DependencyProperty.Register("BindableSelectedItems",
                typeof(IList), typeof(MultipleSelectionListBox),
                new FrameworkPropertyMetadata(default(IList),
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindableSelectedItemsChanged));

        public IList BindableSelectedItems {
            get => (IList)GetValue(BindableSelectedItemsProperty);
            set => SetValue(BindableSelectedItemsProperty, value);
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e) {
            base.OnSelectionChanged(e);
            BindableSelectedItems = SelectedItems;
        }

        private static void OnBindableSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is MultipleSelectionListBox listBox)
                listBox.SetSelectedItems(listBox.BindableSelectedItems);
        }
    }

}
