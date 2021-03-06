using Cupscale.Cupscale;
using Cupscale.Forms;
using Cupscale.ImageUtils;
using Cupscale.IO;
using Cupscale.Main;
using Upscale = Cupscale.Main.Upscale;
using Cupscale.UI;
using ImageMagick;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Paths = Cupscale.IO.Paths;

namespace Cupscale.OS
{
    internal class ESRGAN
    {
        private static Process currentProcess;

        public enum PreviewMode { None, Cutout, FullImage }
        public enum Backend { CUDA, CPU, NCNN }

        public static async Task DoUpscale(string inpath, string outpath, ModelData mdl, string tilesize, bool alpha, PreviewMode mode, Backend backend, bool showTileProgress = true)
        {
            bool useJoey = Config.GetInt("esrganVer") == 0;
            try
            {
                if (backend == Backend.NCNN)
                {
                    Program.mainForm.SetProgress(1f, "Loading ESRGAN-NCNN...");
                    DialogForm dialogForm = new DialogForm("Loading ESRGAN-NCNN...\nThis should take 10-25 seconds.", 14);
                    Program.lastModelName = mdl.model1Name;
                    await RunNcnn(inpath, outpath, mdl.model1Path);
                }
                else
                {
                    Program.mainForm.SetProgress(4f, "Starting ESRGAN...");
                    File.Delete(Paths.progressLogfile);
                    string modelArg = GetModelArg(mdl, useJoey);
                    if (useJoey)
                        await RunJoey(inpath, outpath, modelArg, tilesize, alpha, showTileProgress);
                    else
                        await Run(inpath, outpath, modelArg, tilesize, alpha, showTileProgress);
                }

                if (mode == PreviewMode.Cutout)
                {
                    await ScalePreviewOutput();
                    Program.mainForm.SetProgress(100f, "Merging into preview...");
                    await Program.PutTaskDelay();
                    PreviewMerger.Merge();
                    Program.mainForm.SetHasPreview(true);
                }
                if (mode == PreviewMode.FullImage)
                {
                    await ScalePreviewOutput();
                    Program.mainForm.SetProgress(100f, "Merging into preview...");
                    await Program.PutTaskDelay();
                    Image outImg = ImgUtils.GetImage(Directory.GetFiles(Paths.previewOutPath, "*.png.*", SearchOption.AllDirectories)[0]);
                    Image inputImg = ImgUtils.GetImage(Paths.tempImgPath);
                    MainUIHelper.previewImg.Image = outImg;
                    MainUIHelper.currentOriginal = inputImg;
                    MainUIHelper.currentOutput = outImg;
                    MainUIHelper.currentScale = ImgUtils.GetScaleFloat(inputImg, outImg);
                    MainUIHelper.previewImg.ZoomToFit();
                    Program.mainForm.SetHasPreview(true);
                    //Program.mainForm.SetProgress(0f, "Done.");
                }
            }
            catch (Exception e)
            {
                if (e.Message.Contains("No such file"))
                    Program.ShowMessage("An error occured during upscaling.\nThe upscale process seems to have exited before completion!", "Error");
                else
                    Program.ShowMessage("An error occured during upscaling.", "Error");
                Logger.Log("[ESRGAN] Upscaling Error: " + e.Message + "\n" + e.StackTrace);
                Program.mainForm.SetProgress(0f, "Cancelled.");
            }

        }

        public static async Task ScalePreviewOutput()
        {
            if (ImageProcessing.postScaleMode == Upscale.ScaleMode.Percent && ImageProcessing.postScaleValue == 100)   // Skip if target scale is 100%)
                return;
            Program.mainForm.SetProgress(1f, "[ESRGAN] Resizing preview output...");
            await Task.Delay(1);
            //MagickImage img = ImgUtils.GetMagickImage(Directory.GetFiles(Paths.previewOutPath, "*.png.*", SearchOption.AllDirectories)[0]);
            //MagickImage magickImage = ImageProcessing.ResizeImagePost(img);
            //img = magickImage;
            //img.Write(img.FileName);
            string img = Directory.GetFiles(Paths.previewOutPath, "*.png.*", SearchOption.AllDirectories)[0];
            ImageProcessing.ResizeImagePost(img);
        }

        public static string GetModelArg(ModelData mdl, bool joey)
        {
            string mdl1 = mdl.model1Path;
            string mdl2 = mdl.model2Path;
            ModelData.ModelMode mdlMode = mdl.mode;
            if (mdlMode == ModelData.ModelMode.Single)
            {
                Program.lastModelName = mdl.model1Name;
                if (joey)
                    return mdl1.Wrap(true, false);
                else
                    return " --model \"" + mdl1 + "\"";
            }
            if (mdlMode == ModelData.ModelMode.Interp)
            {
                int interpLeft = 100 - mdl.interp;
                int interpRight = mdl.interp;
                Program.lastModelName = mdl.model1Name + ":" + interpLeft + ":" + mdl.model2Name + ":" + interpRight;
                if (joey)
                    return (mdl1 + ";" + interpLeft + "&" + mdl2 + ";" + interpRight).Wrap(true);
                else
                    return " --model " + mdl1.Wrap() + ";" + interpLeft + ";" + mdl2.Wrap() + ";" + interpRight;
            }
            if (mdlMode == ModelData.ModelMode.Chain)
            {
                Program.lastModelName = mdl.model1Name + ">>" + mdl.model2Name;
                if (joey)
                    return (mdl1 + ">" + mdl2).Wrap(true);
                else
                    return " --model  " + mdl1.Wrap() + " --postfilter " + mdl2.Wrap();
            }
            if (mdlMode == ModelData.ModelMode.Advanced)
            {
                Program.lastModelName = "Advanced";
                return AdvancedModelSelection.GetArg(joey);
            }
            return null;
        }

        public static async Task Run(string inpath, string outpath, string modelArg, string tilesize, bool alpha, bool showTileProgress)
        {
            bool showWindow = Config.GetInt("cmdDebugMode") > 0;
            bool stayOpen = Config.GetInt("cmdDebugMode") == 2;

            inpath = inpath.Wrap(true, true);
            outpath = outpath.Wrap(true, true);

            string alphaStr = " --noalpha";
            if (alpha) alphaStr = "";

            string deviceStr = " --device cuda";
            if (Config.Get("cudaFallback").GetInt() == 1 || Config.Get("cudaFallback").GetInt() == 2) deviceStr = " --device cpu";

            string opt = "/C";
            if (stayOpen) opt = "/K";

            string cmd = $"{opt} cd /D {Paths.esrganPath.Wrap()} & ";
            cmd += $"{EmbeddedPython.GetPyCmd()} esrlmain.py {inpath}{outpath}{deviceStr} --tilesize {tilesize}{alphaStr}{modelArg}";
            Logger.Log("[CMD] " + cmd);
            Process esrganProcess = new Process();
            esrganProcess.StartInfo.UseShellExecute = showWindow;
            esrganProcess.StartInfo.RedirectStandardOutput = !showWindow;
            esrganProcess.StartInfo.RedirectStandardError = !showWindow;
            esrganProcess.StartInfo.CreateNoWindow = !showWindow;
            esrganProcess.StartInfo.FileName = "cmd.exe";
            esrganProcess.StartInfo.Arguments = cmd;
            if (!showWindow)
            {
                esrganProcess.OutputDataReceived += OutputHandler;
                esrganProcess.ErrorDataReceived += OutputHandler;
            }
            currentProcess = esrganProcess;
            esrganProcess.Start();
            if (!showWindow)
            {
                esrganProcess.BeginOutputReadLine();
                esrganProcess.BeginErrorReadLine();
            }
            while (!esrganProcess.HasExited)
            {
                if (showTileProgress)
                    await UpdateProgressFromFile();
                await Task.Delay(50);
            }
            if (Main.Upscale.currentMode == Main.Upscale.UpscaleMode.Batch)
            {
                await Task.Delay(1000);
                Program.mainForm.SetProgress(100f, "Post-Processing...");
                PostProcessingQueue.Stop();
            }
            File.Delete(Paths.progressLogfile);
        }

        public static async Task RunJoey(string inpath, string outpath, string modelArg, string tilesize, bool alpha, bool showTileProgress)
        {
            bool showWindow = Config.GetInt("cmdDebugMode") > 0;
            bool stayOpen = Config.GetInt("cmdDebugMode") == 2;

            inpath = inpath.Wrap(true, true);
            outpath = outpath.Wrap(true, true);

            string alphaStr = "";
            if (alpha) alphaStr = " --alpha_mode 2 ";

            string deviceStr = "";
            if (Config.Get("cudaFallback").GetInt() == 1 || Config.Get("cudaFallback").GetInt() == 2) deviceStr = " --cpu ";

            string seamStr = "";
            if (Config.Get("seamlessMode").GetInt() == 1) seamStr = " --seamless";
            if (Config.Get("seamlessMode").GetInt() == 2) seamStr = " --mirror";

            string opt = "/C";
            if (stayOpen) opt = "/K";

            string cmd = $"{opt} cd /D {Paths.esrganPath.Wrap()} & ";
            cmd += $"{EmbeddedPython.GetPyCmd()} upscale.py --input{inpath}--output{outpath}{deviceStr}{seamStr} --tile_size {tilesize}{alphaStr}{modelArg}";

            Logger.Log("[CMD] " + cmd);
            Process esrganProcess = new Process();
            esrganProcess.StartInfo.UseShellExecute = showWindow;
            esrganProcess.StartInfo.RedirectStandardOutput = !showWindow;
            esrganProcess.StartInfo.RedirectStandardError = !showWindow;
            esrganProcess.StartInfo.CreateNoWindow = !showWindow;
            esrganProcess.StartInfo.FileName = "cmd.exe";
            esrganProcess.StartInfo.Arguments = cmd;
            if (!showWindow)
            {
                esrganProcess.OutputDataReceived += OutputHandler;
                esrganProcess.ErrorDataReceived += OutputHandler;
            }
            currentProcess = esrganProcess;
            esrganProcess.Start();
            if (!showWindow)
            {
                esrganProcess.BeginOutputReadLine();
                esrganProcess.BeginErrorReadLine();
            }
            while (!esrganProcess.HasExited)
            {
                if (showTileProgress)
                    await UpdateProgressFromFile();
                await Task.Delay(50);
            }
            if (Main.Upscale.currentMode == Main.Upscale.UpscaleMode.Batch)
            {
                await Task.Delay(1000);
                Program.mainForm.SetProgress(100f, "Post-Processing...");
                PostProcessingQueue.Stop();
            }
            File.Delete(Paths.progressLogfile);
        }

        private static void OutputHandler(object sendingProcess, DataReceivedEventArgs output)
        {
            if (output == null || output.Data == null)
            {
                return;
            }
            string data = output.Data;
            Logger.Log("[Python] " + data);
            if (data.ToLower().Contains("error"))
            {
                if (currentProcess != null && !currentProcess.HasExited)
                    currentProcess.Kill();

                Program.ShowMessage("Error occurred: \n\n" + data + "\n\nThe ESRGAN process was killed to avoid lock-ups.", "Error");
            }

            if (data.Contains("out of memory"))
                Program.ShowMessage("ESRGAN ran out of memory. Try reducing the tile size and avoid running programs in the background (especially games) that take up your VRAM.", "Error");

            if (data.Contains("Python was not found"))
                Program.ShowMessage("Python was not found. Make sure you have a working Python 3 installation.", "Error");

            if (data.Contains("ModuleNotFoundError"))
                Program.ShowMessage("You are missing ESRGAN Python dependencies. Make sure Pytorch, cv2 (opencv-python) and tensorboardx are installed.", "Error");

            if (data.Contains("RRDBNet"))
                Program.ShowMessage("Model appears to be incompatible!", "Error");

            if (data.Contains("UnpicklingError"))
                Program.ShowMessage("Failed to load model!", "Error");
        }

        static string lastProgressString = "";
        private static async Task UpdateProgressFromFile()
        {
            string progressLogFile = Paths.progressLogfile;
            if (!File.Exists(progressLogFile))
                return;
            string[] lines = IOUtils.ReadLines(progressLogFile);
            if (lines.Length < 1)
                return;
            string outStr = (lines[lines.Length - 1]);
            if (outStr == lastProgressString)
                return;
            lastProgressString = outStr;
            string text = outStr.Replace("Tile ", "").Trim();
            try
            {
                int num = int.Parse(text.Split('/')[0]);
                int num2 = int.Parse(text.Split('/')[1]);
                float previewProgress = (float)num / (float)num2 * 100f;
                Program.mainForm.SetProgress(previewProgress, "Processing Tiles - " + previewProgress.ToString("0") + "%");
            }
            catch
            {
                Logger.Log("[ESRGAN] Failed to parse progress from this line: " + text);
            }
            await Task.Delay(1);
        }

        public static string currentNcnnModel = "";

        public static async Task RunNcnn(string inpath, string outpath, string modelPath)
        {
            bool showWindow = Config.GetInt("cmdDebugMode") > 0;
            bool stayOpen = Config.GetInt("cmdDebugMode") == 2;

            inpath = inpath.Wrap();
            outpath = outpath.Wrap();

            Program.mainForm.SetProgress(3f, "Converting NCNN model...");
            await NcnnUtils.ConvertNcnnModel(modelPath);
            Logger.Log("[ESRGAN] NCNN Model is ready: " + currentNcnnModel);
            Program.mainForm.SetProgress(4f, "Loading ESRGAN-NCNN...");
            int scale = NcnnUtils.GetNcnnModelScale(currentNcnnModel);

            string opt = "/C";
            if (stayOpen) opt = "/K";

            string cmd = $"{opt} cd /D {Paths.esrganPath.Wrap()} & ";
            cmd += "esrgan-ncnn-vulkan.exe -i " + inpath + " -o " + outpath + " -m " + currentNcnnModel.Wrap() + " -s " + scale;
            Logger.Log("[CMD] " + cmd);

            Process ncnnProcess = new Process();
            ncnnProcess.StartInfo.UseShellExecute = showWindow;
            ncnnProcess.StartInfo.RedirectStandardOutput = !showWindow;
            ncnnProcess.StartInfo.RedirectStandardError = !showWindow;
            ncnnProcess.StartInfo.CreateNoWindow = !showWindow;
            ncnnProcess.StartInfo.FileName = "cmd.exe";
            ncnnProcess.StartInfo.Arguments = cmd;
            if (!showWindow)
            {
                ncnnProcess.OutputDataReceived += NcnnOutputHandler;
                ncnnProcess.ErrorDataReceived += NcnnOutputHandler;
            } 
            currentProcess = ncnnProcess;
            ncnnProcess.Start();
            if (!showWindow)
            {
                ncnnProcess.BeginOutputReadLine();
                ncnnProcess.BeginErrorReadLine();
            }
            while (!ncnnProcess.HasExited)
                await Task.Delay(50);

            if (Upscale.currentMode == Upscale.UpscaleMode.Batch)
            {
                await Task.Delay(1000);
                Program.mainForm.SetProgress(100f, "[ESRGAN] Post-Processing...");
                PostProcessingQueue.Stop();
            }
            File.Delete(Paths.progressLogfile);
        }

        private static void NcnnOutputHandler(object sendingProcess, DataReceivedEventArgs output)
        {
            if (output == null || output.Data == null)
                return;

            string data = output.Data;
            Logger.Log("[NCNN] " + data.Replace("\n", " ").Replace("\r", " "));
            if (data.Contains("failed"))
            {
                if (currentProcess != null && !currentProcess.HasExited)
                    currentProcess.Kill();

                Program.ShowMessage("Error occurred: \n\n" + data + "\n\nThe ESRGAN-NCNN process was killed to avoid lock-ups.", "Error");
            }

            if (data.Contains("vkAllocateMemory"))
                Program.ShowMessage("ESRGAN-NCNN ran out of memory. Try reducing the tile size and avoid running programs in the background (especially games) that take up your VRAM.", "Error");
        }

        public static string Interpolate (ModelData mdl)
        {
            bool showWindow = Config.GetInt("cmdDebugMode") > 0;
            bool stayOpen = Config.GetInt("cmdDebugMode") == 2;

            Process py = OSUtils.NewProcess(!showWindow);

            string opt = "/C";
            if (stayOpen) opt = "/K";

            string alphaStr = (mdl.interp / 100f).ToString("0.00").Replace(",", ".");
            string outPath = mdl.model1Path.GetParentDir();
            string filename = $"{mdl.model1Name}-{mdl.model2Name}-interp{alphaStr}.pth";
            outPath = Path.Combine(outPath, filename);

            string cmd = $"{opt} cd /D {Paths.esrganPath.Wrap()} & ";
            cmd += $"{EmbeddedPython.GetPyCmd()} interp.py {mdl.model1Path.Wrap()} {mdl.model2Path.Wrap()} {alphaStr} {outPath.Wrap()}";
            
            py.StartInfo.Arguments = cmd;
            Logger.Log("[ESRGAN Interp] CMD: " + py.StartInfo.Arguments);
            py.Start();
            py.WaitForExit();
            string output = py.StandardOutput.ReadToEnd();
            string err = py.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(err)) output += "\n" + err;
            Logger.Log("[ESRGAN Interp] Output: " + output);
            if (output.ToLower().Contains("error"))
                throw new Exception("Interpolation Error - Output:\n" + output);
            return outPath;
        }
    }
}
