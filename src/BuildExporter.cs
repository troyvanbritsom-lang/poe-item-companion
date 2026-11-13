using System;
using System.IO;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // Explicit, user-triggered export: writes a NEW PoB2 build file containing the loaded baseline build
    // with the candidate item equipped in one slot. The source build (file, share code or imported
    // character) is never modified, and existing files are never overwritten.
    public static class BuildExporter
    {
        public static void ExportInteractive(IWin32Window owner, PobLuaEngine engine, AppConfig config, string itemText, string slotName, string buildName)
        {
            if (engine == null || !engine.IsBuildLoaded)
            {
                MessageBox.Show(owner, "No build is loaded.", "Export PoB build", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(itemText) || string.IsNullOrEmpty(slotName))
            {
                MessageBox.Show(owner, "Compare an item first, then export it.", "Export PoB build", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string err;
            ExportResult r;
            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                var current = config.LoadConfiguredBuild(engine, out err);
                r = current != null && current.Success ? engine.ExportWithCandidate(itemText, slotName, out err) : null;
            }
            finally { Cursor.Current = old; }
            if (r == null || !r.Success || string.IsNullOrEmpty(r.Xml))
            {
                MessageBox.Show(owner, "Could not create the " + config.GameName + " PoB build:\n" + (err ?? (r != null ? r.Error : "unknown error")) + "\n\nNothing was written.", "Export PoB build", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string exportDir = AppConfig.GetExportFolder();
            try { Directory.CreateDirectory(exportDir); }
            catch (Exception ex) { MessageBox.Show(owner, "Could not create the export folder: " + ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            string sourcePath = engine.CurrentBuildPath;
            string written;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save NEW " + config.GameName + " PoB build (existing files are never overwritten)";
                sfd.Filter = "PoB build files (*.xml)|*.xml";
                sfd.InitialDirectory = exportDir;
                sfd.FileName = PobLuaEngine.SuggestExportFileName(buildName, r.ItemName, r.SlotLabel);
                sfd.OverwritePrompt = false;
                sfd.AddExtension = true;
                sfd.DefaultExt = "xml";
                if (sfd.ShowDialog(owner) != DialogResult.OK) return;
                written = PobLuaEngine.WriteNewBuildFile(r.Xml, sfd.FileName, sourcePath, out err);
            }
            if (written == null)
            {
                MessageBox.Show(owner, err ?? "Could not write the file.", "Export PoB build", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string msg = "New " + config.GameName + " PoB build written:\n" + written +
                "\n\n" + r.ItemName + " equipped in " + r.SlotLabel + " (replacing " + r.ReplacedItemName + ")." +
                "\nVerified by reloading in the matching PoB: Level " + r.ValidatedLevel + ", Life " + r.ValidatedLife.ToString("N0") + ", Total DPS " + r.ValidatedTotalDPS.ToString("N1") + "." +
                "\n\nThe source build was not modified. To open it in the matching PoB, either save it into that PoB's build folder, " +
                "or use Import/Export Build > Import from code." +
                "\n\nCopy a matching PoB import code for this new build to the clipboard?";
            if (MessageBox.Show(owner, msg, "Export PoB build", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Clipboard.SetText(PobShareCode.Encode(r.Xml)); }
                catch (Exception ex) { MessageBox.Show(owner, "Could not copy to clipboard: " + ex.Message, "Export PoB build", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }
}
