using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Poe2ItemCompanion
{
    public sealed class CampaignQuest
    {
        public string Game { get; set; }
        public string Chapter { get; set; }
        public string Name { get; set; }
        public bool Optional { get; set; }
        public string Reward { get; set; }
        public string Requirement { get; set; }
        public string Page { get; set; }
        public string Id { get { return Game + "|" + (Page ?? Name); } }
        public string SourceUrl
        {
            get { return "https://www." + (Game == "poe1" ? "poewiki.net" : "poe2wiki.net") + "/wiki/" + Uri.EscapeDataString((Page ?? Name).Replace(' ', '_')); }
        }
    }

    public static class QuestCatalog
    {
        public const string Notice = "Offline wiki facts checked 2026-11-15; patches may change rewards. Choices are alternatives, not all granted. Completion is not detected.";
        public const string Coverage = "Campaign only: PoE1 Acts 1-10 and introductions; PoE2 Acts 1-4 + Interludes. No maps/endgame, retired Cruel repeat or unreleased Acts 5-6. Act 4 rotating requirements stay locked; follow your quest log.";
        private static readonly Lazy<List<CampaignQuest>> Data = new Lazy<List<CampaignQuest>>(Load);

        private static List<CampaignQuest> Load()
        {
            try
            {
                var quests = new JavaScriptSerializer().Deserialize<List<CampaignQuest>>(File.ReadAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "campaign_quests.json")));
                if (quests == null || quests.Count == 0 || quests.Any(q => q == null
                    || (q.Game != "poe1" && q.Game != "poe2") || string.IsNullOrWhiteSpace(q.Chapter)
                    || string.IsNullOrWhiteSpace(q.Name) || string.IsNullOrWhiteSpace(q.Reward) || string.IsNullOrWhiteSpace(q.Requirement))
                    || quests.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != quests.Count)
                    throw new InvalidDataException("Invalid or duplicate campaign quest records.");
                return quests;
            }
            catch (Exception ex) { Logger.Error("Cannot load campaign quest data.", ex); throw; }
        }

        public static List<CampaignQuest> ForGame(GameId game)
        {
            if (!Enum.IsDefined(typeof(GameId), game)) throw new ArgumentOutOfRangeException("game");
            string key = game == GameId.Poe1 ? "poe1" : "poe2";
            return Data.Value.Where(q => q.Game == key).ToList();
        }
    }

    public sealed class QuestPlan
    {
        private readonly AppConfig _config;
        public readonly List<CampaignQuest> Quests;
        public event EventHandler Changed;
        public string StoryChapter { get; private set; }
        public string StoryStatus { get; private set; }

        public QuestPlan(AppConfig config)
        {
            _config = config;
            Quests = QuestCatalog.ForGame(config.Game);
            StoryStatus = "Game log: waiting for a recognized campaign area.";
        }

        public void UpdateProgress(GameLogState state, string error)
        {
            if (state != null && state.Game != _config.Game)
                throw new ArgumentException("Story progress belongs to another game.", "state");
            string chapter = null;
            string status;
            if (error != null)
            {
                chapter = StoryChapter;
                status = "Story follow unavailable: " + error;
            }
            else if (state == null || state.Phase == GamePhase.Unknown)
                status = "Game log: waiting for a recognized campaign area.";
            else
            {
                chapter = state.Phase == GamePhase.Interlude ? "Interludes" : GameLog.PhaseText(state.Phase);
                status = state.Phase == GamePhase.Endgame ? "Game log: in maps; no campaign quests here."
                    : "Game log: " + chapter + " (last recognized story area).";
            }
            if (chapter == StoryChapter && status == StoryStatus) return;
            StoryChapter = chapter;
            StoryStatus = status;
            OnChanged();
        }

        private void OnChanged()
        {
            var changed = Changed;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        public bool IsIncluded(CampaignQuest quest)
        {
            return !quest.Optional || !_config.ExcludedQuestIds.Contains(quest.Id);
        }

        public IEnumerable<CampaignQuest> Visible(bool showExcluded, string chapter)
        {
            return Quests.Where(q => (chapter == null || q.Chapter == chapter) && (showExcluded || IsIncluded(q)));
        }

        public void SetIncluded(CampaignQuest quest, bool included)
        {
            if (!Quests.Contains(quest)) throw new ArgumentException("Quest belongs to another route.", "quest");
            if (!quest.Optional && !included) throw new InvalidOperationException("Main-story and potentially required quests cannot be excluded.");
            if (IsIncluded(quest) == included) return;
            var before = new List<string>(_config.ExcludedQuestIds);
            if (included) _config.ExcludedQuestIds.RemoveAll(id => id == quest.Id);
            else _config.ExcludedQuestIds.Add(quest.Id);
            try { _config.Save(); }
            catch { _config.ExcludedQuestIds = before; throw; }
            OnChanged();
        }
    }
}
