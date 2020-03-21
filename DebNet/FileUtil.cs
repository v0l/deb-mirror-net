using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Compressors.Xz;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DebNet
{
    public static class FileUtil
    {
        private static HttpClient Client { get; } = new HttpClient();

        /// <summary>
        /// Gets a stream from Remote or Local source
        /// </summary>
        /// <param name="path"></param>
        /// <param name="mimeHint">Gives a hint on the content type in order to wrap the stream, this is only used when the path doesnt contain an extension</param>
        /// <returns></returns>
        public static async ValueTask<Stream> GetStream(Uri path, string mimeHint = null, bool getDecompressed = true)
        {
            if (!path.IsFile)
            {
                var ext = Path.GetExtension(path.AbsolutePath);
                var rsp = await Client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
                if (rsp?.IsSuccessStatusCode ?? false)
                {
                    Stream sourceStream = await rsp.Content.ReadAsStreamAsync();
                    if (getDecompressed)
                    {
                        if (!string.IsNullOrEmpty(ext))
                        {
                            sourceStream = WrapDecompression(ext, sourceStream);
                        }
                        else if (string.IsNullOrEmpty(ext) && (rsp.Headers.TryGetValues("Content-Type", out IEnumerable<string> vals) || !string.IsNullOrEmpty(mimeHint)))
                        {
                            var fVal = mimeHint ?? vals.FirstOrDefault();
                            if (!string.IsNullOrEmpty(fVal))
                            {
                                sourceStream = WrapDecompression(fVal, sourceStream);
                            }
                        }
                    }
                    return sourceStream;
                }
				else 
				{
					Console.WriteLine($" [{rsp?.StatusCode}] {path}");
				}
            }
            else
            {
                var ext = Path.GetExtension(path.LocalPath);
                Stream sourceStream = new FileStream(path.LocalPath, FileMode.Open, FileAccess.Read);
                if (getDecompressed && !string.IsNullOrEmpty(ext))
                {
                    sourceStream = WrapDecompression(ext, sourceStream);
                }
                else if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(mimeHint))
                {
                    sourceStream = WrapDecompression(mimeHint, sourceStream);
                }
                return sourceStream;
            }

            return null;
        }

        public static async ValueTask<bool> Exists(Uri path)
        {
            try
            {
                Console.WriteLine($"Testing path: {path}");
                if (path.IsFile)
                {
                    return File.Exists(path.LocalPath);
                }
                else
                {
                    var head = await Client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
                    return head.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return false;
        }

        private static Stream WrapDecompression(string type, Stream s)
        {
            switch (type)
            {
                case "application/gzip":
                case ".gz":
                    {
                        return new GZipStream(s, CompressionMode.Decompress);
                    }
                case "application/x-xz":
                case ".xz":
                    {
                        return new XZStream(s);
                    }
                case "application/x-bzip2":
                case ".bz2":
                    {
                        return new BZip2Stream(s, SharpCompress.Compressors.CompressionMode.Decompress, false);
                    }
                case "application/x-lzma":
                case ".lzma":
                    {
                        return new LzmaStream(new byte[] { }, s);
                    }
            }
            return s;
        }
    }
}
