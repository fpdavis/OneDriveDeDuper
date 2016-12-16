using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CommandLine;
using CommandLine.Text;
using System.Configuration;
using System.Linq;

namespace OneDriveDeDuper
{
    class Program
    {
        protected const int giVerbosity_Debug = 5;       // Debug (5): Super detail
        protected const int giVerbosity_Verbose = 4;     // Verbose (4): Almost everything
        protected const int giVerbosity_Information = 3; // Information (3): Information that might be useful to user
        protected const int giVerbosity_Warning = 2;     // Warning (2): Bad things that happen, but are expected
        protected const int giVerbosity_Error = 1;       // Error (1): Errors that are recoverable
        protected const int giVerbosity_Critical = 0;    // Critical (0): Errors that stop execution

        static private List<string> gaOneDriveComputers;
        static private List<string> gaIdentifyPCs = new List<string>();
        static private Options goOptions;
        static private int giFilesMoved;
        static private int giFilesRemoved;
        static private int giDirectoriesExampined;

        private static string _gsOneDrivePath;
        private static string gsOneDrivePath
        {
            get
            {
                if (_gsOneDrivePath == null)
                {
                    _gsOneDrivePath = (string)Microsoft.Win32.Registry.GetValue(ConfigurationManager.AppSettings["OneDriveRegistryKeyName"], ConfigurationManager.AppSettings["OneDriveRegistryUserFolder"], "OneDrive Registry Key could not be found. Check App.config.");
                }

                return _gsOneDrivePath;
                }
            set { }
        }

       private class Options
        {
            [Option('v', "Verbosity", DefaultValue = giVerbosity_Error, HelpText = 
                  "The amount of verbosity to use. [-v 4]       "
                + "\u0007                                                          "
                + "\u0007      Debug (5): Super detail                             "
                + "\u0007    Verbose (4): Almost everything                        "
                + "Information (3): Generally useful information             "
                + "\u0007    Warning (2): Expected problems                        "
                + "\u0007      Error (1): Errors that are recoverable              "
                + "\u0007   Critical (0): Errors that stop execution.")]
            public int iVerbosity { get; set; }

            [Option('d', "Display", DefaultValue = false, HelpText = "Displays what would happen but doesn't actually move or delete files. [-d]")]
            public Boolean bDisplay { get; set; }

            [Option('i', "Identify", DefaultValue = false, HelpText = "Identifies POSSIBLE PC names with file conflicts synced to this computer. The list WILL contain false positives. Go to https://onedrive.live.com/prev?v=DevicesView for an accurate list of currently syncing PC names. [-i]")]
            public Boolean bIdentifyPCs { get; set; }

            [Option('o', "Orphans", DefaultValue = false, HelpText = "Remove orphaned files. [-o]")]
            public Boolean bRemoveOrphans { get; set; }

            [HelpOption]
            public string GetUsage()
            {               
                var oHelpText = HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
                oHelpText.AddPreOptionsLine("Usage: OneDriveDeDuper [-v x] [-d true] [-i]");

                return oHelpText;

            }
        }

        static void Main(string[] args)
        {
            goOptions = new Options();
            if (CommandLine.Parser.Default.ParseArguments(args, goOptions))
            {
                if (goOptions.bDisplay)
                {
                    WriteLog("Displaying results only, no files will be moved or deleted.", giVerbosity_Information);
                }

                WriteLog("    OneDrive directory: " + gsOneDrivePath, giVerbosity_Information);
                
                if (goOptions.bIdentifyPCs)
                {
                    gaOneDriveComputers = new List<string>(new string[] { @"-[a-zA-z][a-zA-z0-9]+" });
                }
                else
                {
                    //gaOneDriveComputers = new List<string>(ConfigurationManager.AppSettings["OneDriveComputers"].Split(new char[] { ',' }));
                    gaOneDriveComputers = (ConfigurationManager.AppSettings["OneDriveComputers"]).Split(new char[] { ',' }).Select(p => p.Trim()).ToList();

                    WriteLog(" Checking OneDrive PCs: " + string.Join(", ", gaOneDriveComputers), giVerbosity_Information);

                    // Add the begining dash to the computer name
                    for (int iLoop = gaOneDriveComputers.Count - 1; iLoop >= 0; iLoop--)
                    {
                        gaOneDriveComputers[iLoop] = "-" + gaOneDriveComputers[iLoop];
                    }
                }

                TraverseDirectory(gsOneDrivePath);

                WriteLog("  Directories Examined: " + giDirectoriesExampined, giVerbosity_Information);
                WriteLog("           Files Moved: " + giFilesMoved, giVerbosity_Information);
                WriteLog("         Files Removed: " + giFilesRemoved, giVerbosity_Information);
                WriteLog("        PCs Identified: " + string.Join(", ", gaIdentifyPCs), giVerbosity_Information);
            }
        }

        static private void TraverseDirectory(string sDirectoryPath)
        {
            try
            {
                giDirectoriesExampined++;

                List<string> saEnumerateDirectories = new List<string>(System.IO.Directory.EnumerateDirectories(sDirectoryPath));
                Regex oRegexPCName = new Regex("^[a-zA-Z][a-zA-Z0-9]+$");
                int iExtenionDotIndex;

                foreach (var sDirectory in saEnumerateDirectories)
                {
                    TraverseDirectory(sDirectory);
                    
                    string[] aGetFiles = Directory.GetFiles(sDirectory);

                    string sPattern;
                    string sLocalFileName;

                    foreach (string sFileName in aGetFiles)
                    {
                        foreach (string sComputer in gaOneDriveComputers)
                        {
                            sPattern = sComputer + @"(-\d+)*(\.|$)";
                            Regex oRegex = new Regex(sPattern);

                            if (oRegex.IsMatch(sFileName))
                            {
                                sLocalFileName = oRegex.Replace(sFileName, "$2");

                                DateTime dtFileNameLastWriteTime = File.GetLastWriteTime(sFileName);

                                if (File.Exists(sLocalFileName))
                                {
                                    string sPC = sFileName.Substring(sFileName.LastIndexOf("-") + 1);
                                    iExtenionDotIndex = sPC.LastIndexOf(".");

                                    if (iExtenionDotIndex > 0)
                                    {
                                        sPC = sPC.Substring(0, iExtenionDotIndex);
                                    }

                                    if (!gaIdentifyPCs.Contains(sPC) && oRegexPCName.IsMatch(sPC))
                                    {
                                        gaIdentifyPCs.Add(sPC);
                                    }


                                    if (!goOptions.bIdentifyPCs)
                                    {

                                        DateTime dtLocalFileNameLastWriteTime = File.GetLastWriteTime(sLocalFileName);

                                        try
                                        {
                                            if (dtLocalFileNameLastWriteTime < dtFileNameLastWriteTime)
                                            {
                                                WriteLog("Replacing " + dtLocalFileNameLastWriteTime + " - " + sLocalFileName + " with " + dtFileNameLastWriteTime + " - " + sFileName.Substring(sFileName.LastIndexOf("\\") + 1), giVerbosity_Verbose);

                                                if (!goOptions.bDisplay)
                                                {
                                                    File.Delete(sLocalFileName);
                                                    File.Move(sFileName, sLocalFileName);

                                                    giFilesMoved++;
                                                }
                                            }
                                            else
                                            {
                                                WriteLog("Removing " + dtFileNameLastWriteTime + " - " + sFileName, giVerbosity_Verbose);

                                                if (!goOptions.bDisplay)
                                                {
                                                    File.Delete(sFileName);

                                                    giFilesRemoved++;
                                                }
                                            }
                                        }
                                        catch (Exception oException)
                                        {
                                            WriteLog(oException.Message, giVerbosity_Error);
                                        }
                                    }
                                }
                                else if (!goOptions.bIdentifyPCs)
                                {
                                    if (goOptions.bRemoveOrphans)
                                    {
                                        WriteLog("Removing Orphan " + dtFileNameLastWriteTime + " - " + sFileName, giVerbosity_Verbose);

                                        if (!goOptions.bDisplay)
                                        {
                                            File.Delete(sFileName);

                                            giFilesRemoved++;
                                        }
                                    }
                                    else
                                    {
                                        WriteLog("Orphan " + dtFileNameLastWriteTime + " - " + sFileName, giVerbosity_Warning);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception oException )
            {
                WriteLog(oException.Message, giVerbosity_Critical);
            }
        }

        static private void WriteLog(string sMessage, int iVerbosity = giVerbosity_Information)
        {
            if (goOptions.iVerbosity >= iVerbosity)
            {
                Console.WriteLine(sMessage);
            }
        }
    }
}
