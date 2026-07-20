using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopPager
{
    /// <summary>
    /// Generates custom icons for page numbers and application branding
    /// </summary>
    public static class IconGenerator
    {
        private const int IconSize = 256; // High resolution for better quality
        
        /// <summary>
        /// Generates a page number icon and returns the path to the .ico file
        /// </summary>
        public static string GeneratePageNumberIcon(int pageNumber, string iconsDirectory)
        {
            try
            {
                if (!Directory.Exists(iconsDirectory))
                {
                    Directory.CreateDirectory(iconsDirectory);
                }

                string iconPath = Path.Combine(iconsDirectory, $"page_{pageNumber}.ico");
                
                // Check if icon already exists (cache)
                if (File.Exists(iconPath))
                {
                    return iconPath;
                }

                // Create the icon
                using (Bitmap bitmap = CreatePageNumberBitmap(pageNumber))
                {
                    SaveAsIcon(bitmap, iconPath);
                }

                return iconPath;
            }
            catch (Exception ex)
            {
                // Log error and return empty string to fallback to default icon
                File.AppendAllText(
                    Path.Combine(iconsDirectory, "icon_errors.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Error generating icon for page {pageNumber}: {ex.Message}\n"
                );
                return string.Empty;
            }
        }

        /// <summary>
        /// Creates a bitmap with the page number rendered
        /// </summary>
        private static Bitmap CreatePageNumberBitmap(int pageNumber)
        {
            Bitmap bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
            
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                // Enable high quality rendering
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // Create gradient background (modern blue gradient)
                using (LinearGradientBrush gradientBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, IconSize, IconSize),
                    Color.FromArgb(255, 66, 133, 244),  // Google Blue
                    Color.FromArgb(255, 33, 150, 243),  // Material Blue
                    LinearGradientMode.ForwardDiagonal))
                {
                    // Draw rounded rectangle background
                    using (GraphicsPath path = CreateRoundedRectangle(0, 0, IconSize, IconSize, 40))
                    {
                        g.FillPath(gradientBrush, path);
                        
                        // Add subtle border
                        using (Pen borderPen = new Pen(Color.FromArgb(200, 255, 255, 255), 8))
                        {
                            g.DrawPath(borderPen, path);
                        }
                    }
                }

                // Draw the page number
                string text = pageNumber.ToString();
                
                // Adjust font size based on number of digits
                int fontSize = text.Length == 1 ? 140 : 
                               text.Length == 2 ? 110 : 
                               text.Length == 3 ? 85 : 60;

                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    // Measure text for centering
                    SizeF textSize = g.MeasureString(text, font);
                    float x = (IconSize - textSize.Width) / 2;
                    float y = (IconSize - textSize.Height) / 2;

                    // Draw text shadow for depth
                    using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    {
                        g.DrawString(text, font, shadowBrush, x + 4, y + 4);
                    }

                    // Draw main text
                    using (SolidBrush textBrush = new SolidBrush(Color.White))
                    {
                        g.DrawString(text, font, textBrush, x, y);
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Creates a rounded rectangle path
        /// </summary>
        private static GraphicsPath CreateRoundedRectangle(float x, float y, float width, float height, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(x, y, radius, radius, 180, 90);
            path.AddArc(x + width - radius, y, radius, radius, 270, 90);
            path.AddArc(x + width - radius, y + height - radius, radius, radius, 0, 90);
            path.AddArc(x, y + height - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Saves a bitmap as a multi-resolution .ico file
        /// </summary>
        private static void SaveAsIcon(Bitmap source, string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // Icon file header
                writer.Write((short)0);  // Reserved
                writer.Write((short)1);  // Type (1 = icon)
                writer.Write((short)4);  // Number of images (16, 32, 48, 256)

                // Create multiple sizes for better Windows compatibility
                int[] sizes = { 16, 32, 48, 256 };
                long[] imageOffsets = new long[sizes.Length];
                byte[][] imageData = new byte[sizes.Length][];

                // Prepare image data for each size
                for (int i = 0; i < sizes.Length; i++)
                {
                    using (Bitmap resized = new Bitmap(source, sizes[i], sizes[i]))
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            resized.Save(ms, ImageFormat.Png);
                            imageData[i] = ms.ToArray();
                        }
                    }
                }

                // Write directory entries
                long currentOffset = 6 + (16 * sizes.Length); // Header + directory entries
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); // Width (0 = 256)
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); // Height (0 = 256)
                    writer.Write((byte)0);  // Color palette
                    writer.Write((byte)0);  // Reserved
                    writer.Write((short)1); // Color planes
                    writer.Write((short)32); // Bits per pixel
                    writer.Write((int)imageData[i].Length); // Image data size
                    writer.Write((int)currentOffset); // Offset to image data
                    
                    imageOffsets[i] = currentOffset;
                    currentOffset += imageData[i].Length;
                }

                // Write image data
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write(imageData[i]);
                }
            }
        }

        /// <summary>
        /// Generates the main application icon
        /// </summary>
        public static string GenerateApplicationIcon(string iconsDirectory)
        {
            try
            {
                if (!Directory.Exists(iconsDirectory))
                {
                    Directory.CreateDirectory(iconsDirectory);
                }

                string iconPath = Path.Combine(iconsDirectory, "DesktopPager.ico");
                
                if (File.Exists(iconPath))
                {
                    return iconPath;
                }

                using (Bitmap bitmap = CreateApplicationIconBitmap())
                {
                    SaveAsIcon(bitmap, iconPath);
                }

                return iconPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Creates the application icon bitmap (layered pages design)
        /// </summary>
        private static Bitmap CreateApplicationIconBitmap()
        {
            Bitmap bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
            
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // Draw three stacked "pages" to represent the pager concept
                int pageWidth = 180;
                int pageHeight = 200;
                int offsetX = (IconSize - pageWidth) / 2;
                int offsetY = (IconSize - pageHeight) / 2 - 10;

                // Back page (darker)
                DrawPage(g, offsetX - 20, offsetY + 20, pageWidth, pageHeight, 
                    Color.FromArgb(255, 158, 158, 158), 15);

                // Middle page
                DrawPage(g, offsetX - 10, offsetY + 10, pageWidth, pageHeight, 
                    Color.FromArgb(255, 189, 189, 189), 20);

                // Front page (blue gradient)
                using (LinearGradientBrush gradientBrush = new LinearGradientBrush(
                    new Rectangle(offsetX, offsetY, pageWidth, pageHeight),
                    Color.FromArgb(255, 66, 133, 244),
                    Color.FromArgb(255, 33, 150, 243),
                    LinearGradientMode.Vertical))
                {
                    using (GraphicsPath path = CreateRoundedRectangle(offsetX, offsetY, pageWidth, pageHeight, 25))
                    {
                        g.FillPath(gradientBrush, path);
                        using (Pen borderPen = new Pen(Color.White, 6))
                        {
                            g.DrawPath(borderPen, path);
                        }
                    }
                }

                // Draw page indicator dots
                int dotY = offsetY + pageHeight - 40;
                int dotSpacing = 25;
                int startX = IconSize / 2 - dotSpacing;
                
                for (int i = 0; i < 3; i++)
                {
                    Color dotColor = i == 1 ? Color.White : Color.FromArgb(150, 255, 255, 255);
                    float dotSize = i == 1 ? 12 : 8;
                    using (SolidBrush dotBrush = new SolidBrush(dotColor))
                    {
                        g.FillEllipse(dotBrush, startX + (i * dotSpacing) - dotSize/2, dotY - dotSize/2, dotSize, dotSize);
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Helper to draw a page shape
        /// </summary>
        private static void DrawPage(Graphics g, int x, int y, int width, int height, Color color, float cornerRadius)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (GraphicsPath path = CreateRoundedRectangle(x, y, width, height, cornerRadius))
            {
                g.FillPath(brush, path);
                using (Pen borderPen = new Pen(Color.FromArgb(100, 0, 0, 0), 3))
                {
                    g.DrawPath(borderPen, path);
                }
            }
        }
    }
}
