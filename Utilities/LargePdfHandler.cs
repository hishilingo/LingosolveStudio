/**
 * Copyright @ 2026 Hishiryo
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
using LingosolveStudio.Utilities;

namespace LingosolveStudio.Utilities
{
    /// <summary>
    /// Handles large PDF files (500MB+) with lazy page loading to avoid memory issues.
    /// Pages are converted on-demand rather than all at once.
    /// </summary>
    public class LargePdfHandler : IDisposable
    {
        private readonly string _pdfFilePath;
        private readonly string _tempDirectory;
        private readonly Dictionary<string, string> _convertedPagesByDpi; // key: "pageIndex_dpi"
        private readonly Dictionary<int, Image> _pageCache; // Strong references for loaded images
        private int _pageCount = -1;
        private bool _disposed = false;
        private volatile bool _isConverting = false; // Simple flag to track conversion state
        
        /// <summary>
        /// Threshold in bytes for considering a PDF as "large" (default 50MB)
        /// </summary>
        public static long LargeFileThreshold { get; set; } = 50 * 1024 * 1024;
        
        /// <summary>
        /// Maximum page count for full (non-lazy) loading. PDFs with more pages use lazy loading.
        /// </summary>
        public static int MaxPagesForFullLoad { get; set; } = 50;
        
        /// <summary>
        /// DPI for rendering PDF pages
        /// Preview: 72 DPI for fast loading and low memory (~100-200KB per page)
        /// OCR: 300 DPI for accurate text recognition (loaded on-demand)
        /// </summary>
        public int PreviewDpi { get; set; } = 72;
        public int OcrDpi { get; set; } = 300;
        
        /// <summary>
        /// Maximum number of pages to keep in memory cache
        /// </summary>
        public int MaxCachedPages { get; set; } = 10;

        /// <summary>
        /// Event raised during page conversion for progress reporting
        /// </summary>
        public event EventHandler<PageConversionProgressEventArgs> PageConversionProgress;

        public LargePdfHandler(string pdfFilePath)
        {
            if (!File.Exists(pdfFilePath))
                throw new FileNotFoundException("PDF file not found", pdfFilePath);
            
            _pdfFilePath = pdfFilePath;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "LingosolveStudio_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDirectory);
            _convertedPagesByDpi = new Dictionary<string, string>();
            _pageCache = new Dictionary<int, Image>();
        }

        /// <summary>
        /// Check if a file should be treated as a large PDF
        /// </summary>
        public static bool IsLargePdf(string filePath)
        {
            if (!filePath.ToLower().EndsWith(".pdf"))
                return false;
            
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Length > LargeFileThreshold;
        }

        /// <summary>
        /// Get the total page count of the PDF (cached after first call)
        /// </summary>
        public int PageCount
        {
            get
            {
                if (_pageCount < 0)
                {
                    _pageCount = PdfUtilities.GetPdfPageCount(_pdfFilePath);
                    
                    // If page count detection failed, estimate from file size (rough: ~200KB per page)
                    if (_pageCount <= 0)
                    {
                        var fileInfo = new FileInfo(_pdfFilePath);
                        _pageCount = Math.Max(1, (int)(fileInfo.Length / (200 * 1024)));
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not detect PDF page count, estimated {_pageCount} pages");
                    }
                }
                return _pageCount;
            }
        }

        /// <summary>
        /// Get a single page as an Image (lazy loading with caching)
        /// </summary>
        /// <param name="pageIndex">0-based page index</param>
        /// <param name="forOcr">If true, uses higher DPI for OCR quality</param>
        /// <returns>The page as an Image</returns>
        public Image GetPage(int pageIndex, bool forOcr = false)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LargePdfHandler));
                
            if (pageIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index cannot be negative");
            
            // Check cache first
            if (_pageCache.TryGetValue(pageIndex, out var cachedImage))
            {
                return cachedImage;
            }

            // Wait if another conversion is in progress (PDFConvert doesn't like concurrent calls)
            while (_isConverting)
            {
                System.Threading.Thread.Sleep(100);
                // Check cache again - another thread might have loaded this page
                if (_pageCache.TryGetValue(pageIndex, out cachedImage))
                {
                    return cachedImage;
                }
            }

            try
            {
                _isConverting = true;
                
                // Run conversion on a background thread to avoid UI thread conflicts with PDFConvert
                var task = System.Threading.Tasks.Task.Run(() => 
                {
                    // Convert the page if not already done
                    string pageFile = ConvertPage(pageIndex, forOcr ? OcrDpi : PreviewDpi);
                    
                    // Load the image into memory (don't keep file locked)
                    return LoadImageWithoutLock(pageFile);
                });
                
                // Wait for the task to complete
                Image image = task.GetAwaiter().GetResult();
                
                // Cache it
                ManageCache();
                _pageCache[pageIndex] = image;
                
                return image;
            }
            finally
            {
                _isConverting = false;
            }
        }
        
        /// <summary>
        /// Load image into memory without keeping the file locked
        /// </summary>
        private Image LoadImageWithoutLock(string filePath)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                using (var tempImage = Image.FromStream(fileStream))
                {
                    // Create a copy in memory so we don't keep the file locked
                    return new Bitmap(tempImage);
                }
            }
        }

        /// <summary>
        /// Convert a range of pages (useful for batch OCR)
        /// </summary>
        public IEnumerable<Image> GetPages(int startPage, int endPage, bool forOcr = false)
        {
            for (int i = startPage; i <= endPage && i < PageCount; i++)
            {
                yield return GetPage(i, forOcr);
                OnPageConversionProgress(i, endPage - startPage + 1);
            }
        }

        /// <summary>
        /// Convert a single page to PNG file
        /// </summary>
        /// <param name="pageIndex">0-based page index</param>
        /// <param name="dpi">Resolution (72 for preview, 300 for OCR)</param>
        /// <returns>Path to the converted PNG file</returns>
        private string ConvertPage(int pageIndex, int dpi)
        {
            int pageNum = pageIndex + 1; // GhostScript uses 1-based page numbers
            
            // Use different cache keys for different DPIs
            string cacheKey = $"{pageIndex}_{dpi}";
            
            // Check if already converted at this DPI
            if (_convertedPagesByDpi.TryGetValue(cacheKey, out string existingFile) && File.Exists(existingFile))
            {
                return existingFile;
            }

            string outputFile = Path.Combine(_tempDirectory, $"page_{pageIndex:D4}_{dpi}dpi.png");
            
            OnPageConversionProgress(pageIndex, PageCount);

            PDFConvert converter = new PDFConvert();
            converter.GraphicsAlphaBit = 4;
            converter.TextAlphaBit = 4;
            converter.ResolutionX = dpi;
            // Use grayscale for preview (smaller files), full color for OCR
            converter.OutputFormat = dpi <= 100 ? "pnggray" : "png16m";
            converter.ThrowOnlyException = true;
            converter.FirstPageToConvert = pageNum;
            converter.LastPageToConvert = pageNum;
            converter.OutputToMultipleFile = false;

            try
            {
                bool success = converter.Convert(_pdfFilePath, outputFile);
                if (success && File.Exists(outputFile))
                {
                    _convertedPagesByDpi[cacheKey] = outputFile;
                    return outputFile;
                }
                else
                {
                    throw new ApplicationException($"Failed to convert PDF page {pageNum}");
                }
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Error converting PDF page {pageNum}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Pre-convert pages in the background for faster access (at preview DPI)
        /// </summary>
        public void PreconvertPages(int startPage, int count)
        {
            int endPage = Math.Min(startPage + count, PageCount);
            for (int i = startPage; i < endPage; i++)
            {
                string cacheKey = $"{i}_{PreviewDpi}";
                if (!_convertedPagesByDpi.ContainsKey(cacheKey))
                {
                    try
                    {
                        ConvertPage(i, PreviewDpi);
                    }
                    catch
                    {
                        // Ignore errors during preconversion
                    }
                }
            }
        }
        
        /// <summary>
        /// Get a page at full OCR resolution (300 DPI) - for OCR processing only
        /// This is separate from the preview to avoid memory issues
        /// </summary>
        public Image GetPageForOcr(int pageIndex)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LargePdfHandler));
                
            if (pageIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index cannot be negative");
            
            // Wait if another conversion is in progress
            while (_isConverting)
            {
                System.Threading.Thread.Sleep(100);
            }
            
            try
            {
                _isConverting = true;
                
                // Run conversion on a background thread to avoid UI thread conflicts with PDFConvert
                var task = System.Threading.Tasks.Task.Run(() => 
                {
                    // Convert at OCR DPI (don't cache in _pageCache to save memory)
                    string pageFile = ConvertPage(pageIndex, OcrDpi);
                    return LoadImageWithoutLock(pageFile);
                });
                
                return task.GetAwaiter().GetResult();
            }
            finally
            {
                _isConverting = false;
            }
        }

        /// <summary>
        /// Clear old entries from cache to manage memory
        /// </summary>
        private void ManageCache()
        {
            // With strong references, just limit total count
            if (_pageCache.Count > MaxCachedPages)
            {
                try
                {
                    // Remove oldest entries (by page index, assuming sequential access)
                    var sortedKeys = new List<int>(_pageCache.Keys);
                    sortedKeys.Sort();
                    int removeCount = _pageCache.Count - MaxCachedPages / 2;
                    for (int i = 0; i < removeCount && i < sortedKeys.Count; i++)
                    {
                        var key = sortedKeys[i];
                        if (_pageCache.TryGetValue(key, out var img))
                        {
                            img?.Dispose();
                            _pageCache.Remove(key);
                        }
                    }
                }
                catch
                {
                    // Ignore cache cleanup errors
                }
            }
        }

        protected virtual void OnPageConversionProgress(int currentPage, int totalPages)
        {
            PageConversionProgress?.Invoke(this, new PageConversionProgressEventArgs(currentPage, totalPages));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose all cached images
                    foreach (var img in _pageCache.Values)
                    {
                        img?.Dispose();
                    }
                    _pageCache.Clear();
                    _convertedPagesByDpi.Clear();
                }

                // Clean up temp files
                try
                {
                    if (Directory.Exists(_tempDirectory))
                    {
                        Directory.Delete(_tempDirectory, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                _disposed = true;
            }
        }

        ~LargePdfHandler()
        {
            Dispose(false);
        }
    }

    public class PageConversionProgressEventArgs : EventArgs
    {
        public int CurrentPage { get; }
        public int TotalPages { get; }
        public double ProgressPercent => TotalPages > 0 ? (CurrentPage + 1) * 100.0 / TotalPages : 0;

        public PageConversionProgressEventArgs(int currentPage, int totalPages)
        {
            CurrentPage = currentPage;
            TotalPages = totalPages;
        }
    }

    /// <summary>
    /// A lazy-loading image list that wraps LargePdfHandler for seamless integration
    /// </summary>
    public class LazyPdfImageList : IList<Image>, IDisposable
    {
        private readonly LargePdfHandler _handler;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public LazyPdfImageList(string pdfFilePath)
        {
            _handler = new LargePdfHandler(pdfFilePath);
        }

        public event EventHandler<PageConversionProgressEventArgs> PageConversionProgress
        {
            add { _handler.PageConversionProgress += value; }
            remove { _handler.PageConversionProgress -= value; }
        }

        public Image this[int index]
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(LazyPdfImageList));
                    
                // LargePdfHandler already handles caching and thread safety
                return _handler.GetPage(index, false);
            }
            set
            {
                // Not supported for lazy PDF
                throw new NotSupportedException("Cannot set images in lazy PDF list");
            }
        }

        public int Count => _handler.PageCount;
        public bool IsReadOnly => true;

        public void Add(Image item) => throw new NotSupportedException("Cannot add to PDF image list");
        public void Clear() { } // No-op, handler manages its own cache
        public bool Contains(Image item) => false; // Not tracking individual images
        
        public void CopyTo(Image[] array, int arrayIndex)
        {
            for (int i = 0; i < Count && arrayIndex + i < array.Length; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        public int IndexOf(Image item) => -1; // Not tracking individual images

        public void Insert(int index, Image item) => throw new NotSupportedException("Cannot insert into PDF image list");
        public bool Remove(Image item) => throw new NotSupportedException("Cannot remove from PDF image list");
        public void RemoveAt(int index) => throw new NotSupportedException("Cannot remove from PDF image list");

        public IEnumerator<Image> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Get page with OCR quality (300 DPI) - for OCR processing
        /// Returns a NEW image that should be disposed after use
        /// </summary>
        public Image GetPageForOcr(int index)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LazyPdfImageList));
            return _handler.GetPageForOcr(index);
        }

        /// <summary>
        /// Preload next few pages for smoother navigation (at preview DPI)
        /// </summary>
        public void PreloadPages(int startPage, int count)
        {
            if (!_disposed)
                _handler.PreconvertPages(startPage, count);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _handler.Dispose();
            }
        }
    }
}
