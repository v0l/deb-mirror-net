using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DebNet
{
    public interface IRepository
    {
        internal static string[] ReleasePathFormats = new string[] { "dists/{0}/InRelease", "dists/{0}/Release" };

        /// <summary>
        /// List of compressions formats in preference order
        /// </summary>
        internal static string[] CompressionFormats = new string[] { ".xz", ".gz", ".bz2", string.Empty };

        public Task<ReleaseFileStream?> GetReleaseInfo(string dist, bool getDecompressed = true);

        public Task<ReleaseFileStream?> GetPackageIndex(ReleaseInfo ri, string componenet, string arch, bool getDecompressed = true);
        public Task<ReleaseFileStream?> GetContentsIndex(ReleaseInfo ri, string arch, bool getDecompressed = true);
        public Task<ReleaseFileStream?> GetTranslationIndex(ReleaseInfo ri, string componenet, bool getDecompressed = true);

        public IAsyncEnumerable<PackageInfo> ReadPackageIndex(Stream s);
        public IAsyncEnumerable<KeyValuePair<string, string>> ReadContentsIndex(Stream s);
        public Task<Dictionary<string, ReleaseFileInfo>> ReadTranslationIndex(Stream s);
    }

    public class Repository : IRepository
    {
        private Uri RootUrl { get; set; }
        private ILogger Logger { get; }
        private FileUtil FileUtil { get; }

        private Uri CombinePath(string path)
        {
            return new Uri(RootUrl, path);
        }

        public Repository(ILogger log, Uri rootPath) 
        {
            Logger = log;
            RootUrl = rootPath;

            FileUtil = new FileUtil(log);
        }

        public async Task<ReleaseFileStream?> GetReleaseInfo(string dist, bool getDecompressed = true)
        {
            foreach (var fmt in IRepository.ReleasePathFormats) 
            {
                try
                {
                    var releaseFile = string.Format(fmt, dist);
                    var fs = await FileUtil.GetStream(CombinePath(releaseFile), Path.GetExtension(fmt), getDecompressed);
                    if (fs != null)
                    {
                        return new ReleaseFileStream()
                        {
                            Stream = fs,
                            Filename = releaseFile
                        };
                    }
                }
                catch(Exception ex)
                {
                    Logger.LogError(ex, $"Failed to get release {dist}");
                }
            }
            return null;
        }

        /// <summary>
        /// Opens a stream to a content index file
        /// </summary>
        /// <param name="ri"></param>
        /// <param name="arch"></param>
        /// <returns></returns>
        public async Task<ReleaseFileStream?> GetContentsIndex(ReleaseInfo ri, string arch, bool getDecompressed = true)
        {
            if (ri?.Architectures?.Contains(arch) ?? false)
            {
                return await GetIndexFromFileList(ri, $"Contents-{arch}", getDecompressed);
            }
            return null;
        }

        /// <summary>
        /// Reads the raw content index stream
        /// <para>Returns [file, location] key value pairs</para>
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public async IAsyncEnumerable<KeyValuePair<string, string>> ReadContentsIndex(Stream stream)
        {
            if (stream != null)
            {
                using var sr = new StreamReader(stream);
                string line = null;
                var whiteSpace = new char[] { ' ', '\t' };
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    var firstSpace = line.IndexOfAny(whiteSpace);
                    var lastSpace = line.LastIndexOfNone(whiteSpace, firstSpace);
                    if (firstSpace != -1 && lastSpace != -1)
                    {
                        var file = line.Substring(0, firstSpace);
                        var location = line.Substring(lastSpace);
                        if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(location))
                        {
                            yield return new KeyValuePair<string, string>(file, location);
                        }
                    }
                }
            }
        }

        public async Task<ReleaseFileStream?> GetTranslationIndex(ReleaseInfo ri, string componenet, bool getDecompressed = true)
        {
            if (ri?.Components?.Contains(componenet) ?? false)
            {
                return await GetIndexFromFileList(ri, $"{componenet}/i18n/Index", getDecompressed);
            }
            return null;
        }

        public async Task<Dictionary<string, ReleaseFileInfo>> ReadTranslationIndex(Stream s)
        {
            var ret = new Dictionary<string, ReleaseFileInfo>();

            using var sr = new StreamReader(s);
            string line = null;
            while ((line = await sr.ReadLineAsync()) != null) 
            {
                reset_line:
                var key = line?.Split(':', 2)[0]?.Trim()?.ToLower();
                if (!string.IsNullOrEmpty(key)) 
                {
                    line = await ReleaseFileInfo.ReadIntoDictionary(key, ret, sr);
                    if(line != null)
                    {
                        goto reset_line;
                    }
                }
            }

            return ret;
        }

        public async Task<ReleaseFileStream?> GetPackageIndex(ReleaseInfo ri, string componenet, string arch, bool getDecompressed = true)
        {
            if ((ri?.Components?.Contains(componenet) ?? false) && (ri?.Architectures?.Contains(arch) ?? false))
            {
                return await GetIndexFromFileList(ri, $"{componenet}/binary-{arch}/Packages", getDecompressed);
            }
            return null;
        }

        public async IAsyncEnumerable<PackageInfo> ReadPackageIndex(Stream s)
        {
            if (s != null)
            {
                using var sr = new StreamReader(s, encoding: Encoding.UTF8, leaveOpen: true);
                while (true)
                {
                    var pkg = await PackageInfo.ReadFromStream(sr);
                    if (!string.IsNullOrEmpty(pkg.Name))
                    {
                        yield return pkg;
                    } 
                    else
                    {
                        yield break;
                    }
                }
            }
        }

        private async Task<ReleaseFileStream?> GetIndexFromFileList(ReleaseInfo ri, string path, bool getDecompressed = true)
        {
            var orderedContents = GetCompressedOrderdFiles(ri, path);
            foreach (var testPath in orderedContents)
            {
                var url = await ri.GetUri(FileUtil, testPath.Key);
                Stream stream = null;
                try
                {
                    stream = await FileUtil.GetStream(url, Path.GetExtension(testPath.Key), getDecompressed);
                    if (stream == null) return null;
                }
                catch
                {
                    continue;
                }
                
                return new ReleaseFileStream()
                {
                    Stream = stream,
                    Info = testPath.Value,
                    Filename = testPath.Key
                };
            }
            return null;
        }

        public IOrderedEnumerable<KeyValuePair<string, ReleaseFileInfo>> GetCompressedOrderdFiles(ReleaseInfo ri, string path)
        {
            return ri.FileList.Where(a => a.Key.StartsWith(path)).OrderBy(a => Array.FindIndex(IRepository.CompressionFormats, b => b == (Path.GetExtension(a.Key) ?? string.Empty)));
        }

        public async ValueTask<Uri> TryGetCompressedPath(string relative)
        {
            foreach (var ext in IRepository.CompressionFormats)
            {
                var testUrl = CombinePath(Path.ChangeExtension(relative, ext));
                if (await FileUtil.Exists(testUrl))
                {
                    return testUrl;
                }
            }

            return null;
        }
    }

    public struct ReleaseFileStream
    {
        public Stream Stream { get; set; }
        public ReleaseFileInfo Info { get; set; }
        public string Filename { get; set; }
    }
}
