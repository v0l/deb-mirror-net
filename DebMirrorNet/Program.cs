using DebNet;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DebMirrorNet
{
    class Program
    {
        public static double PiB = Math.Pow(1024, 5);
        public static double TiB = Math.Pow(1024, 4);
        public static double GiB = Math.Pow(1024, 3);
        public static double MiB = Math.Pow(1024, 2);
        public static double KiB = 1024d;

        public static double PB = Math.Pow(1000, 5);
        public static double TB = Math.Pow(1000, 4);
        public static double GB = Math.Pow(1000, 3);
        public static double MB = Math.Pow(1000, 2);
        public static double KB = 1000d;

        public static double PBit = PB * 8d;
        public static double TBit = TB * 8d;
        public static double GBit = GB * 8d;
        public static double MBit = MB * 8d;
        public static double KBit = KB * 8d;

        /// <summary>
        /// Mirror a Debian repo
        /// </summary>
        /// <param name="source">Mirror source url</param>
        /// <param name="cachePath">Local path to store cache</param>
        /// <param name="checkMode">Verify mode for local files</param>
        /// <param name="bwLimit">Download bandwidth limit (in Mbps)</param>
        /// <param name="dists">Distributions to mirror</param>
        /// <param name="arch">Architectures to mirror</param>
        /// <param name="verbose">Print more logs</param>
        static async Task Main(string source, string cachePath, FileCheckMode checkMode = FileCheckMode.ReleaseDate, double bwLimit = 0d, List<string> dists = null, List<string> arch = null, bool verbose = false)
        {
            if ((dists?.Count ?? 0) == 0)
            {
                var distChannels = new string[]
                {
                    string.Empty,
                    "-backports",
                    "-proposed",
                    "-security",
                    "-updates"
                };
                dists = new List<string>()
                {
                    "bionic",
                    "eoan",
                    "focal",
                    "groovy",
                    "precise",
                    "trusty",
                    "xenial"
                }.SelectMany(a => distChannels.Select(b => $"{a}{b}")).ToList();
            }

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new CustomLoggerProvider()).SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger<Program>();
            using (logger.BeginScope("Init"))
            {
                var sem = new SemaphoreSlim(Environment.ProcessorCount);
                logger.LogDebug($"Using {Environment.ProcessorCount} threads");

                var repoUrl = new Uri(source);
                var repo = new Repository(logger, repoUrl);
                var fu = new FileUtil(logger);

                var stat = new Stats()
                {
                    LastStart = DateTime.UtcNow
                };

                foreach (var distChan in dists)
                {
                    using (logger.BeginScope(distChan))
                    {
                        var riStream = await repo.GetReleaseInfo(distChan);
                        if (riStream.HasValue)
                        {
                            var releaseTemp = Path.GetTempFileName();
                            var releaseFilePath = Path.Combine(cachePath, riStream.Value.Filename);
                            
                            await CopyStreamToFile(releaseTemp, riStream.Value.Stream, bwLimit * MBit);
                            ReleaseInfo releaseInfo;
                            using (var srTemp = new StreamReader(releaseTemp)) 
                            {
                                releaseInfo = await ReleaseInfo.ReadFromStream(new Uri(repoUrl, riStream.Value.Filename), srTemp);
                            }

                            if (!stat.Dists.ContainsKey(distChan))
                            {
                                stat.Dists.Add(distChan, new DistStats()
                                {
                                    ReleaseDate = releaseInfo.Date
                                });
                            }

                            //Check for updates if release cache already exists
                            if (File.Exists(releaseFilePath) && checkMode == FileCheckMode.ReleaseDate)
                            {
                                using var srOld = new StreamReader(releaseFilePath);
                                var oldReleaseInfo = await ReleaseInfo.ReadFromStream(new Uri(repoUrl, riStream.Value.Filename), srOld);
                                if (oldReleaseInfo.Date == releaseInfo.Date)
                                {
                                    logger.LogInformation($"{distChan} is up to date.");
                                    continue;
                                }
                            }

                            logger.LogInformation($"Updating: {distChan}");
                            using (logger.BeginScope("Contents"))
                            {
                                //Clone contents
                                foreach (var arc in (arch ?? releaseInfo.Architectures))
                                {
                                    foreach (var contentFile in repo.GetCompressedOrderdFiles(releaseInfo, $"Content-{arc}"))
                                    {
                                        await sem.WaitAsync();
                                        _ = Task.Run(async () =>
                                        {
                                            var contentPath = $"dists/{distChan}/{releaseInfo.MakeByHashUri(contentFile.Key) ?? contentFile.Key}";
                                            var outPath = Path.Combine(cachePath, contentPath);
                                            var fetch = !await CheckFile(outPath, contentFile.Value, checkMode);
                                            logger.LogDebug($"[{distChan}][{arc}] [{(!fetch ? "✓" : "🗙")}] {contentFile.Key}");

                                            if (fetch)
                                            {
                                                var contentStream = await fu.GetStream(new Uri(repoUrl, contentPath), Path.GetExtension(contentFile.Key), false);
                                                if (contentStream != null)
                                                {
                                                    logger.LogInformation($"[{distChan}][{arc}] [↓] {contentFile.Key}");
                                                    await CopyStreamToFile(outPath, contentStream, bwLimit * MBit);
                                                    if (!await CheckFile(outPath, contentFile.Value, checkMode))
                                                    {
                                                        logger.LogError($"[Corrupt] {contentFile.Key}");
                                                    }
                                                }
                                            }
                                            sem.Release();
                                        });

                                    }
                                }
                            }

                            using (logger.BeginScope("Translations"))
                            {
                                //copy translations
                                foreach (var comp in releaseInfo.Components)
                                {
                                    foreach (var txFile in repo.GetCompressedOrderdFiles(releaseInfo, $"{comp}/i18n"))
                                    {
                                        await sem.WaitAsync();
                                        _ = Task.Run(async () =>
                                        {
                                            //http://archive.ubuntu.com/ubuntu/dists/bionic/main/i18n/
                                            var contentPath = $"dists/{distChan}/{releaseInfo.MakeByHashUri(txFile.Key) ?? txFile.Key}";
                                            var outPath = Path.Combine(cachePath, contentPath);
                                            var fetch = !await CheckFile(outPath, txFile.Value, checkMode);
                                            logger.LogDebug($"[{distChan}] [{(!fetch ? "✓" : "🗙")}] {txFile.Key}");

                                            if (fetch)
                                            {
                                                var contentStream = await fu.GetStream(new Uri(repoUrl, contentPath), Path.GetExtension(txFile.Key), false);
                                                if (contentStream != null)
                                                {
                                                    logger.LogInformation($"[{distChan}][{comp}] [↓] {txFile.Key}");
                                                    await CopyStreamToFile(outPath, contentStream, bwLimit * MBit);
                                                    if (!await CheckFile(outPath, txFile.Value, checkMode))
                                                    {
                                                        logger.LogError($"[Corrupt] {txFile.Key}");
                                                    }
                                                }
                                            }
                                            sem.Release();
                                        });
                                    }
                                }
                            }

                            var lastStatWrite = DateTime.Now;
                            //Read pacakges
                            foreach (var arc in (arch ?? releaseInfo.Architectures))
                            {
                                foreach (var comp in releaseInfo.Components)
                                {
                                    using (logger.BeginScope($"{distChan}/{arc}/{comp}"))
                                    {
                                        if (!stat.Dists[distChan].Components.ContainsKey(comp))
                                        {
                                            stat.Dists[distChan].Components.Add(comp, new CompStats());
                                        }

                                        var pkgIndexStream = await repo.GetPackageIndex(releaseInfo, comp, arc, false);
                                        if (pkgIndexStream.HasValue)
                                        {
                                            var outPath = Path.Combine(cachePath, $"dists/{distChan}/{releaseInfo.MakeByHashUri(pkgIndexStream.Value.Filename) ?? pkgIndexStream.Value.Filename}");
                                            await CopyStreamToFile(outPath, pkgIndexStream.Value.Stream, bwLimit * MBit);

                                            //read the package index
                                            var pkgIndex = await fu.GetStream(new Uri(Path.GetFullPath(outPath)), Path.GetExtension(pkgIndexStream.Value.Filename));
                                            if (pkgIndex != null)
                                            {
                                                await foreach (var pkg in repo.ReadPackageIndex(pkgIndex))
                                                {
                                                    var compStat = stat.Dists[distChan].Components[comp];

                                                    await sem.WaitAsync();
                                                    _ = Task.Run(async () =>
                                                      {
                                                          var pkgOutPath = Path.Combine(cachePath, pkg.Filename);
                                                          var fetch = !await CheckFile(pkgOutPath, pkg.AsReleaseFile(), checkMode);
                                                            logger.LogDebug($"[{distChan}][{arc}][{comp}] [{(!fetch ? "✓" : "🗙")}] {pkg.Name}");

                                                          if (fetch)
                                                          {
                                                              var pkgUrl = new Uri(repoUrl, pkg.Filename);
                                                              logger.LogInformation($"[{distChan}][{arc}][{comp}] [↓] {pkg.Name}");
                                                              var pkgStream = await fu.GetStream(pkgUrl, getDecompressed: false);
                                                              if (pkgStream != null)
                                                              {
                                                                  await CopyStreamToFile(pkgOutPath, pkgStream, bwLimit * MBit);
                                                                  if (!await CheckFile(pkgOutPath, pkg.AsReleaseFile(), checkMode))
                                                                  {
                                                                      logger.LogError($"[Corrupt] {pkg.Name}");
                                                                  }
                                                              }
                                                          }

                                                          sem.Release();
                                                          compStat.Packages++;
                                                          compStat.Size += pkg.Size;
                                                          if ((DateTime.Now - lastStatWrite).TotalSeconds >= 5)
                                                          {
                                                              await WriteStat(cachePath, stat);
                                                              lastStatWrite = DateTime.Now;
                                                          }
                                                      });
                                                }
                                            }
                                        }
                                        else
                                        {
                                            logger.LogError($"Cant open PackageIndex: {arc}/{comp}");
                                        }
                                    }
                                }
                            }

                            // extra check on dir here
                            // CopyStreamToFile normally does this check, but for first run its needed
                            var releaseDir = Path.GetDirectoryName(releaseFilePath);
                            if (!Directory.Exists(releaseDir))
                            {
                                Directory.CreateDirectory(releaseDir);
                            }
                            File.Move(releaseTemp, releaseFilePath, true);
                        }
                        else
                        {
                            logger.LogError($"Dist not found: {distChan}");
                        }
                    }
                }

                //write repo lastly
                await WriteStat(cachePath, stat, false);
            }
        }

        static async Task WriteStat(string cacheRoot, Stats s, bool running = true)
        {
            s.Running = running;
            if (running)
            {
                s.Runtime = DateTime.UtcNow - s.LastStart;
            }
            await File.WriteAllTextAsync(Path.Combine(cacheRoot, "repo.json"), JsonConvert.SerializeObject(s, Formatting.Indented));
        }

        static async Task CopyStreamToFile(string path, Stream stream, double bwLimit = 0)
        {
            var dirName = Path.GetDirectoryName(path);
            if (!Directory.Exists(dirName))
            {
                Directory.CreateDirectory(dirName);
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            if (bwLimit == 0)
            {
                await stream.CopyToAsync(fs);
            }
            else
            {
                var buff = new byte[(int)KiB];
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    sw.Restart();
                    var rlen = await stream.ReadAsync(buff, 0, buff.Length);
                    if (rlen == 0) break;
                    await fs.WriteAsync(buff, 0, rlen);
                    var txTime = sw.Elapsed;

                    var delay = TimeSpan.FromSeconds((rlen * 8d) / bwLimit) - txTime;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay);
                    }
                }
            }
        }

        static async Task<bool> CheckFile(string path, ReleaseFileInfo info, FileCheckMode mode = FileCheckMode.SHA256)
        {
            if (File.Exists(path))
            {
                Func<string, HashAlgorithm, Task<string>> fnHashFile = async (path, algo) =>
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                    var data = new byte[1024 * 16]; //read 16k at a time
                while (true)
                    {
                        var rlen = await fs.ReadAsync(data, 0, data.Length);
                        if (rlen == 0)
                        {
                            algo.TransformFinalBlock(data, 0, rlen);
                            break;
                        }
                        else
                        {
                            algo.TransformBlock(data, 0, rlen, data, 0);
                        }
                    }
                    return BitConverter.ToString(algo.Hash).Replace("-", "").ToLower();
                };

                //fallback
                if (mode == FileCheckMode.SHA256 && string.IsNullOrEmpty(info.SHA256))
                {
                    mode = FileCheckMode.SHA1;
                }
                if (mode == FileCheckMode.SHA1 && string.IsNullOrEmpty(info.SHA1))
                {
                    mode = FileCheckMode.MD5;
                }
                if (mode == FileCheckMode.MD5 && string.IsNullOrEmpty(info.MD5))
                {
                    mode = FileCheckMode.Size;
                }
                switch (mode)
                {
                    case FileCheckMode.ReleaseDate:
                    case FileCheckMode.Size:
                        {
                            var fi = new FileInfo(path);
                            return fi.Length == info.Size;
                        }
                    case FileCheckMode.MD5:
                        {
                            return info.MD5 == await fnHashFile(path, MD5.Create());
                        }
                    case FileCheckMode.SHA1:
                        {
                            return info.SHA1 == await fnHashFile(path, SHA1.Create());
                        }
                    case FileCheckMode.SHA256:
                        {
                            return info.SHA256 == await fnHashFile(path, SHA256.Create());
                        }
                }
            }
            return false;
        }
    }

    internal enum FileCheckMode
    {
        ReleaseDate,
        Size,
        MD5,
        SHA1,
        SHA256
    }

    internal class Stats
    {
        public DateTime LastStart { get; set; }

        public bool Running { get; set; } = true;

        public TimeSpan Runtime { get; set; }

        public Dictionary<string, DistStats> Dists { get; set; } = new Dictionary<string, DistStats>();
    }

    internal class DistStats
    {
        public DateTime ReleaseDate { get; set; }

        public Dictionary<string, CompStats> Components { get; set; } = new Dictionary<string, CompStats>();
    }

    internal class CompStats
    {
        public long Packages { get; set; } = 0;
        public long Size { get; set; } = 0;
        public string HumanSize => FormatBytes(Size);

        public static string FormatBytes(long b)
        {
            if (b >= Program.PiB)
            {
                return $"{b / Program.PiB:#,##0.00} {nameof(Program.PiB)}";
            }
            else if (b >= Program.TiB)
            {
                return $"{b / Program.TiB:#,##0.00} {nameof(Program.TiB)}";
            }
            else if (b >= Program.GiB)
            {
                return $"{b / Program.GiB:#,##0.00} {nameof(Program.GiB)}";
            }
            else if (b >= Program.MiB)
            {
                return $"{b / Program.MiB:#,##0.00} {nameof(Program.MiB)}";
            }
            else if (b >= Program.KiB)
            {
                return $"{b / Program.KiB:#,##0.00} {nameof(Program.KiB)}";
            }
            return $"{b:#,##0.00} B";
        }
    }
}
