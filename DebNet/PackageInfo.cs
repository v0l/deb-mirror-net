using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DebNet
{
    /*
     *  Package: accountsservice
        Architecture: arm64
        Version: 0.6.45-1ubuntu1
        Priority: standard
        Section: gnome
        Origin: Ubuntu
        Maintainer: Ubuntu Developers <ubuntu-devel-discuss@lists.ubuntu.com>
        Original-Maintainer: Debian freedesktop.org maintainers <pkg-freedesktop-maintainers@lists.alioth.debian.org>
        Bugs: https://bugs.launchpad.net/ubuntu/+filebug
        Installed-Size: 416
        Depends: dbus, libaccountsservice0 (= 0.6.45-1ubuntu1), libc6 (>= 2.17), libglib2.0-0 (>= 2.37.3), libpolkit-gobject-1-0 (>= 0.99)
        Suggests: gnome-control-center
        Filename: pool/main/a/accountsservice/accountsservice_0.6.45-1ubuntu1_arm64.deb
        Size: 53620
        MD5sum: 12afc5ef78c3b695e3a7dc1c45cde704
        SHA1: 6f7021f1459da58db8d8a5e57eda24ef1a8e7a95
        SHA256: f0f8460a2c7ab259158e44e0c89232ffa3164656032acb7abeca60f2d16c6fad
        Homepage: https://www.freedesktop.org/wiki/Software/AccountsService/
        Description: query and manipulate user account information
        Task: standard
        Description-md5: 8aeed0a03c7cd494f0c4b8d977483d7e
        Supported: 5y
        */
    public class PackageInfo
    {
        public string Name { get; set; }
        public string Architecture { get; set; }
        public string Version { get; set; }
        public string Priority { get; set; }
        public string Section { get; set; }
        public string Origin { get; set; }
        public string Maintainer { get; set; }
        public string OriginalMaintainer { get; set; }
        public string Bugs { get; set; }
        public long InstalledSize { get; set; }
        public string Depends { get; set; }
        public string Suggests { get; set; }
        public string Filename { get; set; }
        public long Size { get; set; }
        public string MD5 { get; set; }
        public string SHA1 { get; set; }
        public string SHA256 { get; set; }
        public string Homepage { get; set; }
        public string Description { get; set; }
        public string Task { get; set; }
        public string DescriptionMd5 { get; set; }
        public string Supported { get; set; }

        public Dictionary<string, string> Other { get; set; } = new Dictionary<string, string>();

        public static async Task<PackageInfo> ReadFromStream(StreamReader r)
        {
            var ret = new PackageInfo();

            string lastKey = null;
            string line;
            while((line = await r.ReadLineAsync()) != null)
            {
                if (line == string.Empty) 
                    break;

                if(line.StartsWith(' ') || line.StartsWith('\t'))
                {
                    line = $"{lastKey}: {line}"; //fake the line starting with the key
                }
                var ls = line.Split(':', 2);
                var key = ls[0].Trim().ToLower();
                var val = ls[1].Substring(1);
                switch (key)
                {
                    case "package":
                        {
                            ret.Name = AppendLineOrSet(ret.Name, val);
                            break;
                        }
                    case "architecture":
                        {
                            ret.Architecture = AppendLineOrSet(ret.Architecture, val);
                            break;
                        }
                    case "version":
                        {
                            ret.Version = AppendLineOrSet(ret.Version, val);
                            break;
                        }
                    case "priority":
                        {
                            ret.Priority = AppendLineOrSet(ret.Priority, val);
                            break;
                        }
                    case "section":
                        {
                            ret.Section = AppendLineOrSet(ret.Section, val);
                            break;
                        }
                    case "origin":
                        {
                            ret.Origin = AppendLineOrSet(ret.Origin, val);
                            break;
                        }
                    case "maintainer":
                        {
                            ret.Maintainer = AppendLineOrSet(ret.Maintainer, val);
                            break;
                        }
                    case "original-maintainer":
                        {
                            ret.OriginalMaintainer = AppendLineOrSet(ret.OriginalMaintainer, val);
                            break;
                        }
                    case "bugs":
                        {
                            ret.Bugs = AppendLineOrSet(ret.Bugs, val);
                            break;
                        }
                    case "installed-size":
                        {
                            ret.InstalledSize = long.TryParse(val, out long sz) ? sz : default;
                            break;
                        }
                    case "depends":
                        {
                            ret.Depends = AppendLineOrSet(ret.Depends, val);
                            break;
                        }
                    case "suggests":
                        {
                            ret.Suggests = AppendLineOrSet(ret.Suggests, val);
                            break;
                        }
                    case "filename":
                        {
                            ret.Filename = AppendLineOrSet(ret.Filename, val);
                            break;
                        }
                    case "size":
                        {
                            ret.Size = long.TryParse(val, out long sz) ? sz : default;
                            break;
                        }
                    case "md5sum":
                        {
                            ret.MD5 = AppendLineOrSet(ret.MD5, val);
                            break;
                        }
                    case "sha1":
                        {
                            ret.SHA1 = AppendLineOrSet(ret.SHA1, val);
                            break;
                        }
                    case "sha256":
                        {
                            ret.SHA256 = AppendLineOrSet(ret.SHA256, val);
                            break;
                        }
                    case "homepage":
                        {
                            ret.Homepage = AppendLineOrSet(ret.Homepage, val);
                            break;
                        }
                    case "description":
                        {
                            ret.Description = AppendLineOrSet(ret.Description, val);
                            break;
                        }
                    case "task":
                        {
                            ret.Task = AppendLineOrSet(ret.Task, val);
                            break;
                        }
                    case "description-md5":
                        {
                            ret.DescriptionMd5 = AppendLineOrSet(ret.DescriptionMd5, val);
                            break;
                        }
                    case "supported":
                        {
                            ret.Supported = AppendLineOrSet(ret.Supported, val);
                            break;
                        }
                    default:
                        {
                            if(!ret.Other.ContainsKey(key))
                            {
                                ret.Other.Add(key, val);
                            }else
                            {
                                ret.Other[key] += $"\n{val}";
                            }
                            break;
                        }
                }
                lastKey = key;
            }

            return ret;
        }

        private static string AppendLineOrSet(string val, string line)
        {
            if(string.IsNullOrEmpty(val))
            {
                return line;
            } else
            {
                return $"\n{line}";
            }
        }
    }
}
