# Lingosolve Studio -- Installation Guide

## Prerequisites

### 1. .NET Framework 4.8

Lingosolve Studio requires .NET Framework 4.8, which is included with Windows 10 (version 1903+) and Windows 11.

If needed, download it from:
https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48

### 2. Ghostscript (required for PDF support)

PDF rendering depends on Ghostscript. Install the **64-bit** version:

1. Download Ghostscript from https://www.ghostscript.com/releases/gsdnld.html
2. Install the **GPL 64-bit** edition (e.g. `gs10040w64.exe`)
3. The default install path is `C:\Program Files\gs\gs10.04.0\`
4. No additional configuration is needed -- the application locates Ghostscript automatically via the registry

### 3. OpenRouter.ai API Key

Lingosolve Studio uses OpenRouter.ai for AI OCR and translation.

1. Create an account at https://openrouter.ai
2. Go to https://openrouter.ai/keys and create an API key
3. Add credits to your account (pay-as-you-go)
4. You will enter this key in the application under **AI > API Keys**

## Building from Source

### Using Visual Studio 2022

1. Open `LingosolveStudio.sln` in Visual Studio 2022
2. Set the configuration to **Debug** or **Release**, platform **Any CPU**
3. Build the solution (Ctrl+Shift+B)
4. The output is in `bin\Debug\` or `bin\Release\`

### Using MSBuild (command line)

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    LingosolveStudio.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
```

The built executable will be at `bin\Release\LingosolveStudio.exe`.

## Running the Application

1. Navigate to the build output directory (e.g. `bin\Release\`)
2. Run `LingosolveStudio.exe`
3. On first launch:
   - Go to **AI > API Keys** and enter your OpenRouter API key
   - Select a model (default models work well for OCR)
   - Optionally go to **AI > Translation Settings** to enable auto-translation

## File Types Supported

| Type | Extensions |
|---|---|
| Images | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, `.tif`, `.tiff` |
| PDF | `.pdf` (requires Ghostscript) |

## Troubleshooting

### PDF files don't open
- Verify Ghostscript 64-bit is installed
- Check that the Ghostscript `bin` folder contains `gsdll64.dll`
- Restart the application after installing Ghostscript

### AI OCR returns errors
- Verify your OpenRouter API key is correct in **AI > API Keys**
- Check that your OpenRouter account has sufficient credits
- Ensure you have internet connectivity

### Application won't start
- Verify .NET Framework 4.8 is installed
- Try running as administrator if there are permission issues
