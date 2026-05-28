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

using CUE4Parse.UE4.Versions;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SoulmaskDataMiner
{
	/// <summary>
	/// Locates, instantiates and runs data miners
	/// </summary>
	internal sealed class MineRunner : IDisposable
	{
		private bool mIsDisposed;

		private readonly Config mConfig;

		private readonly ELanguage mLanguage;

		private readonly Logger mLogger;

		private readonly ProviderManager mProviderManager;

		private readonly List<IDataMiner> mMiners;

		private bool mRequireHeirarchy;

		private bool mRequireLootDatabase;

		public MineRunner(Config config, ELanguage language, Logger logger)
		{
			mConfig = config;
			mLanguage = language;
			mLogger = logger;
			mProviderManager = new ProviderManager(config, language);
			mMiners = new();
			mRequireHeirarchy = false;
			mRequireLootDatabase = false;
		}

		public bool Initialize()
		{
			if (!mProviderManager.Initialize(mLogger))
			{
				return false;
			}
			CreateMiners(mConfig.Miners);
			return true;
		}

		#region Dispose
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~MineRunner()
		{
			Dispose(false);
		}

		private void Dispose(bool disposing)
		{
			if (!mIsDisposed)
			{
				if (disposing)
				{
					// Dispose managed objects

					foreach (IDataMiner miner in mMiners)
					{
						if (miner is IDisposable disposable)
						{
							disposable.Dispose();
						}
					}
					mMiners.Clear();

					mProviderManager.Dispose();
				}

				// Free unmanaged resources

				mIsDisposed = true;
			}
		}
		#endregion

		public bool Run()
		{
			if (mRequireHeirarchy)
			{
				GameClassHeirarchy.Load(mProviderManager, mLogger);
			}

			if (mRequireLootDatabase)
			{
				mProviderManager.LoadLootDatabase(mLogger);
			}

			string sqlPath = Path.Combine(mConfig.OutputDirectory, "update.sql");
			using FileStream sqlFile = IOUtil.CreateFile(sqlPath, mLogger);
			using StreamWriter sqlStream = new(sqlFile, Encoding.UTF8) { NewLine = "\n" };
			SqlWriter sqlWriter = new(sqlStream);

			sqlWriter.WriteStartFile();

			var results = new System.Collections.Concurrent.ConcurrentDictionary<IDataMiner, (bool Success, BufferedSqlWriter SqlWriter, double ElapsedMs)>();

			System.Threading.Tasks.Parallel.ForEach(mMiners, miner =>
			{
				mLogger.Important($"Running data miner [{miner.Name}]...");

				BufferedSqlWriter bufferedWriter = new();
				Stopwatch timer = new Stopwatch();
				timer.Start();

				bool success = false;
				if (Debugger.IsAttached)
				{
					// Allow exceptions to escape for easier debugging
					success = miner.Run(mProviderManager, mConfig, mLogger, bufferedWriter);
				}
				else
				{
					try
					{
						success = miner.Run(mProviderManager, mConfig, mLogger, bufferedWriter);
					}
					catch (Exception ex)
					{
						mLogger.Log(LogLevel.Error, $"Data miner [{miner.Name}] failed! [{ex.GetType().FullName}] {ex.Message}");
					}
				}
				timer.Stop();

				double elapsedMs = ((double)timer.ElapsedTicks / (double)Stopwatch.Frequency * 1000.0);
				results.TryAdd(miner, (success, bufferedWriter, elapsedMs));
			});

			bool success = true;
			foreach (IDataMiner miner in mMiners)
			{
				if (results.TryGetValue(miner, out var result))
				{
					success &= result.Success;

					sqlWriter.WriteStartSection(miner.Name);
					result.SqlWriter.Playback(sqlWriter);
					sqlWriter.WriteEndSection();

					mLogger.Information($"[{miner.Name}] completed in {result.ElapsedMs:0.##}ms");
				}
				else
				{
					mLogger.Log(LogLevel.Error, $"Data miner [{miner.Name}] was not executed or results were lost.");
					success = false;
				}
			}

			if (mRequireLootDatabase)
			{
				sqlWriter.WriteStartSection("loot");
				mProviderManager.LootDatabase.SaveData(sqlWriter, mConfig, mLogger);
				sqlWriter.WriteEndSection();
			}

			sqlWriter.WriteEndFile();

			return success;
		}

		public static void ListAllMiners(out List<string> defaultMiners, out List<string> additionalMiners)
		{
			defaultMiners = new();
			additionalMiners = new();

			Type minerInterface = typeof(IDataMiner);

			Assembly assembly = Assembly.GetExecutingAssembly();
			foreach (Type type in assembly.GetTypes())
			{
				if (!type.IsAbstract && minerInterface.IsAssignableFrom(type))
				{
					DefaultEnabledAttribute? defaultEnabledAttribute = type.GetCustomAttribute<DefaultEnabledAttribute>();
					bool isDefaultMiner = defaultEnabledAttribute?.IsEnabled ?? true;

					IDataMiner? miner;
					try
					{
						miner = (IDataMiner?)Activator.CreateInstance(type);
						if (miner == null)
						{
							continue;
						}
					}
					catch
					{
						continue;
					}

					string name = miner.Name;
					if (isDefaultMiner)
					{
						defaultMiners.Add(name);
					}
					else
					{
						additionalMiners.Add(name);
					}

					if (miner is IDisposable disposable)
					{
						disposable.Dispose();
					}
				}
			}

			defaultMiners.Sort();
			additionalMiners.Sort();
		}

		private void CreateMiners(IEnumerable<string>? minersToInclude)
		{
			mRequireHeirarchy = false;

			HashSet<string>? includeMiners = minersToInclude == null ? null : new HashSet<string>(minersToInclude.Select(m => m.ToLowerInvariant()));
			bool forceInclude = includeMiners?.Contains("all", StringComparer.OrdinalIgnoreCase) ?? false;

			Type minerInterface = typeof(IDataMiner);

			Assembly assembly = Assembly.GetExecutingAssembly();
			foreach (Type type in assembly.GetTypes())
			{
				if (!type.IsAbstract && minerInterface.IsAssignableFrom(type))
				{
					if (includeMiners == null)
					{
						DefaultEnabledAttribute? defaultEnabledAttribute = type.GetCustomAttribute<DefaultEnabledAttribute>();
						if (!(defaultEnabledAttribute?.IsEnabled ?? true))
						{
							continue;
						}
					}

					RequireHeirarchyAttribute? requireHeirarchyAttribute = type.GetCustomAttribute<RequireHeirarchyAttribute>();
					bool requireHeirarchy = requireHeirarchyAttribute?.IsRequired ?? false;

					RequireClassDataAttribute? requireClassDataAttribute = type.GetCustomAttribute<RequireClassDataAttribute>();
					if ((requireClassDataAttribute?.IsRequired ?? false) && mProviderManager.ClassMetadata is null)
					{
						IDataMiner? temp = (IDataMiner?)Activator.CreateInstance(type);
						mLogger.Warning($"Skipping miner \"{temp?.Name ?? type.Name}\" because class metadata has not been loaded. Use the --classes parameter to specify a class metadata file to load.");

						if (temp is not null)
						{
							includeMiners?.RemoveWhere(n => n.Equals(temp.Name, StringComparison.OrdinalIgnoreCase));
						}
						continue;
					}

					RequireLootDatabaseAttribute? requireLootDatabaseAttribute = type.GetCustomAttribute<RequireLootDatabaseAttribute>();
					bool requireLootDatabase = requireLootDatabaseAttribute?.IsRequired ?? false;

					IDataMiner? miner;
					try
					{
						miner = (IDataMiner?)Activator.CreateInstance(type);
						if (miner == null)
						{
							mLogger.Log(LogLevel.Error, $"Could not create an instance of {type.Name}. Ensure it has a parameterless constructor. This miner will not run.");
							continue;
						}
					}
					catch (Exception ex)
					{
						mLogger.Log(LogLevel.Error, $"Could not create an instance of {type.Name}. This miner will not run. [{ex.GetType().FullName}] {ex.Message}");
						continue;
					}
					string name = miner.Name.ToLowerInvariant();
					if (forceInclude || (includeMiners?.Contains(name) ?? true))
					{
						includeMiners?.Remove(name);
						mMiners.Add(miner);
						mRequireHeirarchy |= requireHeirarchy;
						mRequireLootDatabase |= requireLootDatabase;
					}
					else if (miner is IDisposable disposable)
					{
						disposable.Dispose();
					}
				}
			}

			includeMiners?.RemoveWhere(n => n.Equals("all", StringComparison.OrdinalIgnoreCase));

			if (includeMiners?.Count > 0)
			{
				mLogger.Warning($"The following miners specified in the filter could not be located: {string.Join(',', includeMiners)}");
			}
			if (mMiners.Count == 0)
			{
				mLogger.Log(LogLevel.Error, "No data miners which match the passed in filter could run.");
			}
			else
			{
				mLogger.Important($"The following miners will be run: {string.Join(',', mMiners.Select(m => m.Name))}");
			}
		}

		public static string GetLanguageCode(ELanguage language)
		{
			return language switch
			{
				ELanguage.English => "en",
				ELanguage.Chinese => "zh",
				ELanguage.Spanish => "es",
				ELanguage.Russian => "ru",
				ELanguage.Japanese => "ja",
				ELanguage.Korean => "ko",
				ELanguage.French => "fr",
				ELanguage.German => "de",
				ELanguage.PortugueseBrazil => "pt",
				_ => "en"
			};
		}
	}
}
