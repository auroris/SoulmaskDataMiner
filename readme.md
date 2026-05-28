Command line program to extract data from the game Soulmask. This is mainly just for my own use to update data when the game updates.

## Usage

Run the program with no parameters to print the usage.
```
Usage: SoulmaskDataMiner [[options]] [game assets directory] [output directory]

  [game assets directory]  Path to a directory containing .pak files for a game.

  [output directory]       Directory to output exported assets.

Options

  --key [key]        The AES encryption key for the game's data.

  --classes [path]   Path to a ClassesInfo.json file output from running Dumper-7.
                     If not specified, miners that require it will be skipped.

  --miners [miners]  Comma separated list of miners to run. If not specified,
                     default miners will run.

  --lang [languages] Comma separated list of languages to mine (e.g. en,zh).
                     Specify 'all' to mine all 9 supported languages.
                     Supported: en, zh, es, ru, ja, ko, fr, de, pt.

  --no-textures      Skip exporting textures/icons to speed up mining.
```

This will also print a list of the names of all available miners.

Soulmask encrypts its pak file, so you will need to obtain the proper AES key and supply it to the program via the `--key` parameter. How you obtain the key is your business. Do not contact me asking for the key. I will not give it to you.

Some miners require class metadata from the game. The program [Dumper-7](https://github.com/Encryqed/Dumper-7) can be used to generate this data. It will output a file called `Dumpspace/ClassesInfo.json` which should be passed to SoulmaskDataMiner via the `--classes` parameter. The readme for Dumper-7 explains how to use it. It requires a separate DLL injector which is not provided. You can use any DLL injector you like, such as [DllInjector](https://github.com/CrystalFerrai/DllInjector).

## Releases

There are no releases of this tool for the time being. If you wish to try it, you will need to build it.

## Building

Clone the repository, including submodules.
```
git clone --recursive https://github.com/CrystalFerrai/SoulmaskDataMiner.git
```

You can then open and build SoulmaskDataMiner.sln.
