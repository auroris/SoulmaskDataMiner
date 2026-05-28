// Copyright 2026 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using CUE4Parse.Compression;
using CUE4Parse.UE4.Versions;

namespace SoulmaskDataMiner
{
	internal class Program
	{
		private static int Main(string[] args)
		{
			Logger logger = new ConsoleLogger();

			if (args.Length == 0)
			{
				Config.PrintUsage(logger, LogLevel.Important);
				return OnExit(0);
			}

			Config? config;
			if (!Config.TryParseCommandLine(args, logger, out config))
			{
				logger.LogEmptyLine(LogLevel.Information);
				Config.PrintUsage(logger, LogLevel.Important);
				return OnExit(1);
			}

			string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;

			ZlibHelper.Initialize(Path.Combine(assemblyDir, ZlibHelper.DLL_NAME));
			OodleHelper.Initialize(Path.Combine(assemblyDir, OodleHelper.OODLE_NAME_CURRENT));

			bool success = true;
			string originalOutputDirectory = config.OutputDirectory;

			for (int langIndex = 0; langIndex < config.Languages.Count; langIndex++)
			{
				ELanguage language = config.Languages[langIndex];
				string langCode = Config.GetLanguageCode(language);

				if (config.Languages.Count > 1)
				{
					logger.Important($"\n--- Mining language: {language} ({langCode}) ---");
				}
				else if (language != ELanguage.English)
				{
					logger.Important($"\n--- Mining language: {language} ({langCode}) ---");
				}

				string localizedOutputDirectory = originalOutputDirectory;
				if (config.Languages.Count > 1 || language != ELanguage.English)
				{
					localizedOutputDirectory = Path.Combine(originalOutputDirectory, langCode);
				}

				try
				{
					Directory.CreateDirectory(localizedOutputDirectory);
				}
				catch (Exception ex)
				{
					logger.Log(LogLevel.Fatal, $"Could not access/create output directory \"{localizedOutputDirectory}\". [{ex.GetType().FullName}] {ex.Message}");
					success = false;
					continue;
				}

				config.OutputDirectory = localizedOutputDirectory;
				config.IsTextureExportActive = config.ExportTextures && (langIndex == 0);

				bool langSuccess;
				using (MineRunner runner = new(config, language, logger))
				{
					if (!runner.Initialize())
					{
						success = false;
						continue;
					}
					langSuccess = runner.Run();
				}

				success &= langSuccess;
			}

			config.OutputDirectory = originalOutputDirectory;

			logger.Important("Done.");

			if (!success)
			{
				logger.Warning("\nOne or more miners failed. See above for details.");
			}

			return OnExit(0);
		}

		private static int OnExit(int code)
		{
			if (System.Diagnostics.Debugger.IsAttached)
			{
				Console.Out.WriteLine("Press a key to exit");
				Console.ReadKey(true);
			}
			return code;
		}
	}
}
