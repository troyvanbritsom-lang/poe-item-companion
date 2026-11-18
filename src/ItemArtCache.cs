using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Poe2ItemCompanion
{
    // Local, game-specific inventory art. PoE2's download also contains gems/currency, which are not equipment art.
    // Read in place; never copied. Unique items only use art named after the unique itself:
    // their base art would show the wrong picture, so they stay text-only instead.
    internal sealed class ItemArtCache : IDisposable
    {
        public const string Poe1Folder = "poe 1 active items";
        // Optional second folder with one PNG per unique item name (e.g. "The Stampede.png").
        public const string Poe1UniqueFolder = "poe 1 pic";
        public const string Poe2Folder = "poe_2_images";
        private static readonly HashSet<string> Poe2EquipmentFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Amulet", "Belt", "Body Armour", "Boots", "Bow", "Buckler", "Claw", "Crossbow", "Dagger",
            "FishingRod", "Flail", "Focus", "Gloves", "Helmet", "Jewel", "LifeFlask", "ManaFlask",
            "One Hand Axe", "One Hand Mace", "One Hand Sword", "Quiver", "Ring", "Sceptre", "Shield",
            "Spear", "Staff", "Talisman", "Two Hand Axe", "Two Hand Mace", "Two Hand Sword", "Unique",
            "UtilityFlask", "Wand", "Warstaff"
        };
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Bitmap> _images = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string[] Directories { get; private set; }
        public string Unavailable { get; private set; }   // why there is no art at all, or null
        public int FileCount { get { return _files.Count; } }

        internal static string DiscoverDirectory(GameId game, string applicationDirectory, string folder = null)
        {
            if (game == GameId.Poe1)
            {
                if (folder == null) folder = Poe1Folder;
                if (folder != Poe1Folder && folder != Poe1UniqueFolder) return null;
            }
            else if (game == GameId.Poe2)
            {
                if (folder == null) folder = Poe2Folder;
                if (folder != Poe2Folder) return null;
            }
            else return null;
            // BaseDirectory ends with '\'; without trimming, GetParent would return bin itself instead of the repository root.
            applicationDirectory = applicationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = System.IO.Directory.GetParent(applicationDirectory);
            foreach (string root in parent == null ? new[] { applicationDirectory } : new[] { applicationDirectory, parent.FullName })
            {
                string candidate = Path.Combine(root, folder);
                if (System.IO.Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        public ItemArtCache(GameId game) : this(game,
            DiscoverDirectory(game, AppDomain.CurrentDomain.BaseDirectory),
            game == GameId.Poe1 ? DiscoverDirectory(game, AppDomain.CurrentDomain.BaseDirectory, Poe1UniqueFolder) : null) { }

        internal ItemArtCache(GameId game, params string[] directories)
        {
            Directories = (directories ?? new string[0]).Where(d => d != null && System.IO.Directory.Exists(d)).ToArray();
            if (Directories.Length == 0)
            {
                Unavailable = "Local " + GameIdentity.Name(game) + " item art " +
                    (game == GameId.Poe1 ? "folders '" + Poe1Folder + "' / '" + Poe1UniqueFolder + "'" : "folder '" + Poe2Folder + "'") +
                    " not found; items are shown as text.";
                return;
            }
            try
            {
                foreach (var group in Directories.SelectMany(d => ImageFiles(game, d))
                    .GroupBy(f => Key(Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase))
                {
                    var files = group.ToArray();
                    if (files.Length == 1) _files[group.Key] = files[0];
                    else
                    {
                        _ambiguous.Add(group.Key);
                        Logger.Warn("Ambiguous item art filename: " + group.Key + ". That item will show text only.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)) throw;
                Unavailable = "Cannot index local item art: " + ex.Message;
                Logger.Warn(Unavailable);
            }
        }

        private static IEnumerable<string> ImageFiles(GameId game, string directory)
        {
            if (game == GameId.Poe1) return System.IO.Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories);
            return System.IO.Directory.GetDirectories(directory)
                .Where(d => Poe2EquipmentFolders.Contains(Path.GetFileName(d)))
                .SelectMany(d => System.IO.Directory.GetFiles(d, "*.png", SearchOption.AllDirectories));
        }

        internal static string Key(string name)
        {
            string text = (name ?? "").Replace('_', ' ').Replace("'", "").Replace("\u2019", "");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        // Which art name belongs to this item, or null when it must be text-only.
        public string ArtName(GearEntry gear, out string reason)
        {
            reason = null;
            if (gear == null) return null;
            if (Unavailable != null) { reason = Unavailable; return null; }
            if (gear.FromNotes) { reason = "Guide note, not a concrete item."; return null; }
            bool unique = TradeSearch.TradeRarity(gear.Rarity) == "unique";
            string name = unique ? TradeSearch.UniqueName(gear) : TradeSearch.StripVariant(gear.BaseName);
            if (!string.IsNullOrEmpty(name) && _files.ContainsKey(Key(name))) return name;
            if (!string.IsNullOrEmpty(name) && _ambiguous.Contains(Key(name)))
            {
                reason = "Ambiguous local art for '" + name + "' (multiple matching PNGs); item is shown as text.";
                return null;
            }
            reason = unique ? "No local art named '" + (name ?? gear.ItemName) + "' (unique art is not substituted with base art)."
                : "No local art for base '" + (gear.BaseName ?? "?") + "'.";
            return null;
        }

        public Bitmap Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = Key(name);
            Bitmap image;
            if (_images.TryGetValue(key, out image)) return image;
            string file;
            if (!_files.TryGetValue(key, out file)) return null;
            image = null;
            try
            {
                using (var stream = File.OpenRead(file))
                using (var source = Image.FromStream(stream))
                    image = new Bitmap(source);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                    ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException)) throw;
                Logger.Warn("Cannot load item art " + file + ": " + ex.Message + ". Item will show text only.");
            }
            _images[key] = image;
            return image;
        }

        public void Dispose()
        {
            foreach (var image in _images.Values) if (image != null) image.Dispose();
            _images.Clear();
            _files.Clear();
            _ambiguous.Clear();
        }
    }
}
