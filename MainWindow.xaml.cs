using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Cursors = System.Windows.Input.Cursors;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace DesktopPager
{
    public partial class MainWindow : Window
    {
        private readonly PageManager _pageManager;
        private int _currentPage;

        public MainWindow(PageManager pageManager)
        {
            InitializeComponent();
            _pageManager = pageManager;
            LoadPages();
        }

        private void LoadPages()
        {
            _currentPage = _pageManager.GetCurrentPage();
            var pages = _pageManager.GetAvailablePages();

            PagesItemsControl.Items.Clear();

            foreach (int pageNum in pages)
            {
                var pageCard = CreatePageCard(pageNum, pageNum == _currentPage);
                PagesItemsControl.Items.Add(pageCard);
            }
        }

        private Border CreatePageCard(int pageNum, bool isActive)
        {
            string pageName = _pageManager.GetPageName(pageNum);
            var wallpaperConfig = _pageManager.GetWallpaperConfig(pageNum);

            // Main card border
            var card = new Border
            {
                Style = (Style)FindResource(isActive ? "ActivePageCardStyle" : "PageCardStyle"),
                Width = 240,
                Height = 260
            };

            // Apply wallpaper preview if available
            if (wallpaperConfig != null && wallpaperConfig.Type == "Image" && !string.IsNullOrEmpty(wallpaperConfig.Value))
            {
                try 
                {
                    if (System.IO.File.Exists(wallpaperConfig.Value))
                    {
                        var brush = new ImageBrush();
                        brush.ImageSource = new BitmapImage(new Uri(wallpaperConfig.Value));
                        brush.Stretch = Stretch.UniformToFill;
                        brush.Opacity = 0.5; // Dim it so text is visible
                        card.Background = brush;
                    }
                }
                catch { }
            }

            // Context Menu removed from card border - moved to Wallpaper Button
            card.ContextMenu = null;

            // Card content
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Active indicator
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Name
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Active indicator
            if (isActive)
            {
                var activeIndicator = new TextBlock
                {
                    Text = "✓ ACTIVE",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetRow(activeIndicator, 0);
                grid.Children.Add(activeIndicator);
            }

            // Page name
            var nameText = new TextBlock
            {
                Text = pageName,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(nameText, 1);
            grid.Children.Add(nameText);

            // Action Buttons Panel
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonsPanel, 2);

            // Rename Button
            var renameButton = new Button
            {
                Content = "✏️",
                Style = (Style)FindResource("ModernButtonStyle"),
                Tag = pageNum,
                ToolTip = "Rename Page",
                Margin = new Thickness(5, 0, 5, 0)
            };
            renameButton.Click += RenameButton_Click;
            buttonsPanel.Children.Add(renameButton);

            // Wallpaper Button (Menu)
            var wallpaperButton = new Button
            {
                Content = "🖼",
                Style = (Style)FindResource("ModernButtonStyle"),
                Tag = pageNum,
                ToolTip = "Wallpaper",
                Margin = new Thickness(5, 0, 5, 0)
            };
            
            // Context Menu for Wallpaper Button
            var wpMenu = new ContextMenu();
            
            var setImgItem = new MenuItem { Header = "Set Image..." };
            setImgItem.Click += (s, e) => SetWallpaperImage(pageNum);
            wpMenu.Items.Add(setImgItem);

            var setWeItem = new MenuItem { Header = "Set Wallpaper Engine ID..." };
            setWeItem.Click += (s, e) => SetWallpaperEngine(pageNum);
            wpMenu.Items.Add(setWeItem);

            wpMenu.Items.Add(new Separator());

            var applyAllItem = new MenuItem { Header = "Apply to All Pages" };
            applyAllItem.Click += (s, e) => ApplyWallpaperToAll(pageNum);
            wpMenu.Items.Add(applyAllItem);
            
            var removeItem = new MenuItem { Header = "Remove Wallpaper" };
            removeItem.Click += (s, e) => RemoveWallpaper(pageNum);
            wpMenu.Items.Add(removeItem);

            wallpaperButton.Click += (s, e) => { wallpaperButton.ContextMenu.IsOpen = true; };
            wallpaperButton.ContextMenu = wpMenu;

            buttonsPanel.Children.Add(wallpaperButton);

            // Delete Button
            var deleteButton = new Button
            {
                Content = "🗑️",
                ToolTip = "Delete Page",
                Style = (Style)FindResource("ModernButtonStyle"),
                Tag = pageNum,
                IsEnabled = _pageManager.GetAvailablePages().Count > 1,
                Foreground = Brushes.White,
                Margin = new Thickness(5, 0, 5, 0)
            };
            deleteButton.Click += DeleteButton_Click;
            buttonsPanel.Children.Add(deleteButton);

            grid.Children.Add(buttonsPanel);

            card.Child = grid;

            // Click to switch page
            if (!isActive)
            {
                card.MouseLeftButtonDown += (s, e) =>
                {
                    SwitchToPage(pageNum);
                };
            }

            return card;
        }

        private void SwitchToPage(int pageNum)
        {
            try
            {
                _pageManager.SwitchPage(pageNum);
                LoadPages(); // Refresh
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error switching page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveWallpaper(int pageNum)
        {
             try
             {
                 _pageManager.RemoveWallpaper(pageNum);
                 LoadPages();
             }
             catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
        }

        private void ApplyWallpaperToAll(int sourcePage)
        {
             try
             {
                 string pageName = _pageManager.GetPageName(sourcePage);
                 if (MessageBox.Show($"Apply wallpaper from '{pageName}' to ALL pages?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                 {
                     _pageManager.CopyWallpaperToAll(sourcePage);
                     LoadPages();
                 }
             }
             catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
        }

        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            int pageNum = (int)button!.Tag;
            string currentName = _pageManager.GetPageName(pageNum);

            var dialog = new InputDialog("Rename Page", $"Enter new name for {currentName}:", currentName)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
            {
                try
                {
                    _pageManager.RenamePage(pageNum, dialog.ResponseText);
                    LoadPages(); // Refresh
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error renaming page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            int pageNum = (int)button!.Tag;
            string pageName = _pageManager.GetPageName(pageNum);

            if (_pageManager.GetCurrentPage() == pageNum)
            {
                MessageBox.Show("You can't delete the active page", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{pageName}'?\n\nAll files on this page will be permanently deleted!",
                "Delete Page",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _pageManager.DeletePage(pageNum);
                    LoadPages(); // Refresh
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void NewPageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _pageManager.CreateNewPage();
                LoadPages(); // Refresh
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetWallpaperImage(int pageNum)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Select Wallpaper"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _pageManager.SetWallpaperForPage(pageNum, openFileDialog.FileName);
                    LoadPages(); // Refresh UI
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error setting wallpaper: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SetWallpaperEngine(int pageNum)
        {
            var dialog = new InputDialog("Wallpaper Engine", "Enter Workshop ID or File Path:");
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
            {
                try
                {
                    _pageManager.SetWallpaperEngineForPage(pageNum, dialog.ResponseText.Trim());
                    LoadPages(); // Refresh UI
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error setting wallpaper: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CheckSystemStatus();
        }

        private void CheckSystemStatus()
        {
            if (!_pageManager.IsJunctionActive)
            {
                string title = _pageManager.IsFirstRun ? "Enable Desktop Pager" : "Desktop Pager Inactive";
                string message = _pageManager.IsFirstRun 
                    ? "Welcome to Desktop Pager!\n\nWould you like to Enable paged desktops? This will convert your Desktop into a paged directory."
                    : "Desktop Pager is not managing your Desktop currently (Restored State).\n\nWould you like to Reactivate it? This will convert your Desktop into a paged directory again.";

                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Explicitly switch to current page to force junction creation
                        _pageManager.SwitchPage(_pageManager.GetCurrentPage());
                        LoadPages();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Activation failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        System.Windows.Application.Current.Shutdown();
                    }
                }
                else
                {
                    // User declined, close the app
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }
    }

    // Modern input dialog
    public class InputDialog : Window
    {
        private TextBox _textBox;
        public string ResponseText => _textBox.Text;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title = title;
            Width = 450;
            Height = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;

            // Main border with rounded corners
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid { Margin = new Thickness(25) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(titleText, 0);
            grid.Children.Add(titleText);

            // Prompt
            var promptText = new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 13
            };
            Grid.SetRow(promptText, 1);
            grid.Children.Add(promptText);

            // TextBox with modern style
            // TextBox with modern style
            _textBox = new TextBox
            {
                Text = defaultValue,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CaretBrush = Brushes.White,
                SelectionBrush = new SolidColorBrush(Color.FromRgb(70, 130, 255)),
                SelectionOpacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Rounded corners for textbox container
            var textBoxBorder = new Border
            {
                Child = _textBox,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 55)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 5, 0, 20),
                Height = 60,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(textBoxBorder, 2);
            grid.Children.Add(textBoxBorder);

            // Buttons panel
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // Cancel button
            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                IsCancel = true
            };
            
            var cancelTemplate = new ControlTemplate(typeof(Button));
            var cancelBorder = new FrameworkElementFactory(typeof(Border));
            cancelBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            cancelBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            cancelBorder.SetValue(Border.PaddingProperty, new Thickness(20, 8, 20, 8));
            var cancelPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            cancelPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cancelPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cancelBorder.AppendChild(cancelPresenter);
            cancelTemplate.VisualTree = cancelBorder;
            cancelButton.Template = cancelTemplate;
            
            cancelButton.MouseEnter += (s, e) => cancelButton.Background = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            cancelButton.MouseLeave += (s, e) => cancelButton.Background = new SolidColorBrush(Color.FromRgb(62, 62, 66));
            
            buttonsPanel.Children.Add(cancelButton);

            // OK button
            var okButton = new Button
            {
                Content = "OK",
                Width = 100,
                Height = 36,
                Background = new SolidColorBrush(Color.FromRgb(66, 133, 244)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                IsDefault = true
            };
            
            var okTemplate = new ControlTemplate(typeof(Button));
            var okBorder = new FrameworkElementFactory(typeof(Border));
            okBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            okBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            okBorder.SetValue(Border.PaddingProperty, new Thickness(20, 8, 20, 8));
            var okPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            okPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            okPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            okBorder.AppendChild(okPresenter);
            okTemplate.VisualTree = okBorder;
            okButton.Template = okTemplate;
            
            okButton.Click += (s, e) => DialogResult = true;
            okButton.MouseEnter += (s, e) => okButton.Background = new SolidColorBrush(Color.FromRgb(82, 149, 255));
            okButton.MouseLeave += (s, e) => okButton.Background = new SolidColorBrush(Color.FromRgb(66, 133, 244));
            
            buttonsPanel.Children.Add(okButton);

            Grid.SetRow(buttonsPanel, 3);
            grid.Children.Add(buttonsPanel);

            mainBorder.Child = grid;
            Content = mainBorder;
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            _textBox.SelectAll();
            _textBox.Focus();
        }
    }
}
