/**
 * Copyright @ 2008 Quan Nguyen
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *  http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace LingosolveStudio.Utilities
{
    class ImageIOHelper
    {
        /// <summary>
        /// Check if a PDF file should use lazy loading (large file)
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>True if the file is a large PDF that should use lazy loading</returns>
        public static bool ShouldUseLazyLoading(string filePath)
        {
            return LargePdfHandler.IsLargePdf(filePath);
        }

        /// <summary>
        /// Get a lazy-loading image list for large PDFs
        /// </summary>
        /// <param name="pdfFile">Path to the PDF file</param>
        /// <returns>A lazy-loading image list</returns>
        public static LazyPdfImageList GetLazyPdfImageList(string pdfFile)
        {
            return new LazyPdfImageList(pdfFile);
        }

        /// <summary>
        /// Get image(s) from file
        /// </summary>
        /// <param name="imageFile">file name</param>
        /// <returns>list of images</returns>
        public static IList<Image> GetImageList(FileInfo imageFile)
        {
            return GetImageList(imageFile, null);
        }

        /// <summary>
        /// Get image(s) from file with progress callback
        /// </summary>
        /// <param name="imageFile">file name</param>
        /// <param name="progressCallback">Optional callback for progress updates (currentPage, totalPages)</param>
        /// <returns>list of images</returns>
        public static IList<Image> GetImageList(FileInfo imageFile, Action<int, int> progressCallback)
        {
            string workingTiffFileName = null;

            Image image = null;

            try
            {
                // convert PDF to TIFF
                if (imageFile.Name.ToLower().EndsWith(".pdf"))
                {
                    // For large PDFs, convert page by page to avoid memory issues
                    if (imageFile.Length > LargePdfHandler.LargeFileThreshold)
                    {
                        return ConvertLargePdfToImages(imageFile.FullName, progressCallback);
                    }
                    
                    workingTiffFileName = PdfUtilities.ConvertPdf2TiffGS(imageFile.FullName);
                    if (workingTiffFileName == null)
                    {
                        throw new ApplicationException("Could not convert PDF file. The file may be too large or corrupted.");
                    }
                    imageFile = new FileInfo(workingTiffFileName);
                }

                // read in the image
                image = Image.FromFile(imageFile.FullName);

                IList<Image> images = new List<Image>();

                int count;
                if (image.RawFormat.Equals(ImageFormat.Gif))
                {
                    count = image.GetFrameCount(FrameDimension.Time);
                }
                else
                {
                    count = image.GetFrameCount(FrameDimension.Page);
                }

                for (int i = 0; i < count; i++)
                {
                    progressCallback?.Invoke(i, count);
                    
                    // save each frame to a bytestream
                    using (MemoryStream byteStream = new MemoryStream())
                    {
                        image.SelectActiveFrame(FrameDimension.Page, i);
                        image.Save(byteStream, ImageFormat.Png);

                        // and then create a new Image from it
                        images.Add(Image.FromStream(byteStream));
                    }
                }

                return images;
            }
            catch (OutOfMemoryException e)
            {
                throw new ApplicationException("Out of memory loading image. Try using a smaller file or enabling lazy loading for large PDFs.\n" + e.Message, e);
            }
            catch (System.Runtime.InteropServices.ExternalException e)
            {
                throw new ApplicationException(e.Message + "\nIt might have run out of memory due to handling too many images or too large a file.", e);
            }
            finally
            {
                if (image != null)
                {
                    image.Dispose();
                }

                if (workingTiffFileName != null && File.Exists(workingTiffFileName))
                {
                    try
                    {
                        File.Delete(workingTiffFileName);
                        // Also try to delete the parent temp directory if empty
                        string parentDir = Path.GetDirectoryName(workingTiffFileName);
                        if (Directory.Exists(parentDir) && Directory.GetFiles(parentDir).Length == 0)
                        {
                            Directory.Delete(parentDir);
                        }
                    }
                    catch { /* Ignore cleanup errors */ }
                }
            }
        }

        /// <summary>
        /// Convert a large PDF to images page by page to avoid memory issues
        /// </summary>
        private static IList<Image> ConvertLargePdfToImages(string pdfPath, Action<int, int> progressCallback)
        {
            IList<Image> images = new List<Image>();
            string tempDirectory = Path.Combine(Path.GetTempPath(), "LingosolveStudio_temp_" + Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));
            
            try
            {
                Directory.CreateDirectory(tempDirectory);
                
                // First, get the page count
                int pageCount = PdfUtilities.GetPdfPageCount(pdfPath);
                if (pageCount <= 0)
                {
                    throw new ApplicationException("Could not determine PDF page count");
                }

                // Convert pages in batches to manage memory
                int batchSize = 10; // Convert 10 pages at a time
                
                for (int startPage = 1; startPage <= pageCount; startPage += batchSize)
                {
                    int endPage = Math.Min(startPage + batchSize - 1, pageCount);
                    progressCallback?.Invoke(startPage - 1, pageCount);
                    
                    // Convert this batch of pages
                    string outputPattern = Path.Combine(tempDirectory, $"page_%04d.png");
                    
                    PDFConvert converter = new PDFConvert();
                    converter.GraphicsAlphaBit = 4;
                    converter.TextAlphaBit = 4;
                    converter.ResolutionX = 200; // Use moderate DPI for preview (saves memory)
                    converter.OutputFormat = "png16m";
                    converter.ThrowOnlyException = true;
                    converter.FirstPageToConvert = startPage;
                    converter.LastPageToConvert = endPage;
                    converter.OutputToMultipleFile = true;
                    
                    converter.Convert(pdfPath, outputPattern);
                    
                    // Load the converted pages
                    for (int page = startPage; page <= endPage; page++)
                    {
                        string pageFile = Path.Combine(tempDirectory, $"page_{page:D4}.png");
                        if (File.Exists(pageFile))
                        {
                            // Load image into memory and delete temp file immediately to save disk space
                            using (var fileStream = new FileStream(pageFile, FileMode.Open, FileAccess.Read))
                            {
                                using (var tempImage = Image.FromStream(fileStream))
                                {
                                    // Create a copy in memory so we can delete the file
                                    images.Add(new Bitmap(tempImage));
                                }
                            }
                            File.Delete(pageFile);
                        }
                        
                        progressCallback?.Invoke(page - 1, pageCount);
                    }
                    
                    // Force garbage collection after each batch to free memory
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                
                return images;
            }
            finally
            {
                // Clean up temp directory
                try
                {
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        /// <summary>
        /// Split multi-page TIFF.
        /// </summary>
        /// <param name="imageFile">input multi-page TIFF files</param>
        /// <returns>list of output TIFF files</returns>
        public static IList<string> SplitMultipageTiff(FileInfo imageFile)
        {
            //get the codec for tiff files
            ImageCodecInfo info = null;

            foreach (ImageCodecInfo ice in ImageCodecInfo.GetImageEncoders())
            {
                if (ice.MimeType == "image/tiff")
                {
                    info = ice;
                    break;
                }
            }

            EncoderParameters ep = new EncoderParameters(1);
            Encoder enc1 = Encoder.Compression;
            ep.Param[0] = new EncoderParameter(enc1, (long)EncoderValue.CompressionNone);

            Image image = null;

            try
            {
                // read in the image
                image = Image.FromFile(imageFile.FullName);

                IList<string> imagefiles = new List<string>();

                int count = image.GetFrameCount(FrameDimension.Page);
                
                for (int i = 0; i < count; i++)
                {
                    // save each frame to a file
                    image.SelectActiveFrame(FrameDimension.Page, i);
                    string filename = Path.GetTempPath() + Guid.NewGuid().ToString() + ".tif";
                    image.Save(filename, info, ep);
                    imagefiles.Add(filename);
                }

                return imagefiles;
            }
            catch (OutOfMemoryException e)
            {
                throw new ApplicationException(e.Message, e);
            }
            catch (System.Runtime.InteropServices.ExternalException e)
            {
                throw new ApplicationException(e.Message + "\nIt might have run out of memory due to handling too many images or too large a file.", e);
            }
            finally
            {
                if (image != null)
                {
                    image.Dispose();
                }
            }
        }

        /// <summary>
        /// Merge multiple images into one TIFF image.
        /// </summary>
        /// <param name="inputImages"></param>
        /// <param name="outputTiff"></param>
        public static void MergeTiff(string[] inputImages, string outputTiff)
        {
            //get the codec for tiff files
            ImageCodecInfo info = null;

            foreach (ImageCodecInfo ice in ImageCodecInfo.GetImageEncoders())
            {
                if (ice.MimeType == "image/tiff")
                {
                    info = ice;
                    break;
                }
            }

            //use the save encoder
            System.Drawing.Imaging.Encoder enc = System.Drawing.Imaging.Encoder.SaveFlag;
            EncoderParameters ep = new EncoderParameters(2);
            ep.Param[0] = new EncoderParameter(enc, (long)EncoderValue.MultiFrame);
            Encoder enc1 = Encoder.Compression;
            ep.Param[1] = new EncoderParameter(enc1, (long)EncoderValue.CompressionNone);
            Bitmap pages = null;

            try
            {
                int frame = 0;

                foreach (string inputImage in inputImages)
                {
                    if (frame == 0)
                    {
                        pages = (Bitmap)Image.FromFile(inputImage);
                        //save the first frame
                        pages.Save(outputTiff, info, ep);
                    }
                    else
                    {
                        //save the intermediate frames
                        ep.Param[0] = new EncoderParameter(enc, (long)EncoderValue.FrameDimensionPage);
                        Bitmap bm = null;
                        try
                        {
                            bm = (Bitmap)Image.FromFile(inputImage);
                            pages.SaveAdd(bm, ep);
                        }
                        catch (System.Runtime.InteropServices.ExternalException e)
                        {
                            throw new ApplicationException(e.Message + "\nIt might have run out of memory due to handling too many images or too large a file.", e);
                        }
                        finally
                        {
                            if (bm != null)
                            {
                                bm.Dispose();
                            }
                        }
                    }

                    if (frame == inputImages.Length - 1)
                    {
                        //flush and close
                        ep.Param[0] = new EncoderParameter(enc, (long)EncoderValue.Flush);
                        pages.SaveAdd(ep);
                    }
                    frame++;
                }
            }
            finally
            {
                if (pages != null)
                {
                    pages.Dispose();
                }
            }
        }
    }
}
