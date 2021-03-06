using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using Cupscale.ImageUtils;
using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using ImageMagick;
using Paths = Cupscale.IO.Paths;
using Point = System.Drawing.Point;

namespace Cupscale
{
    internal class PreviewMerger
    {
        public static float offsetX;
        public static float offsetY;
        public static string inputCutoutPath;
        public static string outputCutoutPath;

        public static bool showingOriginal = false;

        public static void Merge()
        {
            MainUIHelper.sw.Stop();
            Program.mainForm.SetProgress(100f);
            inputCutoutPath = Path.Combine(Paths.previewPath, "preview.png.png");
            outputCutoutPath = Directory.GetFiles(Paths.previewOutPath, "*.png.*", SearchOption.AllDirectories)[0];

            Image sourceImg = ImgUtils.GetImage(Paths.tempImgPath);
            int scale = GetScale();
            if (sourceImg.Width * scale > 16000 || sourceImg.Height * scale > 16000)
            {
                MergeOnlyCutout();
                Program.ShowMessage("The scaled output image is very large (>16000px), so only the cutout will be shown.", "Warning");
                return;
            }
            MergeScrollable();
        }

        static void MergeScrollable()
        {

            if (offsetX < 0f) offsetX *= -1f;
            if (offsetY < 0f) offsetY *= -1f;
            int scale = GetScale();
            offsetX *= scale;
            offsetY *= scale;
            Logger.Log("[Merger] Merging " + Path.GetFileName(outputCutoutPath) + " onto original using offset " + offsetX + "x" + offsetY);
            Image image = MergeInMemory(scale);
            MainUIHelper.currentOriginal = ImgUtils.GetImage(Paths.tempImgPath);
            MainUIHelper.currentOutput = image;
            MainUIHelper.currentScale = ImgUtils.GetScaleFloat(ImgUtils.GetImage(inputCutoutPath), ImgUtils.GetImage(outputCutoutPath));
            UIHelpers.ReplaceImageAtSameScale(MainUIHelper.previewImg, image);
            Program.mainForm.SetProgress(0f, "Done.");
        }


        public static Image MergeInMemory(int scale)
        {
            Image scaledSourceImg;
            int oldWidth;
            int newWidth;
            if (!(ImageProcessing.preScaleMode == Upscale.ScaleMode.Percent && ImageProcessing.preScaleValue == 100))
            {
                string tempScaledSourceImagePath = Path.Combine(Paths.tempImgPath.GetParentDir(), "scaled-source.png");
                MagickImage scaledSourceMagickImg = new MagickImage(Paths.tempImgPath);
                oldWidth = scaledSourceMagickImg.Width;
                scaledSourceMagickImg = ImageProcessing.ResizeImagePre(scaledSourceMagickImg);
                newWidth = scaledSourceMagickImg.Width;
                scaledSourceMagickImg.Write(tempScaledSourceImagePath);
               scaledSourceImg = ImgUtils.GetImage(tempScaledSourceImagePath);
            }
            else
            {
                scaledSourceImg = ImgUtils.GetImage(Paths.tempImgPath);
                oldWidth = scaledSourceImg.Width;
                newWidth = scaledSourceImg.Width;
            }
            float preScale = (float)oldWidth / (float)newWidth;

            Image cutout = ImgUtils.GetImage(outputCutoutPath);

            if (scaledSourceImg.Width * scale == cutout.Width && scaledSourceImg.Height * scale == cutout.Height)
            {
                Logger.Log("[Merger] Cutout is the entire image - skipping merge");
                return cutout;
            }

            var destImage = new Bitmap(scaledSourceImg.Width * scale, scaledSourceImg.Height * scale);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (Program.currentFilter == FilterType.Point)
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                graphics.DrawImage(scaledSourceImg, 0, 0, destImage.Width, destImage.Height);       // Scale up
                graphics.DrawImage(cutout, (offsetX / preScale).RoundToInt(), (offsetY / preScale).RoundToInt());     // Overlay cutout
            }

            return destImage;
        }

        static void MergeOnlyCutout()
        {
            int scale = GetScale();

            MagickImage originalCutout = ImgUtils.GetMagickImage(inputCutoutPath);
            originalCutout.FilterType = Program.currentFilter;
            originalCutout.Resize(new Percentage(scale * 100));
            string scaledCutoutPath = Path.Combine(Paths.previewOutPath, "preview-input-scaled.png");
            originalCutout.Format = MagickFormat.Png;
            originalCutout.Quality = 0;  // Save preview as uncompressed PNG for max speed
            originalCutout.Write(scaledCutoutPath);

            MainUIHelper.currentOriginal = ImgUtils.GetImage(scaledCutoutPath);
            MainUIHelper.currentOutput = ImgUtils.GetImage(outputCutoutPath);

            MainUIHelper.previewImg.Image = MainUIHelper.currentOutput;
            MainUIHelper.previewImg.ZoomToFit();
            MainUIHelper.previewImg.Zoom = (int)Math.Round(MainUIHelper.previewImg.Zoom * 1.01f);
            Program.mainForm.resetImageOnMove = true;
            Program.mainForm.SetProgress(0f, "Done.");
        }

        private static int GetScale()
        {
            MagickImage val = ImgUtils.GetMagickImage(inputCutoutPath);
            MagickImage val2 = ImgUtils.GetMagickImage(outputCutoutPath);
            int result = (int)Math.Round((float)val2.Width / (float)val.Width);
            return result;
        }

        public static void ShowOutput()
        {
            if (MainUIHelper.currentOutput != null)
            {
                showingOriginal = false;
                UIHelpers.ReplaceImageAtSameScale(MainUIHelper.previewImg, MainUIHelper.currentOutput);
            }
        }

        public static void ShowOriginal()
        {
            if (MainUIHelper.currentOriginal != null)
            {
                showingOriginal = true;
                UIHelpers.ReplaceImageAtSameScale(MainUIHelper.previewImg, MainUIHelper.currentOriginal);
            }
        }
    }
}
