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

using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using SoulmaskDataMiner.GameData;

namespace SoulmaskDataMiner
{
	/// <summary>
	/// Per-language view of mining resources. Delegates process-wide state
	/// (provider, class metadata, loot database) to a <see cref="SharedMiningContext"/>
	/// and holds only the locale-dependent state itself (text table, singletons,
	/// achievements). One instance per language batch.
	/// </summary>
	internal sealed class ProviderManager : IProviderManager
	{
		private readonly SharedMiningContext mShared;
		private readonly ELanguage mLanguage;

		private GameTextTable? mGameTextTable;
		private GameSingletonManager? mResourceManager;
		private Achievements? mAchievements;

		public IFileProvider Provider => mShared.Provider;

		public IReadOnlyDictionary<string, MetaClass>? ClassMetadata => mShared.ClassMetadata;

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
				if (mShared.LootDatabase is null)
				{
					throw new InvalidOperationException("LootDatabase not found. Has a running miner declared a RequireLootDatabase attribute?");
				}
				return mShared.LootDatabase;
			}
		}

		public ProviderManager(SharedMiningContext shared, ELanguage language)
		{
			mShared = shared;
			mLanguage = language;
		}

		/// <summary>
		/// Loads the locale-dependent resources for this language batch. The
		/// shared provider's culture must already be switched to <see cref="mLanguage"/>
		/// (the caller is responsible — see <see cref="SharedMiningContext.SwitchCulture"/>).
		/// </summary>
		public bool Initialize(Logger logger)
		{
			mGameTextTable = GameTextTable.Load(mShared.Provider, logger);
			mResourceManager = GameSingletonManager.Load(mShared.Provider, logger);
			if (mResourceManager is not null)
			{
				mAchievements = Achievements.Load(mResourceManager, logger);
			}
			return true;
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
