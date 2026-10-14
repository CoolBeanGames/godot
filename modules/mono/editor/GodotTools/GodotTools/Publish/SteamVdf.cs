using System.Text;

namespace GodotTools.Publish
{
    /// <summary>
    /// Generates the SteamPipe app_build VDF consumed by
    /// `steamcmd +run_app_build`. Pure string logic (BCL only).
    /// </summary>
    public static class SteamVdf
    {
        public static string AppBuild(uint appId, string desc, string buildOutputDir,
            string contentRootDir, uint depotId, string? fileExclusion = "*.pdb")
        {
            var sb = new StringBuilder();
            sb.AppendLine("\"appbuild\"");
            sb.AppendLine("{");
            sb.AppendLine($"\t\"appid\"\t\t\"{appId}\"");
            sb.AppendLine($"\t\"desc\"\t\t\"{Escape(desc)}\"");
            sb.AppendLine($"\t\"buildoutput\"\t\"{Escape(buildOutputDir)}\"");
            sb.AppendLine($"\t\"contentroot\"\t\"{Escape(contentRootDir)}\"");
            sb.AppendLine("\t\"setlive\"\t\"\"");
            sb.AppendLine("\t\"preview\"\t\"0\"");
            sb.AppendLine("\t\"local\"\t\t\"\"");
            sb.AppendLine("\t\"depots\"");
            sb.AppendLine("\t{");
            sb.AppendLine($"\t\t\"{depotId}\"");
            sb.AppendLine("\t\t{");
            if (!string.IsNullOrWhiteSpace(fileExclusion))
                sb.AppendLine($"\t\t\t\"FileExclusion\"\t\"{Escape(fileExclusion)}\"");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
