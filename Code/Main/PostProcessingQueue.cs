﻿using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cupscale.Cupscale
{
    class PostProcessingQueue
    {
        public static Queue<string> outputFileQueue = new Queue<string>();
        public static List<string> processedFiles = new List<string>();
        public static List<string> outputFiles = new List<string>();

        public static bool run;
        public static string currentOutPath;

        public static bool ncnn;

        public enum CopyMode { KeepStructure, CopyToRoot }
        public static CopyMode copyMode;

        public static void Start (string outpath)
        {
            currentOutPath = outpath;
            outputFileQueue.Clear();
            processedFiles.Clear();
            outputFiles.Clear();
            IOUtils.DeleteContentsOfDir(Paths.imgOutNcnnPath);
            run = true;
        }

        public static void Stop ()
        {
            Logger.Log("PostProcessingQueue.Stop()");
            run = false;
        }

        public static async Task Update ()
        {
            while (run || AnyFilesLeft())
            {
                if (ncnn) CheckNcnnOutput();
                //IOUtils.RenameExtensions(Paths.imgOutPath, "png", "tmp", true, "*.*.png");
                string[] outFiles = Directory.GetFiles(Paths.imgOutPath, "*.tmp", SearchOption.AllDirectories);
                Logger.Log("Queue Update() - " + outFiles.Length + " files in out folder");
                foreach (string file in outFiles)
                {
                    if (!outputFileQueue.Contains(file) && !processedFiles.Contains(file) && !outputFiles.Contains(file))
                    {
                        //processedFiles.Add(file);
                        outputFileQueue.Enqueue(file);
                        Logger.Log("[Queue] Enqueued " + Path.GetFileName(file));
                    }
                    else
                    {
                        Logger.Log("Skipped " + file + " - Is In Queue: " + outputFileQueue.Contains(file) + " - Is Processed: " + processedFiles.Contains(file) + " - Is Outfile: " + outputFiles.Contains(file));
                    }
                }
                await Task.Delay(1000);
            }
        }

        static bool AnyFilesLeft ()
        {
            if (IOUtils.GetAmountOfFiles(Paths.imgOutPath, true) > 0)
                return true;
            if (ncnn && IOUtils.GetAmountOfFiles(Paths.imgOutNcnnPath, true) > 0)
                return true;
            return false;
        }

        static void CheckNcnnOutput ()
        {
            try
            {
                IOUtils.RenameExtensions(Paths.imgOutNcnnPath, "png", "tmp", true);
                IOUtils.Copy(Paths.imgOutNcnnPath, Paths.imgOutPath, "*", true);
            }
            catch { }
        }

        public static string lastOutfile;

        public static async Task ProcessQueue ()
        {
            Stopwatch sw = new Stopwatch();
            while (run || AnyFilesLeft())
            {
                if (outputFileQueue.Count > 0)
                {
                    string file = outputFileQueue.Dequeue();
                    Logger.Log("[Queue] Post-Processing " + Path.GetFileName(file));
                    sw.Restart();
                    await Upscale.PostprocessingSingle(file, true);
                    string outFilename = Upscale.FilenamePostprocess(lastOutfile);
                    outputFiles.Add(outFilename);
                    Logger.Log("[Queue] Done Post-Processing " + Path.GetFileName(file) + " in " + sw.ElapsedMilliseconds + "ms");

                    if(Upscale.overwriteMode == Upscale.Overwrite.Yes)
                    {
                        string suffixToRemove = "-" + Program.lastModelName.Replace(":", ".").Replace(">>", "+");
                        if (copyMode == CopyMode.KeepStructure)
                        {
                            string combinedPath = currentOutPath + outFilename.Replace(Paths.imgOutPath, "");
                            Directory.CreateDirectory(combinedPath.GetParentDir());
                            File.Copy(outFilename, combinedPath.ReplaceInFilename(suffixToRemove, "", true));
                        }
                        if (copyMode == CopyMode.CopyToRoot)
                        {
                            File.Copy(outFilename, Path.Combine(currentOutPath, Path.GetFileName(outFilename).Replace(suffixToRemove, "")), true);
                        }
                        File.Delete(outFilename);
                    }
                    else
                    {
                        if (copyMode == CopyMode.KeepStructure)
                        {
                            string combinedPath = currentOutPath + outFilename.Replace(Paths.imgOutPath, "");
                            Directory.CreateDirectory(combinedPath.GetParentDir());
                            File.Copy(outFilename, combinedPath, true);
                        }
                        if (copyMode == CopyMode.CopyToRoot)
                        {
                            File.Copy(outFilename, Path.Combine(currentOutPath, Path.GetFileName(outFilename)), true);
                        }
                        File.Delete(outFilename);
                    }
                    BatchUpscaleUI.upscaledImages++;
                }
                await Task.Delay(250);
            }
        }
    }
}
