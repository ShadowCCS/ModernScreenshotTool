using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SkiaSharp;

namespace ModernScreenshotTool.Services
{
    public class ScreenshotService
    {
        public async Task<WriteableBitmap> CaptureScreenAsync()
        {
            return await Task.Run(() =>
            {
                // Platform-specific native screen capture
                using (var nativeScreenshot = CaptureNativeScreenshot())
                {
                    // Convert to Avalonia WriteableBitmap
                    return ConvertToWriteableBitmap(nativeScreenshot);
                }
            });
        }

        private SKBitmap CaptureNativeScreenshot()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return CaptureWindowsScreen();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return CaptureLinuxScreen();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return CaptureMacScreen();
            }
            else
            {
                throw new PlatformNotSupportedException("Screenshot capture not implemented for this platform");
            }
        }

        private SKBitmap CaptureWindowsScreen()
        {
            // Windows screen capture using P/Invoke
            var screenWidth = GetSystemMetrics(0); // SM_CXSCREEN
            var screenHeight = GetSystemMetrics(1); // SM_CYSCREEN

            // Create bitmap
            var bitmap = new SKBitmap(screenWidth, screenHeight);

            // Get device context
            IntPtr screenDC = GetDC(IntPtr.Zero);
            IntPtr memoryDC = CreateCompatibleDC(screenDC);
            IntPtr hBitmap = CreateCompatibleBitmap(screenDC, screenWidth, screenHeight);
            IntPtr oldBitmap = SelectObject(memoryDC, hBitmap);

            // Copy screen to bitmap
            BitBlt(memoryDC, 0, 0, screenWidth, screenHeight, screenDC, 0, 0, 0x00CC0020); // SRCCOPY

            // Copy bitmap data to SKBitmap
            var info = new SKImageInfo(screenWidth, screenHeight);
            IntPtr pBits = bitmap.GetPixels();

            // Transfer bitmap data
            GetBitmapBits(hBitmap, bitmap.ByteCount, pBits);

            // Clean up
            SelectObject(memoryDC, oldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(memoryDC);
            ReleaseDC(IntPtr.Zero, screenDC);

            return bitmap;
        }

        private SKBitmap CaptureLinuxScreen()
        {
            // Placeholder for Linux implementation
            throw new NotImplementedException("Linux screenshot capture not yet implemented");
        }

        private SKBitmap CaptureMacScreen()
        {
            // Placeholder for macOS implementation
            throw new NotImplementedException("macOS screenshot capture not yet implemented");
        }

        private WriteableBitmap ConvertToWriteableBitmap(SKBitmap skBitmap)
        {
            // Create WriteableBitmap with same dimensions
            var writeableBitmap = new WriteableBitmap(
                new PixelSize(skBitmap.Width, skBitmap.Height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888);

            // Copy pixel data
            using (var framebuffer = writeableBitmap.Lock())
            {
                IntPtr sourcePtr = skBitmap.GetPixels();
                unsafe
                {
                    Buffer.MemoryCopy(
                        sourcePtr.ToPointer(),
                        framebuffer.Address.ToPointer(),
                        framebuffer.RowBytes * skBitmap.Height,
                        skBitmap.ByteCount);
                }
            }

            return writeableBitmap;
        }

        public WriteableBitmap ResizeImage(WriteableBitmap originalBitmap, int width, int height)
        {
            // Create a new bitmap with the target size
            var resizedBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888);

            // Use Skia for high-quality resizing
            using (var originalSurface = SKSurface.Create(
                new SKImageInfo(originalBitmap.PixelSize.Width, originalBitmap.PixelSize.Height),
                originalBitmap.Lock().Address))
            {
                using (var resizedSurface = SKSurface.Create(
                    new SKImageInfo(width, height)))
                {
                    var canvas = resizedSurface.Canvas;
                    var image = originalSurface.Snapshot();
                    canvas.DrawImage(image,
                        new SKRect(0, 0, width, height),
                        new SKPaint { FilterQuality = SKFilterQuality.High });

                    // Copy the result to the writeableBitmap
                    using (var resizedBitmapLock = resizedBitmap.Lock())
                    {
                        resizedSurface.ReadPixels(
                            new SKImageInfo(width, height),
                            resizedBitmapLock.Address,
                            resizedBitmapLock.RowBytes,
                            0, // srcX
                            0  // srcY
                        );

                    }

                    return resizedBitmap;
                }
            }
        }

        public WriteableBitmap CropImage(WriteableBitmap originalBitmap, Rect cropRect)
        {
            // Ensure crop rectangle is within bounds
            int x = Math.Max(0, (int)cropRect.X);
            int y = Math.Max(0, (int)cropRect.Y);
            int width = Math.Min(originalBitmap.PixelSize.Width - x, (int)cropRect.Width);
            int height = Math.Min(originalBitmap.PixelSize.Height - y, (int)cropRect.Height);

            if (width <= 0 || height <= 0)
                return originalBitmap;

            var croppedBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888);

            // Use SKia for high-quality cropping
            using (var originalLock = originalBitmap.Lock())
            {
                using (var croppedLock = croppedBitmap.Lock())
                {
                    // Create source bitmap
                    var sourceBitmap = new SKBitmap();
                    sourceBitmap.InstallPixels(
                        new SKImageInfo(originalBitmap.PixelSize.Width, originalBitmap.PixelSize.Height),
                        originalLock.Address,
                        originalLock.RowBytes);

                    // Create cropped bitmap
                    var croppedSkBitmap = new SKBitmap(width, height);

                    // Extract the region
                    sourceBitmap.ExtractSubset(croppedSkBitmap, new SKRectI(x, y, x + width, y + height));

                    // Copy the cropped bitmap to the destination
                    IntPtr sourcePtr = croppedSkBitmap.GetPixels();
                    unsafe
                    {
                        Buffer.MemoryCopy(
                            sourcePtr.ToPointer(),
                            croppedLock.Address.ToPointer(),
                            croppedLock.RowBytes * height,
                            croppedSkBitmap.ByteCount);
                    }
                }
            }

            return croppedBitmap;
        }

        public async Task SaveImageToFileAsync(WriteableBitmap bitmap, string filePath, string format)
        {
            await Task.Run(() =>
            {
                using (var fileStream = File.Create(filePath))
                {
                    // Convert WriteableBitmap to SKBitmap
                    var skBitmap = new SKBitmap(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                    using (var bitmapLock = bitmap.Lock())
                    {
                        // Copy pixel data
                        IntPtr destPtr = skBitmap.GetPixels();
                        unsafe
                        {
                            Buffer.MemoryCopy(
                                bitmapLock.Address.ToPointer(),
                                destPtr.ToPointer(),
                                skBitmap.ByteCount,
                                bitmapLock.RowBytes * bitmap.PixelSize.Height);
                        }
                    }

                    // Encode and save
                    SKEncodedImageFormat imageFormat;
                    int quality = 100;

                    switch (format.ToLower())
                    {
                        case "png":
                            imageFormat = SKEncodedImageFormat.Png;
                            break;
                        case "jpg":
                        case "jpeg":
                            imageFormat = SKEncodedImageFormat.Jpeg;
                            quality = 90;
                            break;
                        case "bmp":
                            imageFormat = SKEncodedImageFormat.Bmp;
                            break;
                        case "gif":
                            imageFormat = SKEncodedImageFormat.Gif;
                            break;
                        default:
                            imageFormat = SKEncodedImageFormat.Png;
                            break;
                    }

                    using (var image = SKImage.FromBitmap(skBitmap))
                    using (var data = image.Encode(imageFormat, quality))
                    {
                        data.SaveTo(fileStream);
                    }
                }
            });
        }

        public async Task CopyToClipboardAsync(WriteableBitmap bitmap)
        {
            // Implement clipboard functionality based on platform
            await Task.Run(() =>
            {
                // Clipboard functionality would be implemented here
                // Different for each platform

                // This is just a placeholder as Avalonia's clipboard implementation
                // varies by platform
            });
        }

        #region Windows Native Methods
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern int GetBitmapBits(IntPtr hbmp, int cbBuffer, IntPtr lpvBits);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        #endregion
    }
}