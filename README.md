# Jellyfin.Xtream
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/Kevinjil/Jellyfin.Xtream/total)
![GitHub Downloads (all assets, latest release)](https://img.shields.io/github/downloads/Kevinjil/Jellyfin.Xtream/latest/total)
![GitHub commits since latest release](https://img.shields.io/github/commits-since/Kevinjil/Jellyfin.Xtream/latest)
![Dynamic YAML Badge](https://img.shields.io/badge/dynamic/yaml?url=https%3A%2F%2Fraw.githubusercontent.com%2FKevinjil%2FJellyfin.Xtream%2Frefs%2Fheads%2Fmaster%2Fbuild.yaml&query=targetAbi&label=Jellyfin%20ABI)
![Dynamic YAML Badge](https://img.shields.io/badge/dynamic/yaml?url=https%3A%2F%2Fraw.githubusercontent.com%2FKevinjil%2FJellyfin.Xtream%2Frefs%2Fheads%2Fmaster%2Fbuild.yaml&query=framework&label=.NET%20framework)

The Jellyfin.Xtream plugin can be used to integrate the content provided by an [Xtream-compatible API](https://xtream-ui.org/api-xtreamui-xtreamcode/) in your [Jellyfin](https://jellyfin.org/) instance.

## This fork: Jellyfin 12 support (0.9.1)

This fork of [Kevinjil/Jellyfin.Xtream](https://github.com/Kevinjil/Jellyfin.Xtream) targets **Jellyfin 12.3** (.NET 10) and adds:

* **Playback fix for Jellyfin 12.** Jellyfin 12 drops media sources whose identifier is a GUID unknown to the library (series episodes and movies had no source left: `Index was out of range`) and requires a GUID when streaming (`Unrecognized Guid format`). Movies, episodes and catch-up now leave the source identifier empty, so Jellyfin gives them the identifier of the item; items saved by older versions are updated once (their modification date is set to the date of this source format). Live TV keeps its stable GUID.
* **Resilient provider queries.** Some providers answer the same request with an empty list most of the time. Lists are retried (up to 10 attempts), the last usable answer is kept in memory and on disk (`api-cache` in the plugin data folder) and served when the provider gives nothing usable. Channels expose a cache key that changes after a degraded answer, so Jellyfin does not keep an empty folder for hours (its channel cache lasts 3 hours).
* **Custom folder names** for Video On-Demand, Series and Catch-up (Credentials page, "Display names").

Install it with the repository `https://raw.githubusercontent.com/ToniDias/Jellyfin.Xtream/jellyfin-12/manifest.json` (it replaces the original plugin, same plugin id). Tests: `dotnet test` (xUnit, `Jellyfin.Xtream.Tests`).

## Installation

The plugin can be installed using a custom plugin repository.
To add the repository, follow these steps:

1. Open your admin dashboard and navigate to `Plugins`.
1. Select the `Repositories` tab on the top of the page.
1. Click the `+` symbol to add a repository.
1. Enter `Jellyfin.Xtream` as the repository name.
1. Enter [`https://kevinjil.github.io/Jellyfin.Xtream/repository.json`](https://kevinjil.github.io/Jellyfin.Xtream/repository.json) as the repository url.
1. Click save.

To install or update the plugin, follow these steps:

1. Open your admin dashboard and navigate to `Plugins`.
1. Select the `Catalog` tab on the top of the page.
1. Under `Live TV`, select `Jellyfin Xtream`.
1. (Optional) Select the desired plugin version.
1. Click `Install`.
1. Restart your Jellyfin server to complete the installation.

## Configuration

The plugin requires connection information for an [Xtream-compatible API](https://xtream-ui.org/api-xtreamui-xtreamcode/).
The following credentials should be set correctly in the `Credentials` plugin configuration tab on the admin dashboard.

| Property | Description                                                                               |
| -------- | ----------------------------------------------------------------------------------------- |
| Base URL | The URL of the API endpoint excluding the trailing slash, including protocol (http/https) |
| Username | The username used to authenticate to the API                                              |
| Password | The password used to authenticate to the API                                              |

### Live TV

1. Open the `Live TV` configuration tab.
1. Select the categories, or individual channels within categories, you want to be available.
1. Click `Save` on the bottom of the page.
1. Open the `TV Overrides` configuration tab.
1. Modify the channel numbers, names, and icons if desired.
1. Click `Save` on the bottom of the page.

### Video On-Demand

1. Open the `Video On-Demand` configuration tab.
1. Enable `Show this channel to users`.
1. Select the categories, or individual videos within categories, you want to be available.
1. Click `Save` on the bottom of the page.

### Series

1. Open the `Series` configuration tab.
1. Enable `Show this channel to users`.
1. Select the categories, or individual series within categories, you want to be available.
1. Click `Save` on the bottom of the page.

### TV Catchup
1. Open the `Live TV` configuration tab.
1. Enable `Show the catch-up channel to users`.
1. Click `Save` on the bottom of the page.

## Known problems

### Loss of confidentiality

Jellyfin publishes the remote paths in the API and in the default user interface.
As the Xtream format for remote paths includes the username and password, anyone that can access the library will have access to your credentials.
Use this plugin with caution on shared servers.

## Troubleshooting

Make sure you have correctly configured your [Jellyfin networking](https://jellyfin.org/docs/general/networking/):

1. Open your admin dashboard and navigate to `Networking`.
2. Correctly configure your `Published server URIs`.
   For example: `all=https://jellyfin.example.com`
