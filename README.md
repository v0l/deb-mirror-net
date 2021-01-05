
## CLI
```
DebMirrorNet:
  Mirror a Debian repo

Usage:
  DebMirrorNet [options]

Options:
  --source <source>                                  Mirror source url
  --cache-path <cache-path>                          Local path to store cache
  --check-mode <MD5|ReleaseDate|SHA1|SHA256|Size>    Verify mode for local files [default: ReleaseDate]
  --bw-limit <bw-limit>                              Download bandwidth limit (in Mbps) [default: 0]
  --dists <dists>                                    Distributions to mirror [default: ]
  --arch <arch>                                      Architectures to mirror [default: ]
  --verbose                                          Print more logs [default: False]
  --version                                          Show version information
  -?, -h, --help                                     Show help and usage information
```

## Docker
```
docker run -it --rm ghcr.io/v0l/deb-mirror-net:latest -- \
    --source http://archive.ubuntu.com/ubuntu \
    --cache-path /data
```
