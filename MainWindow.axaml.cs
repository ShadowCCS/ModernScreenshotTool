using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModernScreenshotTool.Models;
using ModernScreenshotTool.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using System.IO;

namespace ModernScreenshotTool
{
    public partial class MainWindow : Window
    {
        private WriteableBitmap _capturedImage;
        private Rect _cropRect;
        private bool _isCropping = false;
        private Point _cropStart;
        private readonly List<Resolution> _availableResolutions = new List<Resolution>
        {
            new Resolution { Name = "4K", Width = 3840, Height = 2160 },
            new Resolution { Name = "2K", Width = 2560, Height = 1440 },
            new Resolution { Name = "1080p", Width = 1920, Height = 1080 },
            new Resolution { Name = "720p", Width = 1280, Height = 720 },
            new Resolution { Name = "Original", Width = 0, Height = 0 }
        };
        private readonly List<string> _availableFormats = new List<string> { "PNG", "JPEG", "BMP", "GIF" };
        private readonly ScreenshotService _screenshotService;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            _screenshotService = new ScreenshotService();
            SetupUI();
            SetupEvents();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        private void LeftMouseDragDown(object sender, PointerPressedEventArgs e)
        {
            this.BeginMoveDrag(e);
        }

        private void SetupUI()
        {
            var resolutionComboBox = this.FindControl<ComboBox>("ResolutionComboBox");
            resolutionComboBox.ItemsSource = _availableResolutions;
            resolutionComboBox.SelectedIndex = 4; // Default to Original

            var formatComboBox = this.FindControl<ComboBox>("FormatComboBox");
            formatComboBox.ItemsSource = _availableFormats;
            formatComboBox.SelectedIndex = 0; // Default to PNG
        }

        private void SetupEvents()
        {
            // Button events
            var captureButton = this.FindControl<Button>("CaptureButton");
            captureButton.Click += async (sender, e) => await CaptureScreenshotAsync();

            var cropButton = this.FindControl<Button>("CropButton");
            cropButton.Click += (sender, e) => ToggleCropMode();

            var applyButton = this.FindControl<Button>("ApplyButton");
            applyButton.Click += async (sender, e) => await ApplyChangesAsync();

            var saveButton = this.FindControl<Button>("SaveButton");
            saveButton.Click += async (sender, e) => await SaveImageAsync();

            var copyButton = this.FindControl<Button>("CopyButton");
            copyButton.Click += async (sender, e) => await CopyToClipboardAsync();

            // Close button event
            var closeButton = this.FindControl<Button>("CloseButton");
            closeButton.Click += CloseButton_Click;

            // Canvas events for cropping
            var canvas = this.FindControl<Canvas>("ImageCanvas");
            canvas.PointerPressed += Canvas_PointerPressed;
            canvas.PointerMoved += Canvas_PointerMoved;
            canvas.PointerReleased += Canvas_PointerReleased;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async Task CaptureScreenshotAsync()
        {
            try
            {
                // Hide window temporarily
                this.WindowState = WindowState.Minimized;
                await Task.Delay(300); // Give time for the window to minimize

                // Capture the screen
                _capturedImage = await _screenshotService.CaptureScreenAsync();

                // Display the captured image
                var displayImage = this.FindControl<Image>("DisplayImage");
                displayImage.Source = _capturedImage;

                // Restore window
                this.WindowState = WindowState.Normal;
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Error capturing screenshot: {ex.Message}");
                this.WindowState = WindowState.Normal;
            }
        }

        private void ToggleCropMode()
        {
            if (_capturedImage == null)
            {
                _ = ShowInfoAsync("Please capture an image first.");
                return;
            }

            _isCropping = !_isCropping;
            if (_isCropping)
            {
                _cropRect = new Rect();
                this.Cursor = new Cursor(StandardCursorType.Cross);
            }
            else
            {
                this.Cursor = new Cursor(StandardCursorType.Arrow);
            }
        }

        private void Canvas_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isCropping)
            {
                _cropStart = e.GetPosition((Canvas)sender);
                _cropRect = new Rect(_cropStart, new Size(0, 0));
                DrawCropRectangle();
            }
        }

        private void Canvas_PointerMoved(object sender, PointerEventArgs e)
        {
            if (_isCropping && e.GetCurrentPoint((Canvas)sender).Properties.IsLeftButtonPressed)
            {
                var currentPos = e.GetPosition((Canvas)sender);
                double width = Math.Abs(currentPos.X - _cropStart.X);
                double height = Math.Abs(currentPos.Y - _cropStart.Y);
                double x = Math.Min(_cropStart.X, currentPos.X);
                double y = Math.Min(_cropStart.Y, currentPos.Y);

                _cropRect = new Rect(x, y, width, height);
                DrawCropRectangle();
            }
        }

        private void Canvas_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_isCropping && _cropRect.Width > 0 && _cropRect.Height > 0)
            {
                _isCropping = false;
                this.Cursor = new Cursor(StandardCursorType.Arrow);
                // Keep the rectangle visible until Apply Changes is clicked
            }
        }

        private void DrawCropRectangle()
        {
            var canvas = this.FindControl<Canvas>("ImageCanvas");
            canvas.Children.Clear();

            if (_cropRect.Width > 0 && _cropRect.Height > 0)
            {
                var rectangle = new Avalonia.Controls.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(Colors.DodgerBlue),
                    StrokeThickness = 2,
                    Width = _cropRect.Width,
                    Height = _cropRect.Height
                };

                Canvas.SetLeft(rectangle, _cropRect.X);
                Canvas.SetTop(rectangle, _cropRect.Y);
                canvas.Children.Add(rectangle);
            }
        }

        private async Task ApplyChangesAsync()
        {
            if (_capturedImage == null)
            {
                await ShowInfoAsync("Please capture an image first.");
                return;
            }

            try
            {
                // First apply crop if needed
                if (_cropRect.Width > 0 && _cropRect.Height > 0)
                {
                    _capturedImage = _screenshotService.CropImage(_capturedImage, _cropRect);
                    _cropRect = new Rect();
                    var canvas = this.FindControl<Canvas>("ImageCanvas");
                    canvas.Children.Clear();
                }

                // Then apply resolution change if needed
                var resolutionComboBox = this.FindControl<ComboBox>("ResolutionComboBox");
                var selectedResolution = resolutionComboBox.SelectedItem as Resolution;

                if (selectedResolution != null && selectedResolution.Width > 0 && selectedResolution.Height > 0)
                {
                    _capturedImage = _screenshotService.ResizeImage(
                        _capturedImage,
                        selectedResolution.Width,
                        selectedResolution.Height
                    );
                }

                // Update display
                var displayImage = this.FindControl<Image>("DisplayImage");
                displayImage.Source = _capturedImage;
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Error applying changes: {ex.Message}");
            }
        }

        private async Task SaveImageAsync()
        {
            if (_capturedImage == null)
            {
                await ShowInfoAsync("Please capture an image first.");
                return;
            }

            try
            {
                var formatComboBox = this.FindControl<ComboBox>("FormatComboBox");
                var format = formatComboBox.SelectedItem.ToString();
                var extension = format.ToLower();

                var saveDialog = new SaveFileDialog
                {
                    Title = "Save Screenshot",
                    InitialFileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}"
                };

                // Set file type filters
                saveDialog.Filters.Add(new FileDialogFilter
                {
                    Name = $"{format} Image",
                    Extensions = new List<string> { extension }
                });

                var result = await saveDialog.ShowAsync(this);
                if (!string.IsNullOrEmpty(result))
                {
                    await _screenshotService.SaveImageToFileAsync(_capturedImage, result, format);
                    await ShowSuccessDialogAsync("Image saved successfully!");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Error saving image: {ex.Message}");
            }
        }

        private async Task CopyToClipboardAsync()
        {
            if (_capturedImage == null)
            {
                await ShowInfoAsync("Please capture an image first.");
                return;
            }

            try
            {
                // Get the top level from the current window
                if (TopLevel.GetTopLevel(this) is TopLevel topLevel)
                {
                    // Get clipboard instance
                    var clipboard = topLevel.Clipboard;
                    
                    // Create a data object with the image
                    using (var memoryStream = new MemoryStream())
                    {
                        // Convert to PNG format for clipboard
                        var format = SKEncodedImageFormat.Png;
                        
                        // Create a temporary SKBitmap from the WriteableBitmap
                        var skBitmap = new SKBitmap(_capturedImage.PixelSize.Width, _capturedImage.PixelSize.Height);
                        using (var bitmapLock = _capturedImage.Lock())
                        {
                            // Copy pixel data
                            IntPtr destPtr = skBitmap.GetPixels();
                            unsafe
                            {
                                Buffer.MemoryCopy(
                                    bitmapLock.Address.ToPointer(),
                                    destPtr.ToPointer(),
                                    skBitmap.ByteCount,
                                    bitmapLock.RowBytes * _capturedImage.PixelSize.Height);
                            }
                        }

                        // Create an image and encode to PNG
                        using (var image = SKImage.FromBitmap(skBitmap))
                        using (var data = image.Encode(format, 100))
                        {
                            // Write to memory stream
                            data.SaveTo(memoryStream);
                            memoryStream.Position = 0;
                        }

                        // Create a data object
                        var dataObject = new DataObject();
                        // Set the file format
                        dataObject.Set(DataFormats.Files, new[] { "temp.png" });
                        // Set the PNG data
                        dataObject.Set("PNG", memoryStream);

                        // Set the data object to clipboard
                        await clipboard.SetDataObjectAsync(dataObject);

                    }

                    await ShowSuccessDialogAsync("Image copied to clipboard!");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Error copying to clipboard: {ex.Message}");
            }
        }

        private async Task ShowInfoDialogAsync(string message)
        {
            var dialog = new Window
            {
                Title = "Information",
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Width = 300
                        },
                        new Button
                        {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Margin = new Thickness(0, 15, 0, 0)
                        }
                    }
                },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var button = (dialog.Content as StackPanel).Children.OfType<Button>().First();
            button.Click += (s, e) => dialog.Close();

            await dialog.ShowDialog(this);
        }

        private async Task ShowInfoAsync(string message)
        {
            await ShowInfoDialogAsync(message);
        }

        private async Task ShowErrorDialogAsync(string message)
        {
            var dialog = new Window
            {
                Title = "Error",
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Width = 300
                        },
                        new Button
                        {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Margin = new Thickness(0, 15, 0, 0)
                        }
                    }
                },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var button = (dialog.Content as StackPanel).Children.OfType<Button>().First();
            button.Click += (s, e) => dialog.Close();

            await dialog.ShowDialog(this);
        }

        private async Task ShowSuccessDialogAsync(string message)
        {
            var dialog = new Window
            {
                Title = "Success",
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Width = 300
                        },
                        new Button
                        {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Margin = new Thickness(0, 15, 0, 0)
                        }
                    }
                },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var button = (dialog.Content as StackPanel).Children.OfType<Button>().First();
            button.Click += (s, e) => dialog.Close();

            await dialog.ShowDialog(this);
        }
    }
}