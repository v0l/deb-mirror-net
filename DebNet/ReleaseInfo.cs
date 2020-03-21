using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DebNet
{
    /// <summary>
    /// https://wiki.debian.org/DebianRepository/Format
    /// </summary>
    public class ReleaseInfo
    {
        /// <summary>
        /// Optional field indicating the origin of the repository, a single line of free form text.
        /// </summary>
        public string Origin { get; set; }

        /// <summary>
        /// Optional field including some kind of label, a single line of free form text.
        /// <para>Typically used extensively in repositories split over multiple media such as repositories stored on CDs.</para>
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The Suite field may describe the suite. A suite is a single word. In Debian, this shall be one of oldstable, stable, testing, unstable, or experimental; with optional suffixes such as -updates.
        /// </summary>
        public string Suite { get; set; }

        /// <summary>
        /// The Version field, if specified, shall be the version of the release. This is usually a sequence of integers separated by the character . (full stop).
        /// </summary>
        public string Version { get; set; }
        public string Codename { get; set; }

        /// <summary>
        /// The Date field shall specify the time at which the Release file was created. Clients updating a local on-disk cache should ignore a Release file with an earlier date than the date in the already stored Release file.
        /// </summary>
        /// 
        /// The Valid-Until field may specify at which time the Release file should be considered expired by the client. Client behaviour on expired Release files is unspecified.
        /// The format of the dates is the same as for the Date field in .changes files; and as used in debian/changelog files, and documented in Policy 4.4 (Debian changelog: debian/changelog), 
        /// but all dates must be represented as an instance of UTC(Coordinated Universal Time) as it is the case e.g. in HTTP/1.1, too.Thus, generating a valid value for the Date field can be achieved by running date -R -u.
        /// 
        /// Further clarifications:
        /// The time zone must be specified using one of the strings +0000, UTC, GMT, or Z.Other values, such as different time zones, must not be used. Using the numerical value +0000 is recommended.
        /// The numerical values for day, hour, minute, and second must be zero padded. Clients should also accept non-zero-terminated values for historical compatibility reasons.
        /// Clients may accept other formats in addition to the specified one, but files should not contain them.
        public DateTime Date { get; set; }

        /// <summary>
        /// Whitespace separated unique single words identifying Debian machine architectures as described in Architecture specification
        /// </summary>
        /// Whitespace separated unique single words identifying Debian machine architectures as described in Architecture specification strings, 
        /// Section 11.1. The field identifies which architectures are supported by this repository. A client should give a warning if it is has a 
        /// repository configured which doesn't support the machine architecture(s) configured in the client. Servers are allowed to declare support 
        /// for an architecture even if they currently don't distribute indexes for this architecture. 
        /// Clients should treat a missing entry for an architecture-specific index of a supported architecture as if that index file would 
        /// exist, but is empty. If a server is not specifying the supported architectures with this field client behavior is unspecified in this case.
        /// 
        /// The presents of the architecture all in this field indicates that the architecture-specific indexes do not include information 
        /// about Architecture:all packages and have instead their own index file with the architecture all.Clients must download the all index
        /// files in this case, but must not download them if the Architectures field does not include all.
        public List<string> Architectures { get; set; }

        /// <summary>
        /// A whitespace separated list of areas.
        /// </summary>
        /// 
        /// May also include be prefixed by parts of the path following the directory beneath dists, if the Release file is not in a directory directly beneath dists/.
        public List<string> Components { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// An optional boolean field with the default value "no". A value of "yes" indicates that the server supports the optional "by-hash" locations as an alternative to the canonical location (and name) of an index file. 
        /// A client is free to choose which locations it will try to get indexes from, but it is recommend to use the "by-hash" location if supported by the server for its benefits for servers and clients. A client may fallback to the canonical location if by-hash fails.
        /// </summary>
        public bool AcquireByHash { get; set; }

        [JsonIgnore]
        public Dictionary<string, string> Other { get; set; } = new Dictionary<string, string>();

        public Dictionary<string, ReleaseFileInfo> FileList { get; set; } = new Dictionary<string, ReleaseFileInfo>();

        internal Uri BasePath { get; set; }

        /// <summary>
        /// Gets the "by-hash" url if enabled, path MUST exist in <see cref="FileList"/>
        /// </summary>
        /// <param name="relative">Relate path from this release info</param>
        /// <returns></returns>
        public async ValueTask<Uri> GetUri(string relative)
        {
            /// Only ready by-hash when we are relative to remote source, Ie. not a file path
            if(AcquireByHash && !BasePath.IsFile && FileList.ContainsKey(relative))
            {
                string[] checkHashFormats = new string[] { "SHA256", "SHA1", "MD5Sum" };
                foreach (var hf in checkHashFormats) 
                {
                    var hashUrl = new Uri(BasePath, MakeByHashUri(relative, hf));
                    if(await FileUtil.Exists(hashUrl))
                    {
                        return hashUrl;
                    }
                }
            }

            //Fallback to canonical path
            return new Uri(BasePath, relative);
        }

        public string MakeByHashUri(string relative, string hashFunction = "SHA256")
        {
            /// Only ready by-hash when we are relative to remote source, Ie. not a file path
            if (AcquireByHash && FileList.ContainsKey(relative))
            {
                var fileInfo = FileList[relative];
                string[] checkHashFormats = new string[] { "SHA256", "SHA1", "MD5Sum" };
                Func<string, ReleaseFileInfo, string> fnGetHash = (fmt, rfi) => {
                    switch (fmt)
                    {
                        case "SHA256": return rfi.SHA256;
                        case "SHA1": return rfi.SHA1;
                        case "MD5Sum": return rfi.MD5;
                    }
                    return null;
                };

                var urlByHash = $"by-hash/{hashFunction}/{fnGetHash(hashFunction, fileInfo)}";
                return Path.Combine(Path.GetDirectoryName(relative), urlByHash);
            }

            return null;
        }

        public static async Task<ReleaseInfo> ReadFromStream(Uri basePath, StreamReader r)
        {
            var ret = new ReleaseInfo()
            {
                BasePath = basePath
            };

            string line = null;
            while (null != (line = await r.ReadLineAsync()))
            {
            reset_line:
                if (line.StartsWith("-----") || string.IsNullOrWhiteSpace(line))
                {
                    if (line.Equals("<html>", StringComparison.OrdinalIgnoreCase)) return null;
                    if (line == "-----BEGIN PGP SIGNATURE-----")
                    {
                        var sig = $"{line}\n{await r.ReadToEndAsync()}";
                        ret.Other.Add("pgp-sig", sig);
                    }
                    continue;
                }

                var ls = line.Split(':', 2);
                if (ls.Length != 2) continue;

                var key = ls[0].Trim().ToLower();
                var val = ls[1].Trim();
                switch (key)
                {
                    case "origin":
                        {
                            ret.Origin = val;
                            break;
                        }
                    case "label":
                        {
                            ret.Label = val;
                            break;
                        }
                    case "suite":
                        {
                            ret.Suite = val;
                            break;
                        }
                    case "version":
                        {
                            ret.Version = val;
                            break;
                        }
                    case "codename":
                        {
                            ret.Codename = val;
                            break;
                        }
                    case "date":
                        {
                            if (DateTime.TryParseExact(val, "ddd, dd MMM yyyy HH:mm:ss %UTC", CultureInfo.CreateSpecificCulture("en-US"), DateTimeStyles.AssumeUniversal, out DateTime dt))
                            {
                                ret.Date = dt.ToUniversalTime();
                            }
                            else
                            {
                                ret.Date = DateTime.MinValue;
                            }
                            break;
                        }
                    case "architectures":
                        {
                            ret.Architectures = new List<string>(val.Split(' '));
                            break;
                        }
                    case "components":
                        {
                            ret.Components = new List<string>(val.Split(' '));
                            break;
                        }
                    case "description":
                        {
                            ret.Description = val;
                            break;
                        }
                    case "md5sum":
                    case "sha1":
                    case "sha256":
                        {
                            var lastLine = await ReleaseFileInfo.ReadIntoDictionary(key, ret.FileList, r);
                            if(lastLine != null)
                            {
                                line = lastLine;
                                goto reset_line;
                            }
                            break;
                        }
                    case "acquire-by-hash":
                        {
                            ret.AcquireByHash = val.Equals("yes", StringComparison.OrdinalIgnoreCase);
                            break;
                        }
                    default:
                        {
                            if (!ret.Other.ContainsKey(key))
                            {
                                ret.Other.Add(key, val);
                            }
                            break;
                        }
                }
            }

            return ret;
        }
    }

    public class ReleaseFileInfo
    {
        public string MD5 { get; set; }
        public string SHA1 { get; set; }
        public string SHA256 { get; set; }
        public long Size { get; set; }

        public static async Task<string> ReadIntoDictionary(string sectionKey, Dictionary<string, ReleaseFileInfo> readInto, StreamReader r)
        {
            string fline;
            while (!string.IsNullOrEmpty(fline = await r.ReadLineAsync()))
            {
                if (!fline.StartsWith(' '))
                {
                    return fline;
                }

                var fs = fline.Split(' ').Where(a => !string.IsNullOrEmpty(a)).ToArray(); // (lazy)
                if (fs.Length == 3)
                {
                    var fkey = fs[2];
                    if (!readInto.ContainsKey(fkey))
                    {
                        readInto.Add(fkey, new ReleaseFileInfo()
                        {
                            Size = long.TryParse(fs[1], out long sz) ? sz : default,
                            MD5 = sectionKey == "md5sum" ? fs[0] : null,
                            SHA1 = sectionKey == "sha1" ? fs[0] : null,
                            SHA256 = sectionKey == "sha256" ? fs[0] : null,
                        });
                    }
                    else
                    {
                        if (sectionKey == "md5sum")
                        {
                            readInto[fkey].MD5 = fs[0];
                        }
                        else if (sectionKey == "sha1")
                        {
                            readInto[fkey].SHA1 = fs[0];
                        }
                        else if (sectionKey == "sha256")
                        {
                            readInto[fkey].SHA256 = fs[0];
                        }
                    }
                }
            }

            return null;
        }
    }
}
