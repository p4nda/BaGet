# BaGet :baguette_bread:

![Build status]

A lightweight [NuGet] and [symbol] server.

<p align="center">
  <img width="100%" src="https://user-images.githubusercontent.com/737941/50140219-d8409700-0258-11e9-94c9-dad24d2b48bb.png">
</p>

## Getting Started

1. Install the [.NET SDK]
2. Download and extract [BaGet's latest release]
3. Start the service with `dotnet BaGet.dll`
4. Browse `https://localhost:8081/` in your browser

For more information, please refer to the [documentation].

## Features

* **Cross-platform**: runs on Windows, macOS, and Linux!
* **Cloud native**: supports [Docker], [Azure], [AWS], [Google Cloud], [Alibaba Cloud]
* **Offline support**: [mirror a NuGet server] to speed up builds and enable offline downloads

Stay tuned, more features are planned!

[Build status]: https://img.shields.io/github/actions/workflow/status/loic-sharma/BaGet/.github/workflows/main.yml

[NuGet]: https://learn.microsoft.com/nuget/what-is-nuget
[symbol]: https://docs.microsoft.com/en-us/windows/desktop/debug/symbol-servers-and-symbol-stores
[.NET SDK]: https://www.microsoft.com/net/download
[Node.js]: https://nodejs.org/

[BaGet's latest release]: https://github.com/loic-sharma/BaGet/releases

[Documentation]: https://loic-sharma.github.io/BaGet/
[Docker]: https://loic-sharma.github.io/BaGet/installation/docker/
[Azure]: https://loic-sharma.github.io/BaGet/installation/azure/
[AWS]: https://loic-sharma.github.io/BaGet/installation/aws/
[Google Cloud]: https://loic-sharma.github.io/BaGet/installation/gcp/
[Alibaba Cloud]: https://loic-sharma.github.io/BaGet/installation/aliyun/

[Mirror a NuGet server]: https://loic-sharma.github.io/BaGet/configuration/#enable-read-through-caching

## PostgreSQL
```bash
postgres=# CREATE USER baget_user WITH PASSWORD '...';
postgres=# CREATE DATABASE baget_db OWNER baget_user ENCODING 'UTF8';
postgres=# GRANT ALL PRIVILEGES ON DATABASE baget_db TO baget_user;
postgres=# \c baget_db
postgres=# CREATE EXTENSION citext
# Add user to pg_hba.conf
```

## Container
```bash
podman run -d --rm --build-arg BUILD_TIMESTAMP=$(date +'%Y%m%d-%H%M') -p 8081:8081 \
  -v /home/service-baget/cert:/app/cert:Z,ro \
  -v /home/service-baget/conf:/home/baget/conf:Z,ro \
  -v /home/service-baget/logs:/home/baget/logs:Z,rw \
  -v /mnt/baget/packages:/home/baget/packages:Z,rw \
  localhost/ndf-baget:latest
```

## TLS Setup

1. Create baget.pfx from crt, key and Root CA
2. Check Program.cs

```bash
openssl pkcs12 -export -out baget.pfx -inkey baget.key -in baget.crt -certfile baget.Root_CA.crt
```
