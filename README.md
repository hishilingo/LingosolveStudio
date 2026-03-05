# Lingosolve Studio

AI-Powered OCR & Translation Studio for Windows.

Lingosolve Studio is a standalone WPF desktop application that combines image viewing, AI-based optical character recognition (OCR), and translation into a single workflow. It uses OpenRouter.ai to send page images to vision-capable language models and retrieve extracted text and translations.

## Features

- **Image & PDF Viewer** -- Open images (PNG, JPG, BMP, TIFF) and PDF files. Large PDFs (>50 MB) use lazy page loading for fast startup.
- **AI OCR** -- Extract text from the current page or all pages using OpenRouter.ai vision models. Results stream into the OCR output panel as pages are processed.
- **AI Translation** -- Translate OCR output into a target language. Can run automatically alongside OCR or on demand.
- **Image Processing** -- Brightness, contrast, gamma, threshold, grayscale, monochrome, invert, sharpen, smooth, deskew, and auto image correction (deskew + auto-contrast).
- **Find in Text** -- Built-in search bars in both OCR and Translation panels. Ctrl+F to focus, Enter/Shift+Enter to navigate matches.
- **Multi-page Navigation** -- Page forward/back buttons, direct page number entry, zoom, rotate, fit-to-window.
- **Drag & Drop** -- Drop image or PDF files directly onto the window.
- **Clipboard Paste** -- Paste images from the clipboard for quick OCR.
- **Token Tracking** -- Status bar shows cumulative input/output token usage per session.

## Requirements

- Windows 10 or later
- .NET Framework 4.8
- Ghostscript (for PDF rendering) -- see [INSTALL.md](INSTALL.md)
- An OpenRouter.ai API key

## Quick Start

1. Build or obtain `LingosolveStudio.exe` (see [INSTALL.md](INSTALL.md))
2. Launch the application
3. Go to **AI > API Keys** and enter your OpenRouter.ai API key
4. Optionally configure a translation language in **AI > Translation Settings**
5. Open an image or PDF and click the OCR toolbar button or use **AI > AI OCR (Current Page)**

## Configuration

- **API Keys** -- Set your OpenRouter API key and select a model via **AI > API Keys**.
- **Translation** -- Enable auto-translation and choose a target language via **AI > Translation Settings**. When enabled, a third panel appears showing translations alongside OCR output.
- **Save Output** -- OCR and translation results can be saved to files. Translation output has a Save button in its panel header.

## Project Structure

```
LingosolveStudio/
  MainWindow.xaml / .cs    -- Main application window
  Dialogs/                 -- API keys, translation settings, slider dialogs
  Services/                -- OpenRouter API, AI settings, image processing, file I/O
  Controls/                -- Image canvas, move/resize thumbs
  Utilities/               -- Image helpers, PDF conversion, deskew, large PDF handler
  Icons/fatcow/            -- Toolbar and menu icons
  Resources/               -- Application icon
```

## Credits

Idea/Development: Hishiryo 2026
Lingosolve Studio is partly inspired by VietOCR (https://vietocr.sourceforge.net/).
Powered by [OpenRouter.ai](https://openrouter.ai)

## License

Lingosolve Studio is licensed under the Apache License, Version 2.0
