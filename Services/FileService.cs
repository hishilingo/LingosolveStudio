using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using LingosolveStudio.Utilities;

namespace LingosolveStudio.Services
{
    /// <summary>
    /// Handles file operations: opening images/PDFs, saving text output.
    /// </summary>
    public class FileService
    {
        public const string IMAGE_FILTER = "All Image Files|*.bmp;*.gif;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.pdf|" +
            "BMP (*.bmp)|*.bmp|GIF (*.gif)|*.gif|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
            "PNG (*.png)|*.png|TIFF (*.tif;*.tiff)|*.tif;*.tiff|PDF (*.pdf)|*.pdf|All Files (*.*)|*.*";

        public const string TEXT_FILTER = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";

        /// <summary>
        /// Loads images from a file. For multi-page TIFF/PDF, returns all pages.
        /// </summary>
        public List<Image> LoadImages(string filePath)
        {
            var images = new List<Image>();
            string ext = Path.GetExtension(filePath).ToLower();

            if (ext == ".pdf")
            {
                images = LoadPdfImages(filePath);
            }
            else if (ext == ".tif" || ext == ".tiff")
            {
                images = LoadTiffImages(filePath);
            }
            else
            {
                images.Add(Image.FromFile(filePath));
            }

            return images;
        }

        private List<Image> LoadTiffImages(string filePath)
        {
            var images = new List<Image>();
            try
            {
                var tiffImage = Image.FromFile(filePath);
                int frameCount = tiffImage.GetFrameCount(System.Drawing.Imaging.FrameDimension.Page);

                for (int i = 0; i < frameCount; i++)
                {
                    tiffImage.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Page, i);
                    images.Add((Image)tiffImage.Clone());
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading TIFF: {ex.Message}", ex);
            }
            return images;
        }

        private List<Image> LoadPdfImages(string filePath)
        {
            var images = new List<Image>();
            try
            {
                var result = ImageIOHelper.GetImageList(new FileInfo(filePath));
                images.AddRange(result);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading PDF: {ex.Message}", ex);
            }
            return images;
        }

        public void SaveTextToFile(string text, string filePath)
        {
            File.WriteAllText(filePath, text);
        }

        public string GenerateTimestampedFilename(string prefix, string directory)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(directory, $"{prefix}_{timestamp}.txt");
        }
    }
}
