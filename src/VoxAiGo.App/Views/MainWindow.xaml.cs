using System.Windows;
using System.Windows.Input;
using VoxAiGo.App.ViewModels;

namespace VoxAiGo.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // PasswordBox doesn't support binding — wire manually
        PasswordBox.PasswordChanged += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.Password = PasswordBox.Password;
        };

        SignUpPasswordBox.PasswordChanged += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.Password = SignUpPasswordBox.Password;
        };

        // DevTools: show password prompt when version tapped 5x
        _viewModel.DevToolsPasswordRequested += () =>
        {
            var dialog = new Window
            {
                Title = "Dev Tools",
                Width = 320, Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 30, 30)),
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            var label = new System.Windows.Controls.TextBlock
            {
                Text = "Enter dev password:",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var passBox = new System.Windows.Controls.PasswordBox
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(45, 45, 45)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(80, 80, 80)),
                Padding = new Thickness(8, 6, 8, 6)
            };
            var btn = new System.Windows.Controls.Button
            {
                Content = "Unlock",
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(16, 6, 16, 6),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btn.Click += (_, _) =>
            {
                _viewModel.UnlockDevTools(passBox.Password);
                dialog.Close();
            };
            passBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    _viewModel.UnlockDevTools(passBox.Password);
                    dialog.Close();
                }
            };

            stack.Children.Add(label);
            stack.Children.Add(passBox);
            stack.Children.Add(btn);
            dialog.Content = stack;
            dialog.ShowDialog();
        };

        // Hide instead of close (tray app)
        Closing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    private void VersionLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.HandleVersionTap();
    }
}
