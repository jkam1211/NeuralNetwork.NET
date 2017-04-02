﻿using System;
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

namespace DigitsRecognitionSample
{
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
            };
        }

        private void OnTopToggle_OnOnToggled(object sender, EventArgs e) => Topmost = !Topmost;

        private void MinimizeButton_Clicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void CloseButton_Clicked(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        #region Navigation

        /// <summary>
        /// Manages the navigation by making sure no double instances are created
        /// </summary>
        /// <typeparam name="T">The page to open</typeparam>
        private void NavigatePage<T>() where T : Window, new()
        {
            Window window = Application.Current.Windows.OfType<Window>().SingleOrDefault(x => x.GetType() == typeof(T));
            if (window == null)
            {
                new T { Left = this.Left + 25, Top = this.Top + 25 }.Show();
            }
            else
            {
                window.Focus();
            }
        }

        #endregion
    }
}
