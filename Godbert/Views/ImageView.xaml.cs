using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Godbert.Views {
    /// <summary>
    /// ImageView.xaml 的交互逻辑
    /// </summary>
    public partial class ImageView : UserControl {
        public ImageView() {
            InitializeComponent();
        }

        private void IconSetSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (IconGallery.Items.Count > 0) 
                IconGallery.ScrollIntoView(IconGallery.Items[0]);
        }

        private void PatchSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (IconSets.Items.Count > 0)
                IconSets.SelectedItem = IconSets.Items[0];
        }
    }
}
