﻿namespace Captura
{
    public partial class MainWindow : FirstFloor.ModernUI.Windows.Controls.ModernWindow//, INotifyPropertyChanged
    {
        public static MainWindow Instance { get; private set; }

        public MainWindow()
        {
            InitializeComponent();

            Instance = this;
        }
    }
}