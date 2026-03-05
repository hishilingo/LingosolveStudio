using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using LingosolveStudio.Dialogs;
using LingosolveStudio.Services;
using LingosolveStudio.Utilities;

namespace LingosolveStudio
{
    public partial class MainWindow : Window
    {
        private const string APP_NAME = "Lingosolve Studio 1.0";
        private const float ZOOM_FACTOR = 1.25f;

        // Services
        private readonly FileService fileService = new FileService();
        private readonly ImageProcessingService imageProcessor = new ImageProcessingService();
        private AISettings aiSettings;
        private OpenRouterService openRouterService;
        private TokenUsageStats tokenStats = new TokenUsageStats();

        // Image state
        private List<System.Drawing.Image> imageList = new List<System.Drawing.Image>();
        private int imageIndex;
        private float scaleX = 1f, scaleY = 1f;
        private bool isFitImageSelected;
        private FixedSizeStack<System.Drawing.Image> undoStack = new FixedSizeStack<System.Drawing.Image>(10);

        // Large PDF lazy loading
        private LargePdfHandler largePdfHandler;
        private bool isLargePdfMode;

        // Background workers
        private BackgroundWorker bgWorkerLoad;
        private BackgroundWorker bgWorkerAiOcr;
        private bool isAiOcrRunning;

        // Current file
        private string currentFilePath;
        private readonly string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        public MainWindow()
        {
            InitializeComponent();

            bgWorkerLoad = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            bgWorkerLoad.DoWork += BgWorkerLoad_DoWork;
            bgWorkerLoad.ProgressChanged += BgWorkerLoad_ProgressChanged;
            bgWorkerLoad.RunWorkerCompleted += BgWorkerLoad_RunWorkerCompleted;

            bgWorkerAiOcr = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            bgWorkerAiOcr.DoWork += BgWorkerAiOcr_DoWork;
            bgWorkerAiOcr.ProgressChanged += BgWorkerAiOcr_ProgressChanged;
            bgWorkerAiOcr.RunWorkerCompleted += BgWorkerAiOcr_RunWorkerCompleted;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            aiSettings = AISettings.Load();
            UpdateTranslationPanelVisibility();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (isAiOcrRunning && bgWorkerAiOcr.IsBusy)
                bgWorkerAiOcr.CancelAsync();
            if (bgWorkerLoad.IsBusy)
                bgWorkerLoad.CancelAsync();
            largePdfHandler?.Dispose();
            largePdfHandler = null;
        }

        private System.Drawing.Image CurrentImage => imageList.Any() ? imageList[imageIndex] : null;

        // ==================== FILE OPERATIONS ====================

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = FileService.IMAGE_FILTER };
            if (dialog.ShowDialog() == true)
            {
                LoadFile(dialog.FileName);
            }
        }

        private void LoadFile(string filePath)
        {
            if (bgWorkerLoad.IsBusy) return;

            // Clean up previous state off the UI thread
            var oldHandler = largePdfHandler;
            var oldImages = imageList;
            largePdfHandler = null;
            isLargePdfMode = false;
            imageList = new List<System.Drawing.Image>();

            loadingOverlay.Visibility = Visibility.Visible;

            if (oldHandler != null)
            {
                loadingStatusText.Text = "Closing previous file...";
                Task.Run(() =>
                {
                    oldHandler.Dispose();
                    foreach (var img in oldImages) img?.Dispose();
                }).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        loadingStatusText.Text = "Loading new file...";
                        bgWorkerLoad.RunWorkerAsync(filePath);
                    });
                });
            }
            else
            {
                loadingStatusText.Text = "Please wait...";
                foreach (var img in oldImages) img?.Dispose();
                bgWorkerLoad.RunWorkerAsync(filePath);
            }
        }

        private void BgWorkerLoad_DoWork(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            string filePath = (string)e.Argument;
            string ext = Path.GetExtension(filePath).ToLower();
            var images = new List<System.Drawing.Image>();

            if (ext == ".pdf")
            {
                var fileInfo = new FileInfo(filePath);
                bool useLazyLoading = fileInfo.Length > LargePdfHandler.LargeFileThreshold;

                // Also check page count — even smaller PDFs with many pages should use lazy loading
                if (!useLazyLoading)
                {
                    worker?.ReportProgress(0, "Checking PDF page count...");
                    int quickPageCount = PdfUtilities.GetPdfPageCount(fileInfo.FullName);
                    if (quickPageCount > LargePdfHandler.MaxPagesForFullLoad)
                    {
                        useLazyLoading = true;
                    }
                }

                if (useLazyLoading)
                {
                    // Large/many-page PDF: lazy loading — convert first page only
                    worker?.ReportProgress(0, "Large PDF detected, loading first page...");
                    var handler = new LargePdfHandler(filePath);
                    int pageCount = handler.PageCount;
                    worker?.ReportProgress(10, $"PDF has {pageCount} pages. Converting page 1...");

                    var firstPage = handler.GetPage(0);
                    images.Add(firstPage);
                    for (int i = 1; i < pageCount; i++)
                        images.Add(null); // placeholders

                    e.Result = new LoadResult { FilePath = filePath, Images = images, IsLargePdf = true, PdfHandler = handler };
                    return;
                }
                else
                {
                    // Normal PDF: convert all pages with progress
                    Action<int, int> progress = (cur, total) =>
                    {
                        int pct = total > 0 ? (int)((cur + 1) * 100.0 / total) : 0;
                        worker?.ReportProgress(pct, $"Converting page {cur + 1} of {total}...");
                    };
                    images.AddRange(ImageIOHelper.GetImageList(new FileInfo(filePath), progress));
                }
            }
            else if (ext == ".tif" || ext == ".tiff")
            {
                images.AddRange(ImageIOHelper.GetImageList(new FileInfo(filePath)));
            }
            else
            {
                images.Add(System.Drawing.Image.FromFile(filePath));
            }

            e.Result = new LoadResult { FilePath = filePath, Images = images, IsLargePdf = false };
        }

        private void BgWorkerLoad_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            var msg = e.UserState as string ?? "Loading...";
            statusLabel.Content = msg;
            loadingStatusText.Text = msg;
        }

        private void BgWorkerLoad_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            loadingOverlay.Visibility = Visibility.Collapsed;
            progressBar.Visibility = Visibility.Hidden;
            this.Cursor = null;

            if (e.Error != null)
            {
                MessageBox.Show(this, $"Error loading file: {e.Error.Message}", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Error);
                statusLabel.Content = string.Empty;
                return;
            }
            if (e.Cancelled) { statusLabel.Content = "Loading cancelled."; return; }

            var result = (LoadResult)e.Result;
            imageList = result.Images;
            currentFilePath = result.FilePath;
            isLargePdfMode = result.IsLargePdf;
            largePdfHandler = result.PdfHandler;
            imageIndex = 0;
            undoStack.Clear();
            textBoxOCR.Clear();
            textBoxTranslation.Clear();
            ResetSearchAndTokens();

            txtPageNum.Text = "1";
            txtPageNum.IsEnabled = imageList.Count > 1;
            lblTotalPages.Content = $"/ {imageList.Count}";

            DisplayImage();
            SetNavigationButtons();

            this.Title = $"{APP_NAME} - {Path.GetFileName(result.FilePath)}";
            statusLabel.Content = isLargePdfMode
                ? $"Loaded (large PDF) - {imageList.Count} pages, pages load on demand"
                : $"Loaded: {Path.GetFileName(result.FilePath)}";
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => this.Close();

        private void PasteImage_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsImage())
            {
                var bmpSource = Clipboard.GetImage();
                var encoder = new System.Windows.Media.Imaging.BmpBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmpSource));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    imageList = new List<System.Drawing.Image> { System.Drawing.Image.FromStream(ms) };
                }
                imageIndex = 0;
                undoStack.Clear();
                textBoxOCR.Clear();
                textBoxTranslation.Clear();
                ResetSearchAndTokens();
                txtPageNum.Text = "1";
                txtPageNum.IsEnabled = false;
                lblTotalPages.Content = "/ 1";
                DisplayImage();
                SetNavigationButtons();
                statusLabel.Content = "Pasted from clipboard";
            }
        }

        // ==================== DRAG & DROP ====================

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) LoadFile(files[0]);
            }
        }

        // ==================== IMAGE DISPLAY ====================

        private async void DisplayImage()
        {
            if (!imageList.Any()) return;
            var img = imageList[imageIndex];

            // Lazy load for large PDF pages — run off UI thread
            if (img == null && isLargePdfMode && largePdfHandler != null)
            {
                int pageIdx = imageIndex;
                loadingOverlay.Visibility = Visibility.Visible;
                loadingStatusText.Text = $"Loading page {pageIdx + 1}...";

                try
                {
                    img = await Task.Run(() => largePdfHandler.GetPage(pageIdx));
                    imageList[pageIdx] = img;
                }
                catch (Exception ex)
                {
                    loadingOverlay.Visibility = Visibility.Collapsed;
                    statusLabel.Content = $"Error loading page {pageIdx + 1}";
                    MessageBox.Show(this, $"Error loading page: {ex.Message}", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                loadingOverlay.Visibility = Visibility.Collapsed;
                statusLabel.Content = $"Page {pageIdx + 1} loaded.";

                // Guard: user may have navigated away while we were loading
                if (imageIndex != pageIdx) return;
            }

            if (img == null) return;
            imageMain.Source = ImageConverter.BitmapToImageSource(img);
            imageCanvas.Width = img.Width / scaleX;
            imageCanvas.Height = img.Height / scaleY;
            lblDimensions.Content = $"{img.Width} x {img.Height}px {System.Drawing.Image.GetPixelFormatSize(img.PixelFormat)}bpp";

            EnableImageButtons(true);
        }

        private void EnableImageButtons(bool enabled)
        {
            btnFitImage.IsEnabled = enabled;
            btnActualSize.IsEnabled = enabled;
            btnZoomIn.IsEnabled = enabled;
            btnZoomOut.IsEnabled = enabled;
            btnRotateCCW.IsEnabled = enabled;
            btnRotateCW.IsEnabled = enabled;
        }

        private void SetNavigationButtons()
        {
            btnPrev.IsEnabled = imageIndex > 0;
            btnNext.IsEnabled = imageIndex < imageList.Count - 1;
        }

        private System.Drawing.Size FitImageToContainer(int imgW, int imgH, int containerW, int containerH)
        {
            double ratioX = (double)containerW / imgW;
            double ratioY = (double)containerH / imgH;
            double ratio = Math.Min(ratioX, ratioY);
            return new System.Drawing.Size((int)(imgW * ratio), (int)(imgH * ratio));
        }

        private void CenterPicturebox()
        {
            if (imageCanvas.Width < scrollViewer.ActualWidth)
                Canvas.SetLeft(imageCanvas, (scrollViewer.ActualWidth - imageCanvas.Width) / 2);
            if (imageCanvas.Height < scrollViewer.ActualHeight)
                Canvas.SetTop(imageCanvas, (scrollViewer.ActualHeight - imageCanvas.Height) / 2);
        }

        // ==================== PAGE NAVIGATION ====================

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (imageIndex > 0)
            {
                imageIndex--;
                txtPageNum.Text = (imageIndex + 1).ToString();
                imageCanvas.Deselect();
                undoStack.Clear();
                DisplayImage();
                SetNavigationButtons();
            }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (imageIndex < imageList.Count - 1)
            {
                imageIndex++;
                txtPageNum.Text = (imageIndex + 1).ToString();
                imageCanvas.Deselect();
                undoStack.Clear();
                DisplayImage();
                SetNavigationButtons();
            }
        }

        private void PageNum_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (int.TryParse(txtPageNum.Text, out int pageNum) && pageNum >= 1 && pageNum <= imageList.Count)
            {
                imageIndex = pageNum - 1;
                imageCanvas.Deselect();
                undoStack.Clear();
                DisplayImage();
                SetNavigationButtons();
            }
            else
            {
                txtPageNum.Text = (imageIndex + 1).ToString();
            }
        }

        // ==================== VIEW OPERATIONS ====================

        private void FitImage_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentImage == null) return;
            imageCanvas.Deselect();
            var fitSize = FitImageToContainer(CurrentImage.Width, CurrentImage.Height,
                (int)scrollViewer.ActualWidth, (int)scrollViewer.ActualHeight);
            imageCanvas.Width = fitSize.Width;
            imageCanvas.Height = fitSize.Height;
            scaleX = (float)CurrentImage.Width / fitSize.Width;
            scaleY = (float)CurrentImage.Height / fitSize.Height;
            CenterPicturebox();
            isFitImageSelected = true;
        }

        private void ActualSize_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentImage == null) return;
            imageCanvas.Deselect();
            imageCanvas.Width = CurrentImage.Width;
            imageCanvas.Height = CurrentImage.Height;
            scaleX = scaleY = 1f;
            CenterPicturebox();
            isFitImageSelected = false;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentImage == null) return;
            imageCanvas.Deselect();
            imageCanvas.Width = Convert.ToInt32(imageCanvas.Width * ZOOM_FACTOR);
            imageCanvas.Height = Convert.ToInt32(imageCanvas.Height * ZOOM_FACTOR);
            scaleX = (float)CurrentImage.Width / (float)imageCanvas.Width;
            scaleY = (float)CurrentImage.Height / (float)imageCanvas.Height;
            CenterPicturebox();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentImage == null) return;
            imageCanvas.Deselect();
            imageCanvas.Width = Convert.ToInt32(imageCanvas.Width / ZOOM_FACTOR);
            imageCanvas.Height = Convert.ToInt32(imageCanvas.Height / ZOOM_FACTOR);
            scaleX = (float)CurrentImage.Width / (float)imageCanvas.Width;
            scaleY = (float)CurrentImage.Height / (float)imageCanvas.Height;
            CenterPicturebox();
        }

        private void RotateCCW_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentImage == null) return;
            imageCanvas.Deselect();
            imageList[imageIndex].RotateFlip(RotateFlipType.Rotate270FlipNone);
            imageMain.Source = ImageConverter.BitmapToImageSource(imageList[imageIndex]);
            imageCanvas.Width = CurrentImage.Width / scaleX;
            imageCanvas.Height = CurrentImage.Height / scaleY;
            CenterPicturebox();
            undoStack.Clear();
        }

        private void RotateCW_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentImage == null) return;
            imageCanvas.Deselect();
            imageList[imageIndex].RotateFlip(RotateFlipType.Rotate90FlipNone);
            imageMain.Source = ImageConverter.BitmapToImageSource(imageList[imageIndex]);
            imageCanvas.Width = CurrentImage.Width / scaleX;
            imageCanvas.Height = CurrentImage.Height / scaleY;
            CenterPicturebox();
            undoStack.Clear();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && CurrentImage != null)
            {
                if (e.Delta > 0) ZoomIn_Click(sender, null);
                else ZoomOut_Click(sender, null);
                e.Handled = true;
            }
        }

        // ==================== IMAGE PROCESSING ====================

        private bool RequireImage()
        {
            if (!imageList.Any()) { MessageBox.Show(this, "Please load an image first.", APP_NAME); return false; }
            return true;
        }

        private void ApplyFilter(Func<System.Drawing.Image, System.Drawing.Image> filter)
        {
            if (!RequireImage()) return;
            var original = imageList[imageIndex];
            undoStack.Push(original);
            imageList[imageIndex] = filter(original);
            DisplayImage();
        }

        private void ApplySliderFilter(string label, Action<SliderDialog> setup, Func<System.Drawing.Image, float, System.Drawing.Image> filter)
        {
            if (!RequireImage()) return;
            var dialog = new SliderDialog { LabelText = label, Owner = this };
            setup?.Invoke(dialog);
            var original = imageList[imageIndex];
            undoStack.Push(original);
            dialog.ValueUpdated += (s, args) =>
            {
                var result = filter(original, (float)args.NewValue);
                if (result != null)
                {
                    imageList[imageIndex] = result;
                    imageMain.Source = ImageConverter.BitmapToImageSource(result);
                }
            };
            if (dialog.ShowDialog() != true)
            {
                imageList[imageIndex] = original;
                imageMain.Source = ImageConverter.BitmapToImageSource(original);
            }
        }

        private void Brightness_Click(object sender, RoutedEventArgs e) =>
            ApplySliderFilter("Brightness", null, (img, v) => imageProcessor.Brighten(img, v));

        private void Contrast_Click(object sender, RoutedEventArgs e) =>
            ApplySliderFilter("Contrast", d => d.SetForContrast(), (img, v) => imageProcessor.AdjustContrast(img, v));

        private void Gamma_Click(object sender, RoutedEventArgs e) =>
            ApplySliderFilter("Gamma", d => d.SetForGamma(), (img, v) => imageProcessor.AdjustGamma(img, v));

        private void Threshold_Click(object sender, RoutedEventArgs e) =>
            ApplySliderFilter("Threshold", d => d.SetForThreshold(), (img, v) => imageProcessor.AdjustThreshold(img, v));

        private void Grayscale_Click(object sender, RoutedEventArgs e) => ApplyFilter(imageProcessor.ConvertGrayscale);
        private void Monochrome_Click(object sender, RoutedEventArgs e) => ApplyFilter(imageProcessor.ConvertMonochrome);
        private void Invert_Click(object sender, RoutedEventArgs e) => ApplyFilter(imageProcessor.InvertColor);
        private void Sharpen_Click(object sender, RoutedEventArgs e) => ApplyFilter(imageProcessor.Sharpen);
        private void Smooth_Click(object sender, RoutedEventArgs e) => ApplyFilter(imageProcessor.GaussianBlur);
        private void Deskew_Click(object sender, RoutedEventArgs e) => ApplyFilter(imageProcessor.Deskew);
        private async void AutoCorrect_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireImage()) return;
            var original = imageList[imageIndex];
            undoStack.Push(original);

            loadingOverlay.Visibility = Visibility.Visible;
            loadingStatusText.Text = "Applying auto image correction...";

            try
            {
                var result = await Task.Run(() => imageProcessor.AutoCorrect(original));
                imageList[imageIndex] = result;
                DisplayImage();
            }
            catch (Exception ex)
            {
                imageList[imageIndex] = original;
                undoStack.Pop();
                MessageBox.Show(this, $"Auto correction failed: {ex.Message}", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void UndoImage_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count == 0) return;
            imageList[imageIndex] = undoStack.Pop();
            DisplayImage();
        }

        // ==================== AI OCR ====================

        private void ApiKeys_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ApiKeysDialog { Owner = this };
            if (dialog.ShowDialog() == true)
                aiSettings = AISettings.Load();
        }

        private void TranslationSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AITranslationDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                aiSettings = AISettings.Load();
                UpdateTranslationPanelVisibility();
            }
        }

        private void UpdateTranslationPanelVisibility()
        {
            if (aiSettings.AutoTranslationEnabled)
            {
                translationColumn.Width = new GridLength(1, GridUnitType.Star);
                gridSplitterTranslation.Visibility = Visibility.Visible;
                translationPanel.Visibility = Visibility.Visible;
            }
            else
            {
                translationColumn.Width = new GridLength(0);
                gridSplitterTranslation.Visibility = Visibility.Collapsed;
                translationPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void AiOcrCurrentPage_Click(object sender, RoutedEventArgs e) => await PerformAiOcr(false);
        private async void AiOcrAllPages_Click(object sender, RoutedEventArgs e) => await PerformAiOcr(true);

        /// <summary>
        /// Ensures the page at the given index is loaded (not a null placeholder).
        /// For large PDFs, loads the page on demand with a loading overlay.
        /// Returns true if the page is ready, false if it could not be loaded.
        /// </summary>
        private async Task<bool> EnsurePageLoaded(int pageIdx)
        {
            if (pageIdx < 0 || pageIdx >= imageList.Count) return false;
            if (imageList[pageIdx] != null) return true;

            if (largePdfHandler != null)
            {
                loadingOverlay.Visibility = Visibility.Visible;
                loadingStatusText.Text = $"Loading page {pageIdx + 1}...";
                try
                {
                    var img = await Task.Run(() => largePdfHandler.GetPage(pageIdx));
                    imageList[pageIdx] = img;
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error loading page {pageIdx + 1}: {ex.Message}", APP_NAME,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                finally
                {
                    loadingOverlay.Visibility = Visibility.Collapsed;
                }
            }

            MessageBox.Show(this, $"Page {pageIdx + 1} image is not available.", APP_NAME,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private async Task PerformAiOcr(bool allPages)
        {
            if (!RequireImage()) return;
            if (!aiSettings.HasApiKey())
            {
                MessageBox.Show(this, "No OpenRouter API key configured.\n\nPlease set up your API key in AI > API Keys.", APP_NAME,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (isAiOcrRunning) { bgWorkerAiOcr.CancelAsync(); return; }

            // For single page: ensure the current page image is loaded before starting
            if (!allPages)
            {
                if (!await EnsurePageLoaded(imageIndex)) return;
            }

            openRouterService = new OpenRouterService(aiSettings.OpenRouterApiKey, aiSettings.ModelName);
            openRouterService.ResetPageContext();
            tokenStats.Reset();

            UpdateTranslationPanelVisibility();
            textBoxOCR.Clear();
            if (aiSettings.AutoTranslationEnabled) textBoxTranslation.Clear();

            statusLabel.Content = "AI OCR running...";
            this.Cursor = Cursors.Wait;
            progressBar.Visibility = Visibility.Visible;
            isAiOcrRunning = true;
            menuAiStop.IsEnabled = true;
            menuAiStop.Visibility = Visibility.Visible;

            bgWorkerAiOcr.RunWorkerAsync(new AiOcrArgs
            {
                AllPages = allPages,
                StartIndex = allPages ? 0 : imageIndex,
                EndIndex = allPages ? imageList.Count - 1 : imageIndex,
                TranslateTo = aiSettings.AutoTranslationEnabled ? aiSettings.TargetLanguage : null
            });
        }

        private void BgWorkerAiOcr_DoWork(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            var args = (AiOcrArgs)e.Argument;
            var results = new List<AiOcrPageResult>();
            int totalPages = args.EndIndex - args.StartIndex + 1;
            int pageNum = 0;

            for (int i = args.StartIndex; i <= args.EndIndex; i++)
            {
                if (worker.CancellationPending) { e.Cancel = true; break; }
                pageNum++;
                int pct = totalPages > 0 ? (int)(pageNum * 100.0 / totalPages) : 0;

                try
                {
                    var image = imageList[i];

                    // Lazy-load page on demand for large PDFs
                    if (image == null && largePdfHandler != null)
                    {
                        worker.ReportProgress(pct, $"Loading page {i + 1} of {imageList.Count}...");
                        image = largePdfHandler.GetPage(i);
                        imageList[i] = image;
                    }

                    if (image == null) throw new Exception($"Page {i + 1} could not be loaded");

                    worker.ReportProgress(pct, $"OCR page {pageNum} of {totalPages} (page {i + 1})...");

                    byte[] imageData;
                    using (var ms = new MemoryStream())
                    {
                        image.Save(ms, ImageFormat.Png);
                        imageData = ms.ToArray();
                    }

                    var task = openRouterService.ProcessPdfPageAsync(imageData, args.TranslateTo);
                    task.Wait();
                    var result = task.Result;

                    var pageResult = new AiOcrPageResult
                    {
                        PageNumber = i + 1, Success = result.Success,
                        OriginalText = result.OriginalText, TranslatedText = result.TranslatedText,
                        Error = result.Error, InputTokens = result.InputTokens, OutputTokens = result.OutputTokens
                    };
                    results.Add(pageResult);
                    worker.ReportProgress(pct, pageResult);
                }
                catch (Exception ex)
                {
                    var pageResult = new AiOcrPageResult { PageNumber = i + 1, Success = false, Error = ex.Message };
                    results.Add(pageResult);
                    worker.ReportProgress(pct, pageResult);
                }
            }
            e.Result = results;
        }

        private void BgWorkerAiOcr_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is string msg) { statusLabel.Content = msg; return; }

            if (e.UserState is AiOcrPageResult result)
            {
                if (result.Success)
                {
                    if (!string.IsNullOrEmpty(result.OriginalText))
                    {
                        if (textBoxOCR.Text.Length > 0) textBoxOCR.AppendText($"\n\n--- Page {result.PageNumber} ---\n");
                        textBoxOCR.AppendText(result.OriginalText);
                        textBoxOCR.ScrollToEnd();
                    }

                    if (aiSettings.AutoTranslationEnabled && !string.IsNullOrEmpty(result.TranslatedText))
                    {
                        if (textBoxTranslation.Text.Length > 0) textBoxTranslation.AppendText($"\n\n--- Page {result.PageNumber} ---\n");
                        textBoxTranslation.AppendText(result.TranslatedText);
                        textBoxTranslation.ScrollToEnd();
                    }

                    tokenStats.AddPageStats(result.InputTokens, result.OutputTokens);
                    lblTokens.Content = $"In: {tokenStats.TotalInputTokens:N0} | Out: {tokenStats.TotalOutputTokens:N0}";
                }
                else
                {
                    textBoxOCR.AppendText($"\n\n--- Page {result.PageNumber} Error ---\n{result.Error}");
                }
            }
        }

        private void BgWorkerAiOcr_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            progressBar.Visibility = Visibility.Hidden;
            isAiOcrRunning = false;
            menuAiStop.IsEnabled = false;
            menuAiStop.Visibility = Visibility.Collapsed;
            this.Cursor = null;

            if (e.Error != null)
            {
                statusLabel.Content = string.Empty;
                MessageBox.Show(this, e.Error.Message, APP_NAME, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (e.Cancelled) { statusLabel.Content = "AI OCR cancelled"; }
            else
            {
                statusLabel.Content = "AI OCR completed";
                if (aiSettings.SaveOutputToFile) SaveOutputToFile();
                MessageBox.Show(this, $"AI OCR Complete\n\n{tokenStats.GetTotalStats()}", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AiOcrStop_Click(object sender, RoutedEventArgs e)
        {
            if (isAiOcrRunning && bgWorkerAiOcr.IsBusy)
            {
                bgWorkerAiOcr.CancelAsync();
                statusLabel.Content = "Stopping...";
                menuAiStop.IsEnabled = false;
            }
        }

        private async void TranslateText_Click(object sender, RoutedEventArgs e)
        {
            string text = textBoxOCR.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "No text to translate.\nPlease enter or OCR text first.", APP_NAME);
                return;
            }
            if (!aiSettings.HasApiKey())
            {
                MessageBox.Show(this, "No OpenRouter API key configured.", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!aiSettings.AutoTranslationEnabled || string.IsNullOrEmpty(aiSettings.TargetLanguage) || aiSettings.TargetLanguage == "None")
            {
                MessageBox.Show(this, "Please enable auto-translation and select a target language in AI > Translation Settings.", APP_NAME,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            openRouterService = new OpenRouterService(aiSettings.OpenRouterApiKey, aiSettings.ModelName);
            tokenStats.Reset();

            translationColumn.Width = new GridLength(1, GridUnitType.Star);
            gridSplitterTranslation.Visibility = Visibility.Visible;
            translationPanel.Visibility = Visibility.Visible;
            textBoxTranslation.Clear();

            statusLabel.Content = "Translating...";
            this.Cursor = Cursors.Wait;

            try
            {
                var result = await openRouterService.TranslateTextAsync(text, aiSettings.TargetLanguage);
                if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
                {
                    textBoxTranslation.Text = result.TranslatedText;
                    statusLabel.Content = "Translation complete";
                    lblTokens.Content = $"{result.InputTokens + result.OutputTokens}";
                }
                else
                {
                    statusLabel.Content = "Translation failed";
                    MessageBox.Show(this, $"Translation failed: {result.Error}", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                statusLabel.Content = "Translation error";
                MessageBox.Show(this, $"Error: {ex.Message}", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        // ==================== FIND IN TEXT ====================

        private int lastFindIndexOCR;
        private int lastFindIndexTranslation;

        private void ResetSearchAndTokens()
        {
            findTextOCR.Clear();
            findTextTranslation.Clear();
            lastFindIndexOCR = 0;
            lastFindIndexTranslation = 0;
            lblTokens.Content = "-";
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Focus the find box in the relevant panel
                var target = (textBoxTranslation.IsFocused || findTextTranslation.IsFocused)
                    ? findTextTranslation : findTextOCR;
                target.Focus();
                target.SelectAll();
                e.Handled = true;
            }
        }

        private void FindNextOCR_Click(object sender, RoutedEventArgs e) => FindInTextBox(textBoxOCR, findTextOCR, ref lastFindIndexOCR, true);
        private void FindPrevOCR_Click(object sender, RoutedEventArgs e) => FindInTextBox(textBoxOCR, findTextOCR, ref lastFindIndexOCR, false);

        private void FindNextTranslation_Click(object sender, RoutedEventArgs e) => FindInTextBox(textBoxTranslation, findTextTranslation, ref lastFindIndexTranslation, true);
        private void FindPrevTranslation_Click(object sender, RoutedEventArgs e) => FindInTextBox(textBoxTranslation, findTextTranslation, ref lastFindIndexTranslation, false);

        private void FindTextOCR_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FindInTextBox(textBoxOCR, findTextOCR, ref lastFindIndexOCR, !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
            }
        }

        private void FindTextTranslation_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FindInTextBox(textBoxTranslation, findTextTranslation, ref lastFindIndexTranslation, !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
            }
        }

        private void FindInTextBox(TextBox target, TextBox searchBox, ref int lastIndex, bool forward)
        {
            string searchText = searchBox.Text;
            if (string.IsNullOrEmpty(searchText)) return;
            string content = target.Text;
            if (string.IsNullOrEmpty(content)) return;

            int index;
            if (forward)
            {
                index = content.IndexOf(searchText, lastIndex, StringComparison.OrdinalIgnoreCase);
                if (index < 0) index = content.IndexOf(searchText, 0, StringComparison.OrdinalIgnoreCase); // wrap
            }
            else
            {
                int searchFrom = lastIndex > 0 ? lastIndex - 1 : content.Length - 1;
                index = content.LastIndexOf(searchText, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0) index = content.LastIndexOf(searchText, content.Length - 1, StringComparison.OrdinalIgnoreCase); // wrap
            }

            if (index >= 0)
            {
                target.Focus();
                target.Select(index, searchText.Length);
                lastIndex = index + (forward ? searchText.Length : 0);
                statusLabel.Content = "";
            }
            else
            {
                statusLabel.Content = "Text not found";
            }
        }

        // ==================== TEXT OPERATIONS ====================

        private void ClearText_Click(object sender, RoutedEventArgs e) => textBoxOCR.Clear();
        private void ClearTranslation_Click(object sender, RoutedEventArgs e) => textBoxTranslation.Clear();

        private void SaveTranslation_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBoxTranslation.Text)) return;
            var dialog = new SaveFileDialog
            {
                Filter = FileService.TEXT_FILTER,
                FileName = $"translation_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                InitialDirectory = baseDir
            };
            if (dialog.ShowDialog() == true)
            {
                fileService.SaveTextToFile(textBoxTranslation.Text, dialog.FileName);
                statusLabel.Content = "Translation saved.";
            }
        }

        private void SaveOutputToFile()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string ocrFile = Path.Combine(baseDir, $"ocr_output_{timestamp}.txt");
                File.WriteAllText(ocrFile, textBoxOCR.Text);

                if (aiSettings.AutoTranslationEnabled && !string.IsNullOrWhiteSpace(textBoxTranslation.Text))
                {
                    string transFile = Path.Combine(baseDir, $"translation_output_{timestamp}.txt");
                    File.WriteAllText(transFile, textBoxTranslation.Text);
                }
                statusLabel.Content = $"Output saved to {baseDir}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error saving: {ex.Message}", APP_NAME, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ==================== ABOUT ====================

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this,
                $"{APP_NAME}\n\nAI-Powered OCR & Translation Studio\nPowered by OpenRouter.ai\n\nIdea/Development: Hishiryo 2026",
                $"About {APP_NAME}", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ==================== HELPER CLASSES ====================

    internal class LoadResult
    {
        public string FilePath { get; set; }
        public List<System.Drawing.Image> Images { get; set; }
        public bool IsLargePdf { get; set; }
        public LargePdfHandler PdfHandler { get; set; }
    }

    internal class AiOcrArgs
    {
        public bool AllPages { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public string TranslateTo { get; set; }
    }

    internal class AiOcrPageResult
    {
        public int PageNumber { get; set; }
        public bool Success { get; set; }
        public string OriginalText { get; set; }
        public string TranslatedText { get; set; }
        public string Error { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
