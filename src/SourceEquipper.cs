using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    public class EquipResult
    {
        public ExportResult Export { get; set; }
        public string SourcePath { get; set; }
        public string BackupPath { get; set; }
    }

    // Explicit, user-confirmed: equips the candidate into the Current character SOURCE build file.
    // The original file is backed up first, the new XML comes from PoB2's own SaveDB and is verified by
    // reloading it; on any failure the original bytes are restored. Path of Building 2 does not watch
    // build files, so an already open PoB2 must reopen the build to see the change.
    public static class SourceEquipper
    {
        public static string GetBackupFolder()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
        }

        public static EquipResult EquipIntoSource(PobLuaEngine engine, AppConfig config, string itemText, string slotName, out string error)
        {
            error = null;
            if (engine == null) { error = "Engine not available."; return null; }
            if (engine.Game != config.Game) { error = "Selected game and source-write engine disagree. Nothing was written."; return null; }
            if (string.IsNullOrEmpty(itemText) || string.IsNullOrEmpty(slotName)) { error = "Compare an item first."; return null; }
            if (!string.IsNullOrWhiteSpace(config.CurrentCharacterCode))
            {
                error = "The Current character comes from an import code, so there is no source file to change. " +
                    "Select the Current character build file instead, or use Export as new PoB build.";
                return null;
            }
            string path = config.CurrentBuildFilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { error = "No Current character build file is selected."; return null; }
            path = Path.GetFullPath(path);
            if (config.BuildSource == AppConfig.SourceFile && !string.IsNullOrEmpty(config.BuildFilePath) &&
                string.Equals(path, Path.GetFullPath(config.BuildFilePath), StringComparison.OrdinalIgnoreCase))
            { error = "The Current character file is also the Guide file; refusing to change the guide."; return null; }

            byte[] original;
            try { original = File.ReadAllBytes(path); }
            catch (Exception ex) { error = "Could not read the source build: " + ex.Message; return null; }

            // Build the new XML from exactly what is on disk now.
            var loaded = engine.LoadBuildFile(path, out error);
            if (loaded == null || !loaded.Success) return null;
            var export = engine.ExportWithCandidate(itemText, slotName, out error);
            if (export == null || !export.Success || string.IsNullOrEmpty(export.Xml))
            {
                if (error == null) error = export != null ? export.Error : "Could not create the build.";
                return null;
            }

            string backup;
            try
            {
                if (!BytesEqual(original, File.ReadAllBytes(path)))
                { error = "The source build changed on disk while preparing (was it saved in PoB?). Nothing was written; compare again."; return null; }
                Directory.CreateDirectory(GetBackupFolder());
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                backup = PobLuaEngine.MakeUniquePath(Path.Combine(GetBackupFolder(), Path.GetFileNameWithoutExtension(path) + " " + stamp + ".xml"));
                using (var fs = new FileStream(backup, FileMode.CreateNew, FileAccess.Write)) fs.Write(original, 0, original.Length);
                if (!BytesEqual(original, File.ReadAllBytes(backup))) { error = "Backup verification failed. Nothing was written."; return null; }

                string temp = path + ".companion-tmp";
                File.WriteAllText(temp, export.Xml, new UTF8Encoding(false));
                File.Replace(temp, path, null);
                Logger.Info("Equipped '" + export.ItemName + "' in " + slotName + " of source build " + path + " (backup: " + backup + ")");
            }
            catch (Exception ex)
            {
                error = "Could not write the source build: " + ex.Message + ". The original file is unchanged.";
                Logger.Error(error, ex);
                return null;
            }

            // Verify the written file and make it the comparison baseline.
            string verifyError = null;
            var check = config.LoadConfiguredBuild(engine, out verifyError);
            string equipped = null;
            if (check != null && check.Success)
            {
                var details = engine.GetBuildDetails(out verifyError);
                if (details != null && details.Success)
                    foreach (var g in details.Gear) if (g.SlotName == slotName) equipped = g.ItemName;
            }
            if (equipped != export.ItemName)
            {
                try { File.WriteAllBytes(path, original); } catch (Exception ex) { Logger.Error("Restore failed", ex); }
                string ignored;
                config.LoadConfiguredBuild(engine, out ignored);
                error = "Verification failed (" + (verifyError ?? "slot contains '" + equipped + "'") + "). The original build was restored; backup: " + backup;
                Logger.Error(error);
                return null;
            }
            return new EquipResult { Export = export, SourcePath = path, BackupPath = backup };
        }

        public static bool EquipInteractive(IWin32Window owner, PobLuaEngine engine, AppConfig config, string itemText, string slotName, string slotLabel)
        {
            const string title = "Equip into Current character build";
            string path = config.CurrentBuildFilePath;
            if (!string.IsNullOrWhiteSpace(config.CurrentCharacterCode) || string.IsNullOrWhiteSpace(path))
            {
                string e;
                EquipIntoSource(engine, config, itemText, slotName, out e);
                MessageBox.Show(owner, e, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            string confirm = "Equip this item in " + (slotLabel ?? slotName) + " and SAVE it into your Current character build file?\n\n" + path +
                "\n\nA backup of the original is saved first in:\n" + GetBackupFolder() +
                "\n\nReopen this build in the matching Path of Building afterwards. " +
                "If it is already open there, do not save it before reopening, or PoB will overwrite this change.";
            if (MessageBox.Show(owner, confirm, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return false;

            string err;
            EquipResult r;
            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try { r = EquipIntoSource(engine, config, itemText, slotName, out err); }
            finally { Cursor.Current = old; }
            if (r == null)
            {
                MessageBox.Show(owner, "Not equipped:\n" + err, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            MessageBox.Show(owner, r.Export.ItemName + " is now equipped in " + r.Export.SlotLabel + " (replaced " + r.Export.ReplacedItemName + ").\n\n" +
                "Saved: " + r.SourcePath + "\nBackup: " + r.BackupPath +
                "\n\nComparisons now use the updated Current character. Reopen the build in the matching PoB to see it there.",
                title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
