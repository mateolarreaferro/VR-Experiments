/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using static Meta.XR.Simulator.Utils;
using Unity.EditorCoroutines.Editor;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Meta.XR.Simulator.Editor.SyntheticEnvironments
{
    public class XrSimEditorWindow : EditorWindow
    {
        private const string Name = "Synthetic Environments";

        // Only use the window with MacOS for now
#if UNITY_EDITOR_OSX
        [MenuItem(SyntheticEnvironmentServer.MenuPath + "/" + Name)]
        static void LoadWindow()
        {
            // ReportInfo(Name, "Loading Window");
            XrSimEditorWindow window = GetWindow<XrSimEditorWindow>();
            window.ensureInstallation();
        }
#endif // UNITY_EDITOR_OSX

        Button startButton_;
        void CreateGUI()
        {
            // ReportInfo(Name, "Creating GUI");
            this.titleContent = new GUIContent(Name);
        }

        bool isInstalled_ = false;

#if META_XR_SDK_CORE_72_OR_NEWER
        const string Version = "v72";
#else
        const string Version = "v71";
#endif // META_XR_SDK_CORE_72_OR_NEWER

#if UNITY_EDITOR_OSX
        readonly static string AppDataFolderPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
#else
        readonly static string AppDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#endif
        readonly static string XrSimDataFolderPath = Path.Join(Path.Join(AppDataFolderPath, "MetaXR", "MetaXrSimulator"), Version);

        void ensureInstallation()
        {
            // ReportInfo(Name, "checking for " + XrSimDataFolderPath);

            if (Directory.Exists(XrSimDataFolderPath))
            {
                setInstalled(true);
                // Start the coroutine we define below
                ReportInfo(Name, XrSimDataFolderPath + " found, using it");
                return;
            }

            ReportError(Name, "failed to find " + XrSimDataFolderPath);
            setInstalled(false);

            // ask user if they want to pull the package
            bool installPackage = EditorUtility.DisplayDialog(Name,
                "Do you want to install the Synthetic Environments package?",
            "Ok", "Cancel");

            if (installPackage)
            {
                // #if META_XR_SDK_CORE_SUPPORTS_TELEMETRY
                // marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                // #endif
                this.StartCoroutine(installSesPackage(XrSimDataFolderPath));
            }
        }

#if UNITY_EDITOR_OSX
        readonly static Regex EnvScriptArgs = new Regex(@"open -n (\S+) --args (\S+)");
#else
        readonly static Regex EnvScriptArgs = new Regex(@"start (\S+) (\S+)");
#endif
        SyntheticEnvironment parseSynthEnvScript(string file)
        {
            StreamReader reader = File.OpenText(file);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // ReportInfo(Name, "checking " + line);
                var match = EnvScriptArgs.Match(line);
                if (match.Success)
                {
                    ReportInfo(Name, "got match " + match);
                    return new SyntheticEnvironment()
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        ServerBinaryPath = match.Groups[1].Value,
                        InternalName = match.Groups[2].Value,
                    };
                }
            }
            return null;
        }

        void populateUI()
        {
            var synthEnvs = new List<SyntheticEnvironment>();

            // Find all scripts in the package and create a SyntheticEnvironment for each\
            var subDirs = Directory.EnumerateDirectories(XrSimDataFolderPath);
            foreach (var dir in subDirs)
            {
#if UNITY_EDITOR_OSX
                var files = Directory.EnumerateFiles(dir, "*.sh", SearchOption.AllDirectories);
#else
                var files = Directory.EnumerateFiles(dir, "*.bat", SearchOption.AllDirectories);
#endif
                foreach (var file in files)
                {
                    // ReportInfo(Name, "found " + file);
                    // Parse the file
                    var env = parseSynthEnvScript(file);
                    if (env == null) { continue; }

                    env.ServerBinaryPath = Path.Join(dir, env.ServerBinaryPath);
                    // ReportInfo(Name, "parsed " + env);
                    synthEnvs.Add(env);
                }
            }

            foreach (var synthEnv in synthEnvs)
            {
                var button = new Button();
                button.text = synthEnv.Name;
                button.RegisterCallback<MouseUpEvent>((evt) =>
                {
                    // ReportInfo(Name, "clicked " + synthEnv.Name + ", running " + synthEnv.ServerBinaryPath);
                    synthEnv.Launch();
                });
                rootVisualElement.Add(button);
                // TODO: figure out how to make this more robust
                Registry.Register(synthEnv);
            }
            this.Show();
        }

        private void setInstalled(bool installed)
        {
            isInstalled_ = installed;

            if (isInstalled_)
            {
                // ReportInfo(Name, "Opening Window");
                this.Show();
                this.populateUI();
            }
        }

        private UnityWebRequest webRequest_ = null;

        IEnumerator installSesPackage(string installDir)
        {
#if UNITY_EDITOR_OSX
            const string Platform = "MAC";
#else
            const string Platform = "WIN";
#endif

            var DownloadMap = new[] {
                new[]{"v71", "WIN", "8400220880096047"},
                new[]{"v71", "MAC", "27283409131273921"}
            };

            string downloadId = "";
            foreach (var e in DownloadMap)
            {
                if (e[0] == Version && e[1] == Platform)
                {
                    downloadId = e[2];
                    break;
                }
            }

            if (downloadId == "")
            {
                DisplayDialogOrError(Name, "failed to find downloadId for " + Version + " " + Platform);
                yield break;
            }

            // following https://discussions.unity.com/t/downloadhandlerbuffer-data-gc-allocation-problems/704758/8
            string accessToken = "OC%7C1592049031074901%7C";
            string url = string.Format("https://securecdn.oculus.com/binaries/download/?id={0}&access_token={1}", downloadId, accessToken);

            // TODO: T203804698 save to Downloads folder instead of temp folder...
            string savePath = Path.Join(Path.GetTempPath(), downloadId + ".zip");

            // check if file exists before downloading it again
            if (!File.Exists(savePath))
            {
                int progressId = Progress.Start(Name);
                Progress.ShowDetails(false);
                yield return null;

                webRequest_ = UnityWebRequest.Get(url);
                var handler = new DownloadHandlerFile(savePath);
                webRequest_.downloadHandler = handler;
                handler.removeFileOnAbort = true;

                // ReportInfo(Name, "starting to download " + url);
                UnityWebRequestAsyncOperation operation = webRequest_.SendWebRequest();
                operation.completed += operation =>
                {
                    ReportInfo(Name, "finished downloading " + url);
                };

                while (!webRequest_.downloadHandler.isDone)
                {
                    Progress.Report(progressId, webRequest_.downloadProgress, "Downloading package");
                    yield return null;
                }

                if (webRequest_.result != UnityWebRequest.Result.Success)
                {
                    DisplayDialogOrError(Name, webRequest_.error);
                    yield break;
                }

                ReportInfo(Name, "finished saving data to " + savePath);

                webRequest_ = null;
                Progress.Remove(progressId);
            }
            else
            {
                ReportInfo(Name, "found " + savePath + ", skipping download");
            }

            // ensure normalized path
            if (!installDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                installDir += Path.DirectorySeparatorChar;
            }

            if (Directory.Exists(installDir))
            {
                // Ensure directory is deleted before extracting
                Directory.Delete(installDir);
            }

            // ReportInfo(Name, "extracting " + savePath + " to " + installDir);
            // unzip
            using (ZipArchive archive = ZipFile.OpenRead(savePath))
            {
                int progressId = Progress.Start(Name);
                Progress.ShowDetails(false);
                yield return null;

                int numEntries = archive.Entries.Count;
                float entryIndex = -1;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    entryIndex++;
                    if (entry.FullName.EndsWith("/"))
                    {
                        continue;
                    }

                    // Gets the full path to ensure that relative segments are removed.
                    string destinationPath = Path.GetFullPath(Path.Combine(installDir, entry.FullName));

                    // Ordinal match is safest, case-sensitive volumes can be mounted within volumes that
                    // are case-insensitive.
                    if (!destinationPath.StartsWith(installDir, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // create directory if it doesn't exist
                    var parentDir = Path.GetDirectoryName(destinationPath);
                    if (!Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    entry.ExtractToFile(destinationPath, true);

#if UNITY_EDITOR_OSX
                        // Get the file attributes for file
                    string attrString = Convert.ToString((entry.ExternalAttributes >> 16), 8);
                    string subString = attrString.Substring(attrString.Length - 4);
                    var (retCode, contents) = executeProcess("chmod", new string[] { subString, destinationPath });
                    if(retCode != 0)
                    {
                        ReportError(Name, "failed to set permissions on " + destinationPath + ", retCode:" + retCode + ", contents:" + contents);
                    }
#endif
                    // ReportInfo(Name, "Extracted File:" + entry.FullName + ", ExternalAttributes:0" + Convert.ToString((entry.ExternalAttributes >> 16), 8));
                    Progress.Report(progressId, entryIndex / numEntries, "Extraction progress");
                    yield return null;
                }

                Progress.Remove(progressId);
            }

            ReportInfo(Name, "finished extracting " + savePath + " to " + installDir);

            // NOTE: this is not working right now but doesn't seem to be needed
            // #if UNITY_EDITOR_OSX
            //             {
            //                 const string Attribute = "com.apple.provenance";
            //                 var (retCode, contents) = executeProcess("xattr", new string[] { "-rd", Attribute, installDir });
            //                 if(retCode != 0)
            //                 {
            //                     ReportError(Name, string.Format("failed to remove {0}, retCode={1}, contents={2}", Attribute, retCode, contents));
            //                 }
            //             }
            // #endif

            setInstalled(true);
        }

        static (int retCode, string contents) executeProcess(string path, string[] args)
        {
            using (Process p = new Process())
            {
                var ps = new ProcessStartInfo();

                ps.Arguments = Utils.EscapeArguments(args);
                ps.FileName = path;
                ps.UseShellExecute = false;
                ps.WindowStyle = ProcessWindowStyle.Hidden;
                ps.RedirectStandardInput = true;
                ps.RedirectStandardOutput = true;
                ps.RedirectStandardError = true;

                // ReportInfo(Name, "Executing: " + path + " " + ps.Arguments);

                p.StartInfo = ps;
                p.Start();

                StreamReader stdOutput = p.StandardOutput;
                StreamReader stdError = p.StandardError;

                string content = stdOutput.ReadToEnd() + stdError.ReadToEnd();
                p.WaitForExit();
                int retCode = p.ExitCode;
                return (retCode, content);
            }
        }
    }
}
