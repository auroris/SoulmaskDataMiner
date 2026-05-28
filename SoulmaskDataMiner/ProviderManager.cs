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

using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SoulmaskDataMiner.GameData;
using System.Diagnostics;

namespace SoulmaskDataMiner
{
	/// <summary>
	/// Default implementation of IProviderManager
	/// </summary>
	internal class ProviderManager : IProviderManager, IDisposable
	{
		private bool mIsDisposed;

		private readonly Config mConfig;

		private readonly ELanguage mLanguage;

		private readonly DefaultFileProvider mProvider;

		private Dictionary<string, MetaClass>? mClassMetadata;

		private GameTextTable? mGameTextTable;

		private GameSingletonManager? mResourceManager;

		private Achievements? mAchievements;

		private LootDatabase? mLootDatabase;

		public IFileProvider Provider => mProvider;

		public IReadOnlyDictionary<string, MetaClass>? ClassMetadata => mClassMetadata;

		public GameTextTable GameTextTable
		{
			get
			{
				if (mGameTextTable is null)
				{
					throw new InvalidOperationException("Game text table not found. Has the provider manager been initialized?");
				}
				return mGameTextTable;
			}
		}

		public GameSingletonManager SingletonManager
		{
			get
			{
				if (mResourceManager is null)
				{
					throw new InvalidOperationException("Singleton manager not found. Has the provider manager been initialized?");
				}
				return mResourceManager;
			}
		}

		public Achievements Achievements
		{
			get
			{
				if (mAchievements is null)
				{
					throw new InvalidOperationException("Achievements not found. Has the provider manager been initialized?");
				}
				return mAchievements;
			}
		}

		public LootDatabase LootDatabase
		{
			get
			{
				if (mLootDatabase is null)
				{
					throw new InvalidOperationException("LootDatabase not found. Has the provider manager been initialized, and has a running miner declared a RequireLootDatabase attribute?");
				}
				return mLootDatabase;
			}
		}

		public ProviderManager(Config config, ELanguage language)
		{
			mConfig = config;
			mLanguage = language;
			mProvider = new DefaultFileProvider(Path.Combine(config.GameContentDirectory, "Paks"), SearchOption.TopDirectoryOnly, null, null);
		}

		public bool Initialize(Logger logger)
		{
			InitializeProvider(mProvider);
			if (!LoadClassMetaData(logger))
			{
				return false;
			}
			mGameTextTable = GameTextTable.Load(mProvider, logger);
			mResourceManager = GameSingletonManager.Load(mProvider, logger);
			if (mResourceManager is not null)
			{
				mAchievements = Achievements.Load(mResourceManager, logger);
			}

			return true;
		}

		public bool LoadLootDatabase(Logger logger)
		{
			mLootDatabase = new();
			if (!mLootDatabase.Load(mProvider, logger))
			{
				mLootDatabase = null;
				return false;
			}
			return true;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~ProviderManager()
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
					mResourceManager = null;
					mProvider.Dispose();
				}

				// Free unmanaged resources

				mIsDisposed = true;
			}
		}

		private void InitializeProvider(DefaultFileProvider provider)
		{
			provider.Initialize();

			FAesKey key;
			if (mConfig.EncryptionKey is null)
			{
				key = new(new byte[32]);
			}
			else
			{
				key = new(mConfig.EncryptionKey);
			}

			foreach (var vfsReader in provider.UnloadedVfs)
			{
				provider.SubmitKey(vfsReader.EncryptionKeyGuid, key);
			}

			mProvider.PostMount();

			mProvider.ChangeCulture(MineRunner.GetLanguageCode(mLanguage));
		}

		private bool LoadClassMetaData(Logger logger)
		{
			if (mConfig.ClassesPath is null) return true;

			try
			{
				logger.Information("Loading class metadata...");

				Stopwatch timer = new();
				timer.Start();

				JObject root;

				using (FileStream file = File.OpenRead(mConfig.ClassesPath))
				using (StreamReader sr = new(file))
				using (JsonReader reader = new JsonTextReader(sr))
				{
					root = JObject.Load(reader);
				}

				JArray classArray = (JArray)root["data"]!;

				Dictionary<string, MetaClass> classes = new();

				HashSet<string> propertiesToIgnore = new()
				{
					"__MDKClassSize"
				};
				foreach (JObject classObj in classArray)
				{
					foreach (JProperty @class in classObj.Properties())
					{
						string className = @class.Name.Substring(1); // Trim leading U, A, F, etc.
						if (classes.ContainsKey(className))
						{
							logger.Debug($"Found an additional instance of class \"{className}\" in metadata. Skipping.");
							continue;
						}

						string? classSuper = null;
						List<MetaClassProperty> classProperties = new();

						JArray classPropertyArray = (JArray)@class.Value;
						foreach (JObject classPropertyObj in classPropertyArray)
						{
							foreach (JProperty classProperty in classPropertyObj.Properties())
							{
								string propertyName = classProperty.Name;
								if (propertiesToIgnore.Contains(propertyName)) continue;

								if (propertyName.Equals("__InheritInfo"))
								{
									JArray classSuperArray = (JArray)classProperty.Value;
									if (classSuperArray.Count > 0)
									{
										classSuper = ((string)classSuperArray[0]!).Substring(1); // Trim leading U, A, F, etc.
									}
									continue;
								}

								JArray propertyValue = (JArray)classProperty.Value;
								JArray propertyValue0 = (JArray)propertyValue[0];

								string propertyType = (string)propertyValue0[0]!;

								classProperties.Add(new() { Name = propertyName, Type = propertyType });
							}
						}

						classes.Add(className, new() { Name = className, Super = classSuper!, Properties = classProperties });
					}
				}

				mClassMetadata = classes;

				timer.Stop();

				logger.Information($"Class metadata loaded in {((double)timer.ElapsedTicks / (double)Stopwatch.Frequency * 1000.0):0.##}ms");

				return true;
			}
			catch (Exception ex)
			{
				logger.Error($"Failed to load class metadata. [{ex.GetType().FullName}] {ex.Message}");
				return false;
			}
		}
	}

	internal struct MetaClass
	{
		public string Name;
		public string? Super;
		public IReadOnlyList<MetaClassProperty> Properties;

		public override string ToString()
		{
			return $"{Name} ({Properties.Count} properties)";
		}
	}

	internal struct MetaClassProperty
	{
		public string Name;
		public string Type;

		public override string ToString()
		{
			return $"{Name} [{Type}]";
		}
	}
}
