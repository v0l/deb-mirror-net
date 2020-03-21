using DebNet;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DebMirrorNet
{
    class Program
    {
        static Task Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("DeMirrorNet <source> <path>");
                return Task.CompletedTask;
            }
            Console.WriteLine($"Using source: {args[0]}");
            Console.WriteLine($"Mirror to path: {args[1]}");

            return Task.Run(async () =>
            {
                var cacheRoot = args[1];

                var dists = new string[]
                {
                    "bionic",
                    "disco",
                    "eoan",
                    "focal",
                    "precise",
                    "trusty",
                    "xenial"
                };
                var distChannels = new string[]
                {
                    string.Empty,
                    "-backports",
                    "-proposed",
                    "-security",
                    "-updates"
                };

                var sem = new SemaphoreSlim(Environment.ProcessorCount);
                Console.WriteLine($"Using {Environment.ProcessorCount} threads");
                var repoUrl = new Uri(args[0]);
                var repo = new Repository(repoUrl);

                foreach (var d in dists)
                {
                    foreach (var dc in distChannels)
                    {
                        var distChan = $"{d}{dc}";
                        var riStream = await repo.GetReleaseInfo(distChan);
                        if (riStream.HasValue)
                        {
                            var releaseFilePath = Path.Combine(cacheRoot, riStream.Value.Filename);
                            await CopyStreamToFile(releaseFilePath, riStream.Value.Stream);
                            var releaseInfo = await ReleaseInfo.ReadFromStream(new Uri(repoUrl, riStream.Value.Filename), new StreamReader(releaseFilePath));

                            Console.WriteLine($"Cloning: {releaseInfo.Description}");

                            //Clone contents
                            foreach (var arch in releaseInfo.Architectures)
                            {
                                Console.WriteLine($"Fetching contents: {arch}");
                                foreach (var contentFile in repo.GetCompressedOrderdFiles(releaseInfo, $"Content-{arch}"))
                                {
                                    await sem.WaitAsync();
                                    _ = Task.Run(async () =>
                                    {
                                        var contentPath = $"dists/{distChan}/{releaseInfo.MakeByHashUri(contentFile.Key) ?? contentFile.Key}";
                                        var outPath = Path.Combine(cacheRoot, contentPath);
                                        var fetch = true;
                                        if (File.Exists(outPath))
                                        {
                                            fetch = !await CheckFileHash(outPath, contentFile.Value.SHA256);
                                            Console.WriteLine($"[{distChan}][{arch}] [{(!fetch ? "✓" : "🗙")}] {contentFile.Key}");
                                        }

                                        if (fetch)
                                        {
                                            var contentStream = await FileUtil.GetStream(new Uri(repoUrl, contentPath), Path.GetExtension(contentFile.Key));
                                            if (contentStream != null)
                                            {
                                                await CopyStreamToFile(outPath, contentStream);
                                            }
                                        }
                                        sem.Release();
                                    });

                                }
                            }

                            //Read pacakges
                            foreach (var arch in releaseInfo.Architectures)
                            {
                                foreach (var comp in releaseInfo.Components)
                                {
                                    var pkgIndexStream = await repo.GetPackageIndex(releaseInfo, comp, arch, false);
                                    if (pkgIndexStream.HasValue)
                                    {
                                        var outPath = Path.Combine(cacheRoot, $"dists/{distChan}/{releaseInfo.MakeByHashUri(pkgIndexStream.Value.Filename) ?? pkgIndexStream.Value.Filename}");
                                        await CopyStreamToFile(outPath, pkgIndexStream.Value.Stream);

                                        //read the package index
                                        var pkgIndex = await FileUtil.GetStream(new Uri(Path.GetFullPath(outPath)), Path.GetExtension(pkgIndexStream.Value.Filename));
                                        if (pkgIndex != null)
                                        {
                                            await foreach (var pkg in repo.ReadPackageIndex(pkgIndex))
                                            {
                                                await sem.WaitAsync();
                                                _ = Task.Run(async () =>
                                                  {
                                                      var pkgOutPath = Path.Combine(cacheRoot, pkg.Filename);
                                                      var fetch = true;
                                                      if (File.Exists(pkgOutPath))
                                                      {
                                                          fetch = !await CheckFileHash(pkgOutPath, pkg.SHA256);
                                                          Console.WriteLine($"[{distChan}][{arch}][{comp}] [{(!fetch ? "✓" : "🗙")}] {pkg.Name}");
                                                      }

                                                      if (fetch)
                                                      {
                                                          var pkgUrl = new Uri(repoUrl, pkg.Filename);
                                                          Console.WriteLine($"[{distChan}][{arch}][{comp}] [↓] {pkg.Name}");
                                                          var pkgStream = await FileUtil.GetStream(pkgUrl, getDecompressed: false);
                                                          if (pkgStream != null)
                                                          {
                                                              await CopyStreamToFile(pkgOutPath, pkgStream);
                                                          }
                                                      }
                                                      sem.Release();
                                                  });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Dist not found: {distChan}");
                        }
                    }
                }
            });
        }

        static async Task CopyStreamToFile(string path, Stream stream)
        {
            var dirName = Path.GetDirectoryName(path);
            if (!Directory.Exists(dirName))
            {
                Directory.CreateDirectory(dirName);
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            await stream.CopyToAsync(fs);
        }

        static async Task<bool> CheckFileHash(string path, string hash)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var data = new byte[1024 * 16]; //read 16k at a time
            while (true)
            {
                var rlen = await fs.ReadAsync(data, 0, data.Length);
                if (rlen == 0)
                {
                    sha.TransformFinalBlock(data, 0, rlen);
                    break;
                }
                else
                {
                    sha.TransformBlock(data, 0, rlen, data, 0);
                }
            }
            var hashString = BitConverter.ToString(sha.Hash).Replace("-", "").ToLower();
            return hash == hashString;
        }
    }
}
