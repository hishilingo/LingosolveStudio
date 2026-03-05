using System.Drawing;
using System.Drawing.Imaging;
using LingosolveStudio.Utilities;

namespace LingosolveStudio.Services
{
    /// <summary>
    /// Consolidates all image processing operations into a standalone service.
    /// </summary>
    public class ImageProcessingService
    {
        public Image Brighten(Image original, float value)
        {
            return ImageHelper.Brighten(original, value * 0.005f);
        }

        public Image AdjustContrast(Image original, float value)
        {
            return ImageHelper.Contrast(original, value * 0.04f);
        }

        public Image AdjustGamma(Image original, float value)
        {
            return ImageHelper.AdjustGamma(original, value * 0.005f);
        }

        public Image AdjustThreshold(Image original, float value)
        {
            return ImageHelper.AdjustThreshold(original, value * 0.01f);
        }

        public Image ConvertGrayscale(Image original)
        {
            return ImageHelper.ConvertGrayscale((Bitmap)original);
        }

        public Image ConvertMonochrome(Image original)
        {
            return ImageHelper.ConvertMonochrome((Bitmap)original);
        }

        public Image InvertColor(Image original)
        {
            return ImageHelper.InvertColor((Bitmap)original);
        }

        public Image Sharpen(Image original)
        {
            return ImageHelper.Sharpen((Bitmap)original);
        }

        public Image GaussianBlur(Image original)
        {
            return ImageHelper.GaussianBlur((Bitmap)original);
        }

        public Image BilateralFilter(Image original)
        {
            return ImageHelper.BilateralFilter((Bitmap)original);
        }

        public Image Deskew(Image original)
        {
            gmseDeskew deskew = new gmseDeskew((Bitmap)original);
            double skewAngle = deskew.GetSkewAngle();
            const double MINIMUM_DESKEW_THRESHOLD = 0.05d;

            if (skewAngle > MINIMUM_DESKEW_THRESHOLD || skewAngle < -MINIMUM_DESKEW_THRESHOLD)
            {
                return ImageHelper.Rotate((Bitmap)original, -skewAngle);
            }
            return original;
        }

        public Image AutoCorrect(Image original)
        {
            Image result = Deskew(original);
            result = ImageHelper.AutoContrast((Bitmap)result);
            return result;
        }


    }
}
