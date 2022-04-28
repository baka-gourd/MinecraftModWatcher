# MinecraftModWatcher

Make your makepack in github easily.

## How to use

1. put the binary in `.minecraft` directory.
2. run binary once.
3. add your [api key](https://console.curseforge.com/#/api-keys) in ./ModWatcher/config.json

> use -h to get more features.

## How to build

1. set up dotnet7
2. `dotnet publish .\MinecraftModWatcher\MinecraftModWatcher.csproj -c Release -o ./ -r win-x64`
