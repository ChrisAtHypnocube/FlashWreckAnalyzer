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

namespace FlashWreckAnalyzer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = (DataContext as ViewModel);
            if (vm != null) 
                vm.Load();
        }

        private async void MakeStatsClicked(object sender, RoutedEventArgs e)
        {
            var vm = (DataContext as ViewModel);
            if (vm != null)
            {
                var statName = await vm.MakeStatsAsync();
                MessageBox.Show("stats file saved: " + statName);
            }
        }

        private void RenderClicked(object sender, RoutedEventArgs e)
        {
            var vm = (DataContext as ViewModel);
            if (vm != null)
                vm.RenderFrames();

        }
    }
}
